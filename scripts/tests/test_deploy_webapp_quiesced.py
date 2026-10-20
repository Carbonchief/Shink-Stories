import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest


DEPLOY_SCRIPT = Path(__file__).resolve().parents[1] / "deploy-webapp-quiesced.sh"


class QuiescedDeploymentTests(unittest.TestCase):
    def run_deployment(self, initial_state="Running", fail_operation="", deployment_status="4",
                       scm_site_also_stopped="false", fail_scm_read=False):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            (root / "state").write_text(initial_state)
            package = root / "application.zip"
            package.write_bytes(b"fixture")
            fake_az = root / "az"
            fake_az.write_text("""#!/usr/bin/env python3
import json, os, pathlib, sys
root = pathlib.Path(os.environ['SHINK_DEPLOY_TEST_ROOT'])
operation = sys.argv[2]
with (root / 'calls').open('a') as log:
    log.write(json.dumps(sys.argv[1:]) + '\\n')
if operation == os.environ.get('SHINK_DEPLOY_FAIL_OPERATION'):
    sys.exit(9)
if operation == 'show':
    if sys.argv[sys.argv.index('--query') + 1] == 'scmSiteAlsoStopped':
        if os.environ['SHINK_FAIL_SCM_READ'] == 'true':
            sys.exit(8)
        print(os.environ['SHINK_SCM_SITE_ALSO_STOPPED'])
    else:
        print((root / 'state').read_text())
elif operation == 'stop':
    (root / 'state').write_text('Stopped')
elif operation == 'start':
    (root / 'state').write_text('Running')
elif operation == 'deploy':
    if (root / 'state').read_text() != 'Stopped':
        sys.exit(10)
    print(os.environ['SHINK_DEPLOY_STATUS'])
""")
            fake_az.chmod(0o755)
            result = subprocess.run(
                ["bash", str(DEPLOY_SCRIPT), "test-group", "test-app", str(package)],
                env={**os.environ, "PATH": str(root) + os.pathsep + os.environ["PATH"],
                     "SHINK_DEPLOY_TEST_ROOT": str(root), "SHINK_DEPLOY_FAIL_OPERATION": fail_operation,
                     "SHINK_DEPLOY_STATUS": deployment_status,
                     "SHINK_SCM_SITE_ALSO_STOPPED": scm_site_also_stopped,
                     "SHINK_FAIL_SCM_READ": str(fail_scm_read).lower()},
                capture_output=True, text=True, timeout=15,
            )
            calls = [json.loads(line) for line in (root / "calls").read_text().splitlines()]
            return result, calls, (root / "state").read_text()

    def test_running_app_is_stopped_before_deployment_and_restarted(self):
        result, calls, state = self.run_deployment()
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual(["show", "show", "stop", "show", "deploy", "start"], [call[1] for call in calls])
        deployment = next(call for call in calls if call[1] == "deploy")
        self.assertEqual("false", deployment[deployment.index("--restart") + 1])
        self.assertEqual("false", deployment[deployment.index("--async") + 1])
        self.assertEqual("false", deployment[deployment.index("--track-status") + 1])
        self.assertEqual("false", deployment[deployment.index("--clean") + 1])
        self.assertEqual("Running", state)

    def test_previously_stopped_app_stays_stopped(self):
        result, calls, state = self.run_deployment(initial_state="Stopped")
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual(["show", "show", "show", "deploy"], [call[1] for call in calls])
        self.assertEqual("Stopped", state)

    def test_failed_deployment_does_not_start_partial_application(self):
        result, calls, state = self.run_deployment(fail_operation="deploy")
        self.assertEqual(9, result.returncode)
        self.assertNotIn("start", [call[1] for call in calls])
        self.assertEqual("Stopped", state)
        self.assertIn("restore a known-good package", result.stderr)

    def test_failed_deployment_also_preserves_previously_stopped_state(self):
        result, calls, state = self.run_deployment(initial_state="Stopped", fail_operation="deploy")
        self.assertEqual(9, result.returncode)
        self.assertNotIn("start", [call[1] for call in calls])
        self.assertEqual("Stopped", state)

    def test_accepted_but_unconfirmed_deployment_does_not_start_app(self):
        result, calls, state = self.run_deployment(deployment_status="2")
        self.assertEqual(1, result.returncode)
        self.assertNotIn("start", [call[1] for call in calls])
        self.assertEqual("Stopped", state)

    def test_failed_stop_does_not_deploy_and_restores_unchanged_app(self):
        result, calls, state = self.run_deployment(fail_operation="stop")
        self.assertEqual(9, result.returncode)
        self.assertEqual(["show", "show", "stop", "start"], [call[1] for call in calls])
        self.assertEqual("Running", state)

    def test_failed_restart_is_reported(self):
        result, calls, state = self.run_deployment(fail_operation="start")
        self.assertEqual(1, result.returncode)
        self.assertEqual("Stopped", state)
        self.assertIn("Unable to restore", result.stderr)

    def test_unknown_initial_state_never_deploys_or_starts(self):
        result, calls, state = self.run_deployment(initial_state="Starting")
        self.assertEqual(1, result.returncode)
        self.assertEqual(["show", "show"], [call[1] for call in calls])
        self.assertEqual("Starting", state)

    def test_scm_stop_true_or_unknown_refuses_all_mutations(self):
        for flag in ["true", "", "null"]:
            with self.subTest(scm_site_also_stopped=flag):
                result, calls, state = self.run_deployment(scm_site_also_stopped=flag)
                self.assertEqual(1, result.returncode)
                self.assertEqual(["show"], [call[1] for call in calls])
                self.assertEqual("Running", state)
                self.assertIn("scmSiteAlsoStopped=false", result.stderr)

    def test_scm_configuration_read_failure_refuses_all_mutations(self):
        result, calls, state = self.run_deployment(fail_scm_read=True)
        self.assertEqual(8, result.returncode)
        self.assertEqual(["show"], [call[1] for call in calls])
        self.assertEqual("Running", state)


if __name__ == "__main__":
    unittest.main()
