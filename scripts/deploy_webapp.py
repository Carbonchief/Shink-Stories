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

from webapp_deployment_worker import TERMINAL, digest, package_files


def az(*arguments):
    return json.loads(subprocess.check_output(["az", *arguments, "--output", "json"], text=True))


class Kudu:
    def __init__(self, url, token):
        self.url, self.token = url, token

    def request(self, method, path, data=None):
        headers = {"Authorization": "Bearer " + self.token}
        if method in {"PUT", "DELETE"}:
            headers["If-Match"] = "*"
        if isinstance(data, dict):
            data = json.dumps(data).encode()
            headers["Content-Type"] = "application/json"
        request = urllib.request.Request(self.url + path, data=data, method=method, headers=headers)
        with urllib.request.urlopen(request, timeout=120) as response:
            body = response.read()
            return json.loads(body) if body else None

    def put(self, job, name, data):
        return self.request("PUT", "/api/vfs/data/schink-deployments/" + job + "/" + name, data)

    def state(self, job):
        return self.request("GET", "/api/vfs/data/schink-deployments/" + job + "/state.json")

    def launch(self, job, authorization, recover=False):
        # Kudu's command parser supports double quotes. Interpolated paths
        # contain only generated UUIDs, never user input or credentials.
        root = "/home/data/schink-deployments/" + job
        command = ('bash -c "nohup python3 ' + root + '/worker.py ' + root + ' ' + root + '/' +
                   authorization + (' --recover' if recover else '') +
                   ' > ' + root + '/worker.log 2>&1 < /dev/null &"')
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
            if phase in TERMINAL:
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


def deploy(kudu, site, package, state_file):
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
        except (OSError, urllib.error.URLError):
            pass  # Normally already removed by the worker.


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("resource_group", nargs="?")
    parser.add_argument("app_name", nargs="?")
    parser.add_argument("package", nargs="?", type=Path)
    parser.add_argument("--recover", type=Path)
    parser.add_argument("--state-file", type=Path, default=Path(os.environ.get(
        "SHINK_DEPLOY_STATE_FILE", ".artifacts/webapp-deployment-job.json")))
    args = parser.parse_args()
    if args.recover:
        if not args.recover.exists():
            print("No staged deployment needs recovery.")
            return 0
        locator = json.loads(args.recover.read_text())
        resource_group, app_name = locator["resource_group"], locator["app_name"]
        job = locator["job"]
        if len(job) != 32 or any(character not in "0123456789abcdef" for character in job):
            raise ValueError("Invalid recovery job ID")
    else:
        if not args.resource_group or not args.app_name or not args.package or not args.package.is_file():
            parser.error("RESOURCE_GROUP APP_NAME PACKAGE_ZIP are required")
        resource_group, app_name = args.resource_group, args.app_name
    site = az("webapp", "show", "--resource-group", resource_group, "--name", app_name)
    scm_hosts = [host["name"] for host in site["hostNameSslStates"] if host["hostType"] == "Repository"]
    if len(scm_hosts) != 1:
        raise ValueError("Could not determine the app's SCM hostname")
    token = az("account", "get-access-token", "--resource", "https://management.azure.com/")["accessToken"]
    kudu = Kudu("https://" + scm_hosts[0], token)
    if args.recover:
        try:
            state = kudu.state(job)
        except urllib.error.HTTPError as error:
            if error.code == 404:
                print("Deployment was interrupted before activation was prepared.")
                return 0
            raise
        if state["phase"] in TERMINAL - {"recovery_failed"}:
            print("Deployment already finished: " + state["phase"])
            return 0 if state["phase"] in {"succeeded", "rolled_back", "failed_preparation"} else 1
    else:
        job = deploy(kudu, site, args.package, args.state_file)
    state = launch_and_wait(kudu, job, recover=bool(args.recover))
    print(json.dumps(state), flush=True)
    return 0 if state["phase"] == "succeeded" or (args.recover and state["phase"] == "rolled_back") else 1


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (OSError, ValueError, KeyError, RuntimeError, subprocess.CalledProcessError) as error:
        raise SystemExit("Deployment failed: " + str(error))
