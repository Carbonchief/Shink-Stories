import base64
import fcntl
import hashlib
import json
import os
from pathlib import Path
import stat
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch
import urllib.error
import zipfile

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import deploy_webapp as coordinator
import webapp_deployment_worker as worker


def release(root, version):
    root.mkdir(parents=True, exist_ok=True)
    (root / 'wwwroot').mkdir(exist_ok=True)
    for name in worker.REQUIRED - {'Shink.staticwebassets.endpoints.json'}:
        (root / name).write_bytes(version.encode())
    css = (version + ' CSS').encode()
    (root / 'wwwroot/app.css').write_bytes(css)
    (root / 'Shink.staticwebassets.endpoints.json').write_text(json.dumps({
        'ManifestType': 'Publish', 'Endpoints': [{
            'AssetFile': 'app.css', 'ResponseHeaders': [
                {'Name': 'Content-Length', 'Value': str(len(css))},
                {'Name': 'ETag', 'Value': '"' + base64.b64encode(hashlib.sha256(css).digest()).decode() + '"'}]}]}))


def tree(root):
    return {str(path.relative_to(root)): path.read_bytes() for path in root.rglob('*') if path.is_file()}


class FakeApp:
    def __init__(self, root, state='Running', fail_health=False, fail_stop=False, fail_start=False):
        self.root, self.current_state = root, state
        self.fail_health, self.fail_stop, self.fail_start = fail_health, fail_stop, fail_start
        self.events = []

    def state(self):
        return self.current_state

    def ensure_credentials(self):
        pass

    def version(self):
        return (self.root / 'Shink.dll').read_text()

    def stop(self):
        self.events.append(('stop', self.version()))
        if self.fail_stop:
            self.fail_stop = False
            raise TimeoutError('Stop reply lost')
        self.current_state = 'Stopped'

    def start(self):
        worker.verify_static_assets(self.root)
        self.events.append(('start', self.version()))
        if self.fail_start:
            self.fail_start = False
            raise OSError('Start failed')
        self.current_state = 'Running'

    def check_health(self):
        self.events.append(('health', self.version()))
        if self.fail_health and self.version() == 'new':
            raise TimeoutError('New release unhealthy')


class DeploymentTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.base = Path(self.temporary.name).resolve()
        scratch_location = patch.object(worker.tempfile, 'tempdir', str(self.base))
        scratch_location.start()
        self.addCleanup(scratch_location.stop)
        self.root = self.base / 'wwwroot'
        release(self.root, 'old')
        (self.root / 'Stories').mkdir()
        (self.root / 'Stories/private.mp3').write_bytes(b'private audio')
        (self.root / 'wwwroot/uploads').mkdir()
        (self.root / 'wwwroot/uploads/customer.jpg').write_bytes(b'uploaded image')
        self.before = tree(self.root)
        source = self.base / 'publish'
        release(source, 'new')
        (source / 'wwwroot/new-file.txt').write_text('new only')
        self.job = self.base / 'jobs' / ('a' * 32)
        self.job.mkdir(parents=True)
        self.package = self.job / 'candidate.zip'
        with zipfile.ZipFile(self.package, 'w') as archive:
            for path in sorted(source.rglob('*')):
                if path.is_file():
                    archive.write(path, str(path.relative_to(source)))
        self.settings = {'files': worker.package_files(self.package),
                         'package_sha256': worker.digest(self.package), 'original_state': 'Running'}
        (self.job / 'settings.json').write_text(json.dumps(self.settings))
        (self.job / 'state.json').write_text(json.dumps({'phase': 'uploaded'}))
        self.app = FakeApp(self.root)

    def phase(self):
        return json.loads((self.job / 'state.json').read_text())['phase']

    def test_success_verifies_all_files_and_health_before_finishing(self):
        self.assertEqual(0, worker.transaction(self.job, self.root, self.app))
        self.assertEqual('succeeded', self.phase())
        self.assertEqual([('stop', 'old'), ('start', 'new'), ('health', 'new')], self.app.events)
        worker.verify_files(self.root, self.settings['files'])
        self.assertEqual('Running', self.app.state())

    def test_success_preserves_runtime_uploads_and_private_audio(self):
        worker.transaction(self.job, self.root, self.app)
        for name in ['Stories/private.mp3', 'wwwroot/uploads/customer.jpg']:
            self.assertEqual(self.before[name], (self.root / name).read_bytes())

    def test_upload_corruption_never_stops_running_app(self):
        with self.package.open('ab') as stream:
            stream.write(b'corrupt')
        self.assertEqual(1, worker.transaction(self.job, self.root, self.app))
        self.assertEqual('failed_preparation', self.phase())
        self.assertEqual([], self.app.events)
        self.assertEqual(self.before, tree(self.root))

    def test_static_asset_mismatch_fails_before_stopping(self):
        with zipfile.ZipFile(self.package, 'a') as archive:
            # Change package contents without changing its publish manifest.
            archive.writestr('wwwroot/bad.txt', 'irrelevant')
        self.settings['files'] = worker.package_files(self.package)
        self.settings['package_sha256'] = worker.digest(self.package)
        (self.job / 'settings.json').write_text(json.dumps(self.settings))
        real_verify = worker.verify_static_assets
        def tamper_candidate(root):
            if root != self.root:
                (root / 'wwwroot/app.css').write_bytes(b'wrong')
            real_verify(root)
        with patch.object(worker, 'verify_static_assets', side_effect=tamper_candidate):
            self.assertEqual(1, worker.transaction(self.job, self.root, self.app))
        self.assertEqual([], self.app.events)
        self.assertEqual(self.before, tree(self.root))

    def test_corrupt_snapshot_refuses_to_stop(self):
        real_copy = worker.atomic_copy
        def corrupt_backup(source, target):
            real_copy(source, target)
            if 'backup' in target.parts:
                target.write_bytes(b'bad backup')
        with patch.object(worker, 'atomic_copy', side_effect=corrupt_backup):
            self.assertEqual(1, worker.transaction(self.job, self.root, self.app))
        self.assertEqual([], self.app.events)
        self.assertEqual(self.before, tree(self.root))

    def test_partial_copy_is_rolled_back_before_restart(self):
        real_copy = worker.atomic_copy
        failed = False
        def fail_after_first_replacement(source, target):
            nonlocal failed
            real_copy(source, target)
            if target == self.root / 'Shink.dll' and not failed:
                failed = True
                raise OSError('Copy failed after replacing assembly')
        with patch.object(worker, 'atomic_copy', side_effect=fail_after_first_replacement):
            self.assertEqual(1, worker.transaction(self.job, self.root, self.app))
        self.assertEqual('rolled_back', self.phase())
        self.assertEqual(self.before, tree(self.root))
        self.assertEqual([('start', 'old')], [event for event in self.app.events if event[0] == 'start'])
        self.assertEqual('Running', self.app.state())

    def test_unhealthy_release_restores_original_and_removes_new_files(self):
        self.app.fail_health = True
        self.assertEqual(1, worker.transaction(self.job, self.root, self.app))
        self.assertEqual('rolled_back', self.phase())
        self.assertEqual(self.before, tree(self.root))
        self.assertEqual([('start', 'new'), ('start', 'old')],
                         [event for event in self.app.events if event[0] == 'start'])
        self.assertEqual(('health', 'old'), self.app.events[-1])

    def test_failed_start_restores_previous_release(self):
        self.app.fail_start = True
        self.assertEqual(1, worker.transaction(self.job, self.root, self.app))
        self.assertEqual('rolled_back', self.phase())
        self.assertEqual(self.before, tree(self.root))
        self.assertEqual('Running', self.app.state())

    def test_failed_stop_restores_unchanged_running_release(self):
        self.app.fail_stop = True
        self.assertEqual(1, worker.transaction(self.job, self.root, self.app))
        self.assertEqual('rolled_back', self.phase())
        self.assertEqual(self.before, tree(self.root))
        self.assertEqual('Running', self.app.state())

    def test_previously_stopped_app_is_not_started(self):
        self.settings['original_state'] = 'Stopped'
        (self.job / 'settings.json').write_text(json.dumps(self.settings))
        self.app.current_state = 'Stopped'
        self.assertEqual(0, worker.transaction(self.job, self.root, self.app))
        self.assertEqual([('stop', 'old')], self.app.events)
        self.assertEqual('Stopped', self.app.state())

    def test_concurrent_admin_stop_during_upload_is_respected(self):
        self.app.current_state = 'Stopped'
        self.assertEqual(1, worker.transaction(self.job, self.root, self.app))
        self.assertEqual('failed_preparation', self.phase())
        self.assertEqual([], self.app.events)
        self.assertEqual(self.before, tree(self.root))

    def test_global_lock_prevents_overlapping_activations(self):
        with (self.job.parent / '.activation.lock').open('a') as lock:
            fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
            self.assertEqual(2, worker.transaction(self.job, self.root, self.app))
        self.assertEqual('uploaded', self.phase())
        self.assertEqual([], self.app.events)

    def test_process_crash_mid_copy_is_recovered_from_durable_journal(self):
        code = '''
import os, sys
from pathlib import Path
sys.path.insert(0, sys.argv[1])
import webapp_deployment_worker as w
root = Path(sys.argv[3])
class App:
    def state(self): return 'Running'
    def stop(self): pass
    def ensure_credentials(self): pass
real_copy = w.atomic_copy
def crash(source, target):
    real_copy(source, target)
    if target == root / 'Shink.dll': os._exit(91)
w.atomic_copy = crash
w.transaction(Path(sys.argv[2]), root, App())
'''
        result = subprocess.run([sys.executable, '-c', code, str(Path(worker.__file__).parent),
                                 str(self.job), str(self.root)], timeout=10,
                                env={**os.environ, 'TMPDIR': str(self.base)})
        self.assertEqual(91, result.returncode)
        self.assertEqual('activating', self.phase())
        self.assertEqual('new', self.app.version())
        self.app.current_state = 'Stopped'
        self.assertEqual(1, worker.transaction(self.job, self.root, self.app, recover=True))
        self.assertEqual('rolled_back', self.phase())
        self.assertEqual(self.before, tree(self.root))
        self.assertEqual('Running', self.app.state())

    def test_recovery_without_journal_does_not_touch_site(self):
        self.assertEqual(1, worker.transaction(self.job, self.root, self.app, recover=True))
        self.assertEqual('failed_preparation', self.phase())
        self.assertEqual([], self.app.events)

    def test_preparation_crash_after_snapshot_does_not_stop_healthy_site(self):
        (self.job / 'journal.json').write_text(json.dumps({'previous': {}, 'original_state': 'Running'}))
        (self.job / 'state.json').write_text(json.dumps({'phase': 'validating'}))
        self.assertEqual(1, worker.transaction(self.job, self.root, self.app, recover=True))
        self.assertEqual('failed_preparation', self.phase())
        self.assertEqual([], self.app.events)
        self.assertEqual(self.before, tree(self.root))

    def test_bad_backup_is_never_started(self):
        def fail_and_corrupt_backup():
            (self.job / 'backup/Shink.dll').write_bytes(b'bad backup')
            raise TimeoutError('Unhealthy release')
        self.app.check_health = fail_and_corrupt_backup
        self.assertEqual(1, worker.transaction(self.job, self.root, self.app))
        self.assertEqual('recovery_failed', self.phase())
        self.assertNotIn(('start', 'old'), self.app.events)

    def test_502_after_worker_launch_does_not_leave_app_stopped(self):
        fixture = self
        class LostReply:
            launches = 0
            def put(self, *args): pass
            def request(self, *args): pass
            def launch(self, *args):
                self.launches += 1
                worker.transaction(fixture.job, fixture.root, fixture.app)
                raise urllib.error.HTTPError('https://scm/api/command', 502, 'Bad Gateway', {}, None)
            def state(self, job):
                return json.loads((fixture.job / 'state.json').read_text())
        kudu = LostReply()
        with patch.object(coordinator, 'az', return_value={'accessToken': 'fixture-token', 'expires_on': 9999999999}):
            result = coordinator.launch_and_wait(kudu, 'a' * 32)
        self.assertEqual('succeeded', result['phase'])
        self.assertEqual(1, kudu.launches)
        self.assertEqual('Running', self.app.state())

    def test_transient_polling_502_is_reconciled_without_redeployment(self):
        class Replies:
            replies = [urllib.error.HTTPError('https://scm', 502, 'Bad Gateway', {}, None),
                       {'phase': 'activating'}, {'phase': 'succeeded'}]
            def state(self, job):
                reply = self.replies.pop(0)
                if isinstance(reply, Exception): raise reply
                return reply
        result = coordinator.wait_for_job(Replies(), 'a' * 32, timeout=1, interval=0)
        self.assertEqual('succeeded', result['phase'])

    def test_interrupted_authorization_upload_is_cleaned_without_launch(self):
        class InterruptedAuthorization:
            deleted = []
            def put(self, *args): raise TimeoutError('Authorization upload reply lost')
            def request(self, method, path): self.deleted.append((method, path))
            def launch(self, *args): raise AssertionError('Must not launch')
        kudu = InterruptedAuthorization()
        with patch.object(coordinator, 'az', return_value={'accessToken': 'fixture', 'expires_on': 9999999999}):
            with self.assertRaises(TimeoutError):coordinator.launch_and_wait(kudu, 'a' * 32)
        self.assertEqual(1, len(kudu.deleted))
        self.assertEqual('DELETE', kudu.deleted[0][0])
        self.assertEqual([], self.app.events)

    def test_upload_timeout_cannot_stop_or_launch_app(self):
        class UploadFails:
            calls = []
            def request(self, method, path):
                self.calls.append((method, path))
                return []
            def put(self, job, name, data):
                self.calls.append(('PUT', name))
                if name == 'candidate.zip': raise TimeoutError('Upload timeout')
        kudu = UploadFails()
        site = {'name': 'test', 'resourceGroup': 'test', 'state': 'Running', 'scmSiteAlsoStopped': False}
        with self.assertRaises(TimeoutError):
            coordinator.deploy(kudu, site, self.package, self.base / 'locator.json')
        self.assertEqual([], self.app.events)
        self.assertEqual(self.before, tree(self.root))
        self.assertFalse(any(method == 'POST' for method, _ in kudu.calls))

    def test_unknown_scm_or_app_state_is_rejected_before_upload(self):
        for scm, state in [(True, 'Running'), (None, 'Running'), (False, 'Starting')]:
            with self.subTest(scm=scm, state=state):
                site = {'state': state, 'scmSiteAlsoStopped': scm}
                with self.assertRaises(ValueError):
                    coordinator.deploy(None, site, self.package, self.base / 'locator.json')

    def test_traversal_and_symlink_packages_are_rejected(self):
        for name in ['../escape', '/absolute', 'a/../../escape', 'a\\escape']:
            with self.subTest(name=name):
                with self.assertRaises(ValueError):worker.safe_path(self.root, name)
        with zipfile.ZipFile(self.package, 'a') as archive:
            link = zipfile.ZipInfo('evil-link')
            link.create_system = 3
            link.external_attr = (stat.S_IFLNK | 0o777) << 16
            archive.writestr(link, '/etc/passwd')
        with self.assertRaises(ValueError):worker.package_files(self.package)

    def test_symlink_in_target_is_rejected_before_stop(self):
        outside = self.base / 'outside'
        outside.mkdir()
        (self.root / 'wwwroot').rename(self.root / 'old-wwwroot')
        (self.root / 'wwwroot').symlink_to(outside, target_is_directory=True)
        self.assertEqual(1, worker.transaction(self.job, self.root, self.app))
        self.assertEqual([], self.app.events)
        self.assertEqual({}, tree(outside))

    def test_launch_detaches_with_closed_stdin_and_no_credentials_in_command(self):
        kudu = coordinator.Kudu('https://example.scm', 'fixture-secret')
        with patch.object(kudu, 'request', return_value={'ExitCode': 0}) as request:
            kudu.launch('a' * 32, 'authorization-' + 'b' * 32)
        command = request.call_args.args[2]['command']
        self.assertIn('nohup python3', command)
        self.assertIn('2>&1 < /dev/null &', command)
        self.assertNotIn('fixture-secret', command)

    def test_expiring_authorization_cannot_stop_the_app(self):
        with patch.object(self.app, 'ensure_credentials', side_effect=ValueError('Authorization expires soon')):
            self.assertEqual(1, worker.transaction(self.job, self.root, self.app))
        self.assertEqual('failed_preparation', self.phase())
        self.assertEqual([], self.app.events)
        self.assertEqual(self.before, tree(self.root))

    def test_real_azure_adapter_rejects_insufficient_token_lifetime(self):
        app = worker.AzureApp('/subscriptions/test/resourceGroups/test/providers/Microsoft.Web/sites/test',
                              'https://example.azurewebsites.net/healthz', 'fixture', 0)
        with self.assertRaisesRegex(ValueError, '30 minutes'):
            app.ensure_credentials()

    def test_partial_copy_preserves_previously_stopped_state_on_rollback(self):
        self.settings['original_state'] = 'Stopped'
        (self.job / 'settings.json').write_text(json.dumps(self.settings))
        self.app.current_state = 'Stopped'
        real_copy = worker.atomic_copy
        failed = False
        def fail_once(source, target):
            nonlocal failed
            real_copy(source, target)
            if target == self.root / 'Shink.dll' and not failed:
                failed = True
                raise OSError('Copy failed')
        with patch.object(worker, 'atomic_copy', side_effect=fail_once):
            self.assertEqual(1, worker.transaction(self.job, self.root, self.app))
        self.assertEqual('rolled_back', self.phase())
        self.assertEqual('Stopped', self.app.state())
        self.assertEqual(self.before, tree(self.root))
        self.assertFalse(any(event[0] == 'start' for event in self.app.events))

    def test_failed_recovery_can_be_retried_after_backup_is_repaired(self):
        def corrupt_then_fail():
            (self.job / 'backup/Shink.dll').write_bytes(b'corrupt')
            raise TimeoutError('Startup failed')
        self.app.check_health = corrupt_then_fail
        worker.transaction(self.job, self.root, self.app)
        self.assertEqual('recovery_failed', self.phase())
        (self.job / 'backup/Shink.dll').write_bytes(self.before['Shink.dll'])
        self.app.check_health = lambda: None
        self.assertEqual(1, worker.transaction(self.job, self.root, self.app, recover=True))
        self.assertEqual('rolled_back', self.phase())
        self.assertEqual(self.before, tree(self.root))

    def test_existing_kudu_deployment_blocks_activation_before_upload(self):
        class ExistingDeployment:
            def request(self, *args): return [{'status': 1, 'complete': False}]
            def put(self, *args): raise AssertionError('Must not upload')
        site = {'state': 'Running', 'scmSiteAlsoStopped': False}
        with self.assertRaisesRegex(ValueError, 'existing Kudu deployment'):
            coordinator.deploy(ExistingDeployment(), site, self.package, self.base / 'locator.json')
        self.assertEqual(self.before, tree(self.root))

    def test_polling_timeout_never_launches_or_starts_another_copy(self):
        class Pending:
            def state(self, job): return {'phase': 'activating'}
        with self.assertRaises(TimeoutError):
            coordinator.wait_for_job(Pending(), 'a' * 32, timeout=0.001, interval=0)
        self.assertEqual([], self.app.events)

    def test_cleanup_preserves_active_jobs_and_unrelated_directories(self):
        old = self.job.parent / ('b' * 32)
        active = self.job.parent / ('c' * 32)
        unrelated = self.job.parent / 'customer-content'
        for directory, phase in [(old, 'succeeded'), (active, 'activating'), (unrelated, 'succeeded')]:
            directory.mkdir()
            (directory / 'state.json').write_text(json.dumps({'phase': phase}))
        (self.job / 'state.json').write_text(json.dumps({'phase': 'succeeded'}))
        worker.prune_completed_jobs(self.job.parent, self.job, keep=0)
        self.assertFalse(old.exists())
        self.assertTrue(active.exists())
        self.assertTrue(unrelated.exists())
        self.assertTrue(self.job.exists())

    def test_always_recovery_step_is_safe_before_a_job_was_staged(self):
        script = Path(worker.__file__).with_name('deploy-webapp-quiesced.sh')
        result = subprocess.run(['bash', str(script), '--recover', str(self.base / 'missing.json')],
                                capture_output=True, text=True, timeout=10)
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn('No staged deployment needs recovery', result.stdout)

    def test_workflow_keeps_recovery_outside_the_activation_step(self):
        workflow = Path(worker.__file__).parents[1] / '.github/workflows/master_schink.yml'
        source = workflow.read_text()
        self.assertIn("if: always() && steps.publish_check.outputs.should_publish == 'true'", source)
        self.assertIn('python3 scripts/deploy_webapp.py --recover "$RUNNER_TEMP/schink-deployment-job.json"', source)
        self.assertIn("'scripts/webapp_deployment_worker.py'", source)
        self.assertIn("'scripts/deploy_webapp.py'", source)
        self.assertIn('cancel-in-progress: false', source)

    def test_unchanged_dependencies_are_not_rewritten_or_backed_up(self):
        # Keep one published dependency identical in the old and new packages.
        (self.root / 'Shink.deps.json').write_bytes(b'new')
        before = (self.root / 'Shink.deps.json').stat().st_ino
        self.assertEqual(0, worker.transaction(self.job, self.root, self.app))
        journal = json.loads((self.job / 'journal.json').read_text())
        self.assertNotIn('Shink.deps.json', journal['previous'])
        self.assertFalse((self.job / 'backup/Shink.deps.json').exists())
        self.assertEqual(before, (self.root / 'Shink.deps.json').stat().st_ino)

    def test_identical_release_checks_health_without_stopping_app(self):
        with zipfile.ZipFile(self.package) as archive:
            archive.extractall(self.root)
        with patch.object(self.app, 'ensure_credentials', side_effect=AssertionError('No stop needs authorization')):
            self.assertEqual(0, worker.transaction(self.job, self.root, self.app))
        self.assertEqual('succeeded', self.phase())
        self.assertEqual([('health', 'new')], self.app.events)

    def test_insufficient_activation_space_does_not_stop_site(self):
        with patch.object(worker.shutil, 'disk_usage', return_value=type('Space', (), {'free': 0})()):
            self.assertEqual(1, worker.transaction(self.job, self.root, self.app))
        self.assertEqual('failed_preparation', self.phase())
        self.assertEqual([], self.app.events)
        self.assertEqual(self.before, tree(self.root))

    def test_status_write_failure_does_not_prevent_rollback(self):
        real_write = worker.write_json
        def fail_recovery_status(path, value):
            if value.get('phase') in {'rolling_back', 'rolled_back', 'recovery_failed'}:
                raise OSError('Status storage failed')
            real_write(path, value)
        self.app.fail_health = True
        with patch.object(worker, 'write_json', side_effect=fail_recovery_status):
            self.assertEqual(1, worker.transaction(self.job, self.root, self.app))
        self.assertEqual(self.before, tree(self.root))
        self.assertEqual('Running', self.app.state())
        self.assertEqual(('health', 'old'), self.app.events[-1])


if __name__ == '__main__':
    unittest.main()
