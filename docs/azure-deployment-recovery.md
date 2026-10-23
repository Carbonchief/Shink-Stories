# Azure deployment recovery

On 23 October 2026 the scheduled workflow stopped `schink` at 06:29:57 SAST and
submitted its deployment at 06:30:13. Kudu recorded the deployment as failed
because its SCM container restarted, and the caller received HTTP 502 at
06:31:22. Azure's deployment status explicitly warned against management and
deployment operations in quick succession. The overlapping shutdown/startup
timestamps support a stop/deploy sequencing race; the exact Azure internal
recycle trigger was not exposed in the available logs.

The previous script intentionally refused to start an unconfirmed package and
had no automatic rollback. This left the website stopped until its deployed
assets were validated and the app was started. The workflow was disabled during
remediation; the user approved pushing the initial fix and re-enabling it on
23 October.

Incident evidence: [failed workflow run 39900421585](https://github.com/Carbonchief/Shink-Stories/actions/runs/39900421585),
Azure deployment `a468d553-e536-465d-a795-ea631d708f4f` (failed, inactive).

A later [manual run 39918628571](https://github.com/Carbonchief/Shink-Stories/actions/runs/39918628571)
passed publish and migration steps, then failed during activation at 09:39 SAST.
Renaming a temporary file over `Shink.pdb` returned `ENOENT`; the same operation
also blocked rollback. An isolated copy/rename probe on Azure's shared storage
passed, so the exact cause of that rename failure remains unconfirmed. The
independent recovery step then received HTTP 412 while overwriting `state.json`
without Kudu's ETag condition, before its worker could launch. Credential cleanup
also received HTTP 409.

The follow-up below is approved for commit/push only. It has not been deployed or
executed against production as part of this follow-up.

## Protected deployment

`scripts/deploy-webapp-quiesced.sh RESOURCE_GROUP APP_NAME PACKAGE_ZIP` remains the
entry point. It now invokes `deploy_webapp.py`:

1. Upload the package and deployment worker through authenticated SCM VFS into
   `/home/data/schink-deployments/<job-id>`, outside the served application.
   Upload failures leave the running site untouched.
2. Launch an Azure-side worker with `nohup`, closed stdin and redirected output.
   It runs independently of a GitHub runner or an HTTP request. The coordinator
   polls this exact job's durable state instead of relying on a synchronous
   deployment reply or `/deployments/latest`.
3. Copy the ZIP to temporary local disk, then verify the uploaded ZIP, every
   extracted file, and both releases' published
   static asset manifests. Save and hash-check every existing file that the new
   package can overwrite. Leave runtime uploads and legacy audio outside the
   package untouched. Refuse symlinks and unsafe paths. Temporary extraction and
   repeated hashing use local disk; rollback files and journals remain on durable
   shared storage.
4. Require the app's original state, SCM availability, an exclusive activation
   lock, working space for changed files, and at least 30 minutes remaining on the ARM
   authorization before stop. A failed recovery-status write cannot block rollback.
5. Stop the app, then copy only changed application files directly, checking each
   source and destination against its expected checksum. This avoids temporary-file
   renames over served files during both activation and rollback. Verify all
   deployed checksums, restore its original running state and require
   HTTP 200 with body `ok` from `/healthz`. Intentionally stopped apps stay stopped.
6. On a copy, verification, startup or health failure, stop before restoring the
   verified snapshot, remove only newly introduced package files, verify the old
   application, start it if originally running, and check health again. The
   deployment still reports failure when rollback succeeds.

Application file copying is deliberately non-atomic while the app is stopped.
The journal is durable before the first copy, so an interrupted write can be
restored before restart. Backup preparation retains bounded atomic-copy retries;
if that preparation fails, the running application has not been stopped or changed.

Kudu VFS updates and deletes include `If-Match: *` for files owned by the staged
job. This covers resetting failed recovery state and deleting authorization files.
An authorization file already consumed by the worker returns a harmless HTTP 404;
other HTTP cleanup failures are reported without logging credentials.

The short-lived ARM token is passed in an authenticated SCM staging file, removed
as soon as the worker reads it, and is never placed in command arguments or logs.
Unchanged dependencies are not rewritten or backed up. Identical releases require
health verification without an app stop. The last three completed job directories
are retained, with staged ZIPs/extracted candidates removed after a successful
activation, rollback, or preparation failure; active jobs and failed recovery
snapshots are preserved.

The workflow also runs an independent `always()` recovery step. If a worker
process itself dies during activation, its durable journal can restore the old
package without guessing which files were replaced. The normal activation lock
prevents a recovery process from racing a worker that is still running.

## Manual recovery

The coordinator saves the job locator in the path passed through `--state-file`
(the workflow uses `$RUNNER_TEMP/schink-deployment-job.json`). It contains job and
resource identifiers, not credentials. Use a fresh Azure login before recovery:

```sh
python3 scripts/deploy_webapp.py --recover /path/to/schink-deployment-job.json
```

Inspect the job's `state.json`, `journal.json`, and `worker.log` through
authenticated SCM when diagnosing `recovery_failed`. Do not blindly start a
partial application. Restore a verified backup and repeat health verification.

An end-to-end activation and rollback drill should use a separate non-production
app before claiming this follow-up's Azure activation has been proven.
Azure host/storage outages or loss of ARM
access can still prevent recovery; these safeguards cannot guarantee availability
during a platform outage.

## Verification

```sh
python3 -m unittest discover -s scripts/tests -p 'test_deploy_webapp_quiesced.py' -v
bash -n scripts/deploy-webapp-quiesced.sh
```

Tests use real temporary package/website directories, simulate startup and copy
failures, and kill a subprocess halfway through writing an assembly before
recovering from the journal. Persistent website rename failures cover activation
and rollback. A local HTTP server enforces Kudu's overwrite/delete conditions and
exercises failed-recovery relaunch and credential cleanup with the real client.
They make no production API calls.

Remediation verification on 23 October 2026: Release publish/static validation,
37 regression tests, Bash/Python syntax and workflow YAML validation passed. Azure
SCM probes confirmed Python 3.12, shared-volume locking, a detached process
surviving its launch request, and upload/checksum/static validation of all 895
files in a 246,560,655-byte published ZIP. These probes did not activate a release,
stop the live app, or stage ARM credentials for an activation worker.

Follow-up verification: 49 deployment regression tests pass, including recovery
from a partially written file, destination checksum corruption, persistent rename
failures and Kudu HTTP preconditions. The follow-up has not been run in production.

The SCM transport follows the documented [Kudu VFS and command APIs](https://github.com/projectkudu/kudu/wiki/REST-API)
and [Microsoft's Entra bearer authentication guidance](https://learn.microsoft.com/en-us/azure/app-service/deploy-zip).
