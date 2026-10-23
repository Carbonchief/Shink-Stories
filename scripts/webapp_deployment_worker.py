#!/usr/bin/env python3
"""Detached Azure-side activation with a durable rollback journal. Standard library only."""

import argparse
import base64
import fcntl
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import shutil
import signal
import stat
import tempfile
import time
import urllib.error
import urllib.request
import zipfile


TERMINAL = {"succeeded", "rolled_back", "failed_preparation", "recovery_failed"}
REQUIRED = {"Shink.dll", "Shink.deps.json", "Shink.runtimeconfig.json",
            "Shink.staticwebassets.endpoints.json"}


def stream_digest(stream):
    checksum = hashlib.sha256()
    for chunk in iter(lambda: stream.read(1024 * 1024), b""):
        checksum.update(chunk)
    return checksum.hexdigest()


def digest(path):
    with Path(path).open("rb") as stream:
        return stream_digest(stream)


def safe_path(root, name):
    parts = PurePosixPath(name)
    if not name or "\\" in name or parts.is_absolute() or ".." in parts.parts:
        raise ValueError("Unsafe package path")
    root = Path(root).resolve()
    path = root.joinpath(*parts.parts)
    for parent in [path, *path.parents]:
        if parent == root:
            break
        if parent.is_symlink():
            raise ValueError("Deployment paths must not contain symlinks")
    if not path.resolve().is_relative_to(root) or path == root:
        raise ValueError("Package path is outside its root")
    return path


def package_files(package):
    files = {}
    with zipfile.ZipFile(package) as archive:
        for entry in archive.infolist():
            safe_path(Path(tempfile.gettempdir()) / "shink-package-check", entry.filename)
            if stat.S_ISLNK(entry.external_attr >> 16):
                raise ValueError("Package contains a symlink")
            if entry.is_dir():
                continue
            if entry.filename != str(PurePosixPath(entry.filename)) or entry.filename in files:
                raise ValueError("Package contains duplicate or non-canonical paths")
            with archive.open(entry) as stream:
                checksum = stream_digest(stream)
            files[entry.filename] = checksum
    if not REQUIRED.issubset(files):
        raise ValueError("Package is missing required published application files")
    return files


def verify_static_assets(root):
    root = Path(root)
    manifest = json.loads((root / "Shink.staticwebassets.endpoints.json").read_text())
    if manifest["ManifestType"] != "Publish":
        raise ValueError("A publish static asset manifest is required")
    checked = {}
    for endpoint in manifest["Endpoints"]:
        name = endpoint["AssetFile"]
        path = safe_path(root / "wwwroot", name)
        if name not in checked:
            checked[name] = (path.stat().st_size, '"' + base64.b64encode(
                bytes.fromhex(digest(path))).decode() + '"')
        headers = {h["Name"].lower(): h["Value"] for h in endpoint["ResponseHeaders"]
                   if h["Name"].lower() != "etag" or not h["Value"].startswith("W/")}
        if (int(headers.get("content-length", -1)), headers.get("etag")) != checked[name]:
            raise ValueError("Static asset does not match its publish manifest: " + name)


def verify_files(root, files):
    for name, expected in files.items():
        path = safe_path(root, name)
        if expected is None:
            if path.exists():
                raise ValueError("Unexpected file after rollback: " + name)
        elif not path.is_file() or digest(path) != expected:
            raise ValueError("Published file checksum mismatch: " + name)


def atomic_copy(source, target):
    target.parent.mkdir(parents=True, exist_ok=True)
    descriptor, temporary = tempfile.mkstemp(prefix=".schink-copy-", dir=target.parent)
    os.close(descriptor)
    try:
        shutil.copy2(source, temporary)
        os.replace(temporary, target)
    finally:
        Path(temporary).unlink(missing_ok=True)


def write_json(path, value):
    temporary = path.with_suffix(".tmp")
    temporary.write_text(json.dumps(value))
    os.replace(temporary, path)


class AzureApp:
    def __init__(self, resource_id, health_url, token, expires_on):
        if not resource_id.startswith("/subscriptions/") or "/providers/Microsoft.Web/sites/" not in resource_id:
            raise ValueError("Invalid App Service resource ID")
        self.url = "https://management.azure.com" + resource_id
        self.health_url = health_url
        self.token = token
        self.expires_on = expires_on

    def ensure_credentials(self):
        if self.expires_on < time.time() + 1800:
            raise ValueError("ARM authorization must have 30 minutes remaining before stopping the app")

    def arm(self, action=""):
        request = urllib.request.Request(self.url + action + "?api-version=2024-11-01",
                                         data=b"" if action else None,
                                         headers={"Authorization": "Bearer " + self.token})
        with urllib.request.urlopen(request, timeout=30) as response:
            body = response.read()
            return json.loads(body) if body else {}

    def stop(self):
        self.arm("/stop")
        deadline = time.monotonic() + 120
        while time.monotonic() < deadline:
            if self.arm()["properties"]["state"] == "Stopped":
                return
            time.sleep(3)
        raise TimeoutError("App did not confirm Stopped; files were not changed")

    def state(self):
        properties = self.arm()["properties"]
        if properties.get("scmSiteAlsoStopped") is not False:
            raise ValueError("SCM must remain running throughout activation")
        return properties["state"]

    def start(self):
        for attempt in range(3):
            try:
                self.arm("/start")
                return
            except (OSError, urllib.error.URLError):
                if attempt == 2:
                    raise
                time.sleep(5)

    def check_health(self):
        deadline = time.monotonic() + 240
        while time.monotonic() < deadline:
            try:
                request = urllib.request.Request(self.health_url, headers={
                    "User-Agent": "Schink-Deployment-Health/1.0", "Cache-Control": "no-cache"})
                with urllib.request.urlopen(request, timeout=10) as response:
                    if response.status == 200 and response.read(16).strip() == b"ok":
                        return
            except (OSError, urllib.error.URLError):
                pass
            time.sleep(5)
        raise TimeoutError("Application did not pass /healthz after startup")


def rollback(job, root, journal, app):
    # Verify every backup before touching wwwroot. Never restart a mixture.
    backup = job / "backup"
    verify_files(backup, {name: value for name, value in journal["previous"].items() if value is not None})
    app.stop()
    for name, previous in journal["previous"].items():
        target = safe_path(root, name)
        if previous is None:
            target.unlink(missing_ok=True)
        else:
            atomic_copy(safe_path(backup, name), target)
    verify_files(root, journal["previous"])
    verify_static_assets(root)
    if journal["original_state"] == "Running":
        app.start()
        app.check_health()


def transaction(job, root, app, recover=False):
    job, root = Path(job), Path(root)
    settings = json.loads((job / "settings.json").read_text())
    state_path, journal_path = job / "state.json", job / "journal.json"
    journal = None
    stop_attempted = False
    scratch = None

    def phase(value, **details):
        write_json(state_path, {"phase": value, **details})

    def recovery_phase(value, **details):
        try:
            phase(value, **details)
        except OSError:
            # A full disk or a failed status write must not prevent restoration.
            print("Could not persist deployment recovery status: " + value, flush=True)

    with (job.parent / ".activation.lock").open("a") as lock:
        try:
            fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError:
            return 2  # Another worker owns activation; do not overwrite its state.
        if state_path.exists():
            existing = json.loads(state_path.read_text())["phase"]
            if existing in TERMINAL and not (recover and existing == "recovery_failed"):
                return 0
        try:
            if recover:
                last_phase = json.loads(state_path.read_text())["phase"] if state_path.exists() else "uploaded"
                if last_phase in {"uploaded", "validating"} or not journal_path.exists():
                    phase("failed_preparation", error="Interrupted before any app stop")
                    return 1
                journal = json.loads(journal_path.read_text())
                recovery_phase("rolling_back")
                rollback(job, root, journal, app)
                recovery_phase("rolled_back", error="Recovered an interrupted deployment")
                return 1

            phase("validating")
            scratch = Path(tempfile.mkdtemp(prefix="schink-release-" + job.name + "-"))
            package = scratch / "candidate.zip"
            # Read the uploaded archive from shared storage once. Repeated ZIP
            # scans/extraction/hash checks should use the worker's local disk.
            shutil.copyfile(job / "candidate.zip", package)
            if digest(package) != settings["package_sha256"]:
                raise ValueError("Uploaded ZIP checksum mismatch")
            files = package_files(package)
            if files != settings["files"]:
                raise ValueError("Uploaded package file list mismatch")
            candidate, backup = scratch / "candidate", job / "backup"
            candidate.mkdir()
            backup.mkdir()
            with zipfile.ZipFile(package) as archive:
                for name in files:
                    target = safe_path(candidate, name)
                    target.parent.mkdir(parents=True, exist_ok=True)
                    with archive.open(name) as source, target.open("wb") as destination:
                        shutil.copyfileobj(source, destination)
                    mode = archive.getinfo(name).external_attr >> 16
                    target.chmod(0o755 if mode & 0o111 else 0o644)
            verify_files(candidate, files)
            verify_static_assets(candidate)
            verify_static_assets(root)
            if app.state() != settings["original_state"]:
                raise ValueError("App state changed during staging; refusing activation")
            previous = {}
            for name in files:
                source = safe_path(root, name)
                if source.exists():
                    if not source.is_file():
                        raise ValueError("Package would replace an existing directory")
                    source_hash = digest(source)
                    if source_hash == files[name]:
                        continue  # Do not copy or back up unchanged dependencies.
                    atomic_copy(source, safe_path(backup, name))
                    previous[name] = source_hash
                else:
                    previous[name] = None
            verify_files(backup, {k: v for k, v in previous.items() if v is not None})
            journal = {"previous": previous, "original_state": settings["original_state"]}
            write_json(journal_path, journal)
            if not previous:
                verify_files(root, files)
                if settings["original_state"] == "Running":
                    app.check_health()
                phase("succeeded")
                return 0
            app.ensure_credentials()
            # The extracted files have already been verified. Free the upload
            # and reserve room for the largest atomic replacement before stop.
            package.unlink()
            (job / "candidate.zip").unlink()
            largest = max(safe_path(candidate, name).stat().st_size for name in previous)
            if shutil.disk_usage(root).free < largest + 16 * 1024 * 1024:
                raise OSError("Insufficient free space for activation; app was not stopped")
            phase("stopping")
            stop_attempted = True
            app.stop()
            phase("activating")
            for name in previous:
                atomic_copy(safe_path(candidate, name), safe_path(root, name))
            verify_files(root, files)
            verify_static_assets(root)
            if settings["original_state"] == "Running":
                phase("starting")
                app.start()
                app.check_health()
            phase("succeeded")
            return 0
        except BaseException as error:
            if recover:
                recovery_phase("recovery_failed", error=str(error))
            elif stop_attempted:
                try:
                    if journal is None:
                        raise ValueError("Recovery journal is missing")
                    recovery_phase("rolling_back", error=str(error))
                    rollback(job, root, journal, app)
                    recovery_phase("rolled_back", error=str(error))
                except BaseException as recovery_error:
                    recovery_phase("recovery_failed", error=str(error), recovery_error=str(recovery_error))
            else:
                phase("failed_preparation", error=str(error))
            return 1
        finally:
            if scratch is not None:
                shutil.rmtree(scratch, ignore_errors=True)


def prune_completed_jobs(parent, current, keep=3):
    completed = []
    for directory in parent.iterdir():
        if (len(directory.name) == 32 and all(c in "0123456789abcdef" for c in directory.name)
                and directory.is_dir() and not directory.is_symlink() and (directory / "state.json").exists()):
            if json.loads((directory / "state.json").read_text())["phase"] in {
                    "succeeded", "rolled_back", "failed_preparation"}:
                completed.append(directory)
    for directory in sorted(completed, key=lambda path: path.stat().st_mtime, reverse=True)[keep:]:
        if directory != current:
            shutil.rmtree(directory)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("job", type=Path)
    parser.add_argument("authorization", type=Path)
    parser.add_argument("--recover", action="store_true")
    args = parser.parse_args()
    # Never put the short-lived ARM token in arguments or logs. Remove its SCM
    # staging file before performing any operations.
    try:
        authorization = json.loads(args.authorization.read_text())
    finally:
        args.authorization.unlink(missing_ok=True)
    settings = json.loads((args.job / "settings.json").read_text())
    app = AzureApp(settings["resource_id"], settings["health_url"],
                   authorization["token"], authorization["expires_on"])
    def interrupted(signum, frame):
        raise InterruptedError("Deployment worker interrupted")
    signal.signal(signal.SIGTERM, interrupted)
    result = transaction(args.job, Path("/home/site/wwwroot"), app, args.recover)
    state_path = args.job / "state.json"
    phase = json.loads(state_path.read_text())["phase"] if state_path.exists() else None
    if result != 2 and phase in {"succeeded", "rolled_back", "failed_preparation"}:
        try:
            # The verified snapshot and journal are sufficient for recovery.
            # Avoid retaining multiple copies of large native dependencies.
            shutil.rmtree(args.job / "candidate", ignore_errors=True)
            (args.job / "candidate.zip").unlink(missing_ok=True)
            prune_completed_jobs(args.job.parent, args.job)
        except (OSError, ValueError):
            pass  # Cleanup must not undo an already healthy deployment.
    return result


if __name__ == "__main__":
    raise SystemExit(main())
