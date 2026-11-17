#!/usr/bin/env python3
"""Stage a release and supervise an independent Azure-side deployment transaction."""

import argparse
import json
import os
from pathlib import Path
import subprocess
import time
import urllib.error
import urllib.request
import uuid

from webapp_deployment_worker import START_CHECKPOINTS, TERMINAL, AzureApp, digest, package_files


def az(*arguments):
    return json.loads(subprocess.check_output(["az", *arguments, "--output", "json"], text=True))


class Kudu:
    def __init__(self, url, token):
        self.url, self.token = url, token

    def request(self, method, path, data=None, *, timeout=120, retry_status_write=False):
        headers = {"Authorization": "Bearer " + self.token}
        if path.startswith("/api/vfs/") and method in {"PUT", "DELETE"}:
            # Each job owns its staging files. Kudu otherwise rejects updates
            # and credential cleanup when a file already has an ETag.
            headers["If-Match"] = "*"
        if isinstance(data, dict):
            data = json.dumps(data).encode()
            headers["Content-Type"] = "application/json"
        request = urllib.request.Request(self.url + path, data=data, method=method, headers=headers)
        # SCM can time out while the application remains healthy. Reads can be
        # retried safely; command replies must be reconciled with the saved job
        # instead of launching a second activation after a lost reply.
        attempts = 3 if method == "GET" or retry_status_write else 1
        for attempt in range(1, attempts + 1):
            try:
                with urllib.request.urlopen(request, timeout=timeout) as response:
                    body = response.read()
                    return json.loads(body) if body else None
            except urllib.error.HTTPError as error:
                if attempts == 1 or error.code not in {408, 429, 500, 502, 503, 504}:
                    raise
                reason = "HTTP " + str(error.code)
                error.close()
                if attempt == attempts:
                    print("Kudu " + method + " " + path + ": " + reason + "; retries exhausted", flush=True)
                    raise
            except (OSError, urllib.error.URLError) as error:
                if attempts == 1:
                    raise
                reason = type(error).__name__
                if attempt == attempts:
                    raise OSError("Kudu " + method + " " + path + " failed after " + str(attempts) +
                                  " attempts (" + reason + ")") from error
            delay = 5 * 2 ** (attempt - 1)
            print("Kudu " + method + " " + path + ": " + reason + "; retrying in " + str(delay) +
                  "s (attempt " + str(attempt + 1) + "/" + str(attempts) + ")", flush=True)
            time.sleep(delay)

    def put(self, job, name, data):
        return self.request("PUT", "/api/vfs/data/schink-deployments/" + job + "/" + name, data)

    def state(self, job):
        return self.request("GET", "/api/vfs/data/schink-deployments/" + job + "/state.json", timeout=15)

    def record_start_result(self, job, result):
        # A retry overwrites the same job's checkpoint with the same verified
        # result. This remains safe after a lost PUT reply during SCM restart.
        return self.request("PUT", "/api/vfs/data/schink-deployments/" + job + "/state.json",
                            result, timeout=15, retry_status_write=True)

    def launch(self, job, authorization, recover=False):
        # Kudu's command parser supports double quotes. Interpolated paths
        # contain only generated UUIDs, never user input or credentials.
        root = "/home/data/schink-deployments/" + job
        log = "recovery.log" if recover else "worker.log"
        command = ('bash -c "nohup python3 ' + root + '/worker.py ' + root + ' ' + root + '/' +
                   authorization + (' --recover' if recover else '') +
                   ' >> ' + root + '/' + log + ' 2>&1 < /dev/null &"')
        return self.request("POST", "/api/command", {"command": command, "dir": "/home"})


def wait_for_job(kudu, job, timeout=1200, interval=10):
    deadline = time.monotonic() + timeout
    previous = None
    while time.monotonic() < deadline:
        try:
            state = kudu.state(job)
            phase = state["phase"]
            if phase != previous:
                print("Deployment " + job + ": " + phase, flush=True)
                previous = phase
            if phase in TERMINAL or phase in START_CHECKPOINTS:
                return state
        except urllib.error.HTTPError as error:
            error.close()
        except (OSError, urllib.error.URLError, json.JSONDecodeError):
            # A lost HTTP reply does not cancel the worker. Reconcile this job;
            # never activate a second copy based on /deployments/latest.
            pass
        time.sleep(interval)
    raise TimeoutError("Worker status unavailable; use --recover with the saved state file. "
                       "The Azure-side transaction continues independently.")


def deploy(kudu, site, package, state_file, release_id=None):
    if site.get("scmSiteAlsoStopped") is not False:
        raise ValueError("Deployment requires scmSiteAlsoStopped=false; app was not stopped")
    if site["state"] not in {"Running", "Stopped"}:
        raise ValueError("Refusing deployment while app state is " + site["state"])
    files = package_files(package)
    deployments = kudu.request("GET", "/api/deployments")
    if any(not item.get("complete", False) or item.get("status") in {0, 1, 2} for item in deployments):
        raise ValueError("An existing Kudu deployment must finish before activation")
    job = uuid.uuid4().hex
    # Persist the recovery locator before anything can stop the app.
    state_file.parent.mkdir(parents=True, exist_ok=True)
    state_file.write_text(json.dumps({"job": job, "resource_group": site["resourceGroup"],
                                     "app_name": site["name"]}))
    kudu.put(job, "", b"")
    kudu.put(job, "candidate.zip", package.read_bytes())
    kudu.put(job, "worker.py", Path(__file__).with_name("webapp_deployment_worker.py").read_bytes())
    kudu.put(job, "settings.json", {"files": files, "package_sha256": digest(package),
                                   "original_state": site["state"], "resource_id": site["id"],
                                   "release_id": release_id,
                                   "health_url": "https://" + site["defaultHostName"] + "/healthz"})
    kudu.put(job, "state.json", {"phase": "uploaded"})
    return job


def launch_and_wait(kudu, job, recover=False):
    authorization = "authorization-" + uuid.uuid4().hex
    access = az("account", "get-access-token", "--resource", "https://management.azure.com/")
    try:
        kudu.put(job, authorization, {"token": access["accessToken"], "expires_on": int(access["expires_on"])})
        if recover and kudu.state(job)["phase"] == "recovery_failed":
            # Do not mistake the previous attempt's terminal status for this
            # retry's result, or delete its authorization file before it starts.
            kudu.put(job, "state.json", {"phase": "recovery_requested"})
        try:
            result = kudu.launch(job, authorization, recover)
            if result and result.get("ExitCode", 0) != 0:
                raise RuntimeError("Could not launch deployment worker")
        except urllib.error.HTTPError as error:
            error.close()
            print("Launch reply was lost; checking the saved deployment job.", flush=True)
        except (OSError, urllib.error.URLError):
            print("Launch reply was lost; checking the saved deployment job.", flush=True)
        return wait_for_job(kudu, job)
    finally:
        try:
            kudu.request("DELETE", "/api/vfs/data/schink-deployments/" + job + "/" + authorization)
        except urllib.error.HTTPError as error:
            error.close()
            if error.code != 404:
                print("Authorization cleanup failed with HTTP " + str(error.code), flush=True)
        except (OSError, urllib.error.URLError):
            pass  # Normally already removed by the worker.


def complete_start(kudu, job, app, checkpoint):
    # The worker has released its lock after verifying the files. Use ARM and
    # the direct app endpoint so a Kudu restart cannot interrupt verification.
    state = app.state()
    if state == "Stopped":
        app.start()
    elif state not in {"Running", "Starting"}:
        raise ValueError("Unexpected app state before startup verification: " + state)

    rollback = checkpoint["phase"] == "rollback_ready"
    try:
        app.check_health(checkpoint["release_id"])
    except TimeoutError as error:
        if rollback:
            raise  # Keep the rollback checkpoint so recovery can retry startup.
        result = {"phase": "candidate_unhealthy", "error": str(error)}
    else:
        if rollback:
            result = {"phase": "rolled_back", "error": checkpoint["error"]}
        else:
            result = {"phase": "succeeded"}

    # Kudu may still be warming up. A failed status write leaves the same
    # recoverable checkpoint, and the recovery step can recheck the running app.
    try:
        kudu.record_start_result(job, result)
    except OSError:
        print("Kudu could not record " + result["phase"] + "; the startup checkpoint remains recoverable.",
              flush=True)
        raise
    return result


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("resource_group", nargs="?")
    parser.add_argument("app_name", nargs="?")
    parser.add_argument("package", nargs="?", type=Path)
    parser.add_argument("--release-id", type=uuid.UUID, help="Copy from the published app's --deployment-id output")
    parser.add_argument("--recover", type=Path)
    parser.add_argument("--require-activation", action="store_true",
                        help="Fail recovery unless the candidate release was activated")
    parser.add_argument("--state-file", type=Path, default=Path(os.environ.get(
        "SHINK_DEPLOY_STATE_FILE", ".artifacts/webapp-deployment-job.json")))
    args = parser.parse_args()
    if args.require_activation and not args.recover:
        parser.error("--require-activation requires --recover")
    recovery_success = {"succeeded"}
    if not args.require_activation:
        recovery_success.update({"rolled_back", "failed_preparation"})
    if args.recover:
        if not args.recover.exists():
            print("No staged deployment needs recovery.")
            return int(args.require_activation)
        locator = json.loads(args.recover.read_text())
        resource_group, app_name = locator["resource_group"], locator["app_name"]
        job = locator["job"]
        if len(job) != 32 or any(character not in "0123456789abcdef" for character in job):
            raise ValueError("Invalid recovery job ID")
    else:
        if not args.resource_group or not args.app_name or not args.package or not args.package.is_file() or not args.release_id:
            parser.error("RESOURCE_GROUP APP_NAME PACKAGE_ZIP and --release-id are required")
        resource_group, app_name = args.resource_group, args.app_name
    site = az("webapp", "show", "--resource-group", resource_group, "--name", app_name)
    scm_hosts = [host["name"] for host in site["hostNameSslStates"] if host["hostType"] == "Repository"]
    if len(scm_hosts) != 1:
        raise ValueError("Could not determine the app's SCM hostname")
    access = az("account", "get-access-token", "--resource", "https://management.azure.com/")
    kudu = Kudu("https://" + scm_hosts[0], access["accessToken"])
    app = AzureApp(site["id"], "https://" + site["defaultHostName"] + "/healthz",
                   access["accessToken"], int(access["expires_on"]))
    if args.recover:
        try:
            state = kudu.state(job)
        except urllib.error.HTTPError as error:
            if error.code == 404:
                print("Deployment was interrupted before activation was prepared.")
                return int(args.require_activation)
            raise
        if state["phase"] in TERMINAL - {"recovery_failed"}:
            print("Deployment already finished: " + state["phase"])
            return int(state["phase"] not in recovery_success)
        if state["phase"] in START_CHECKPOINTS:
            state = complete_start(kudu, job, app, state)
            if state["phase"] in {"succeeded", "rolled_back"}:
                print(json.dumps(state), flush=True)
                return int(state["phase"] not in recovery_success)
    else:
        job = deploy(kudu, site, args.package, args.state_file, str(args.release_id))
    state = launch_and_wait(kudu, job, recover=bool(args.recover))
    if state["phase"] in START_CHECKPOINTS:
        state = complete_start(kudu, job, app, state)
    print(json.dumps(state), flush=True)
    accepted = recovery_success if args.recover else {"succeeded"}
    return int(state["phase"] not in accepted)


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (OSError, ValueError, KeyError, RuntimeError, subprocess.CalledProcessError) as error:
        raise SystemExit("Deployment failed: " + str(error))
