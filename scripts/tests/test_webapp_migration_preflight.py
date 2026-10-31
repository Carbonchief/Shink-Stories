"""Execute the workflow's migration shell using a local Supabase test double."""
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest


class MigrationPreflightTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.calls = self.root / 'calls.jsonl'
        tool = self.root / 'supabase'
        tool.write_text('''#!/usr/bin/env python3
import json, os, sys
with open(os.environ['SUPABASE_TEST_CALLS'], 'a') as output:
    output.write(json.dumps(sys.argv[1:]) + '\\n')
if '--output' in sys.argv:
    print('already_applied')
    print(os.environ.get('SUPABASE_TEST_APPLIED', 'false'))
''')
        tool.chmod(0o700)
        workflow = Path(__file__).resolve().parents[2] / '.github/workflows/master_schink.yml'
        step = workflow.read_text().split('      - name: Check and apply Supabase migrations\n', 1)[1].split('      - name:', 1)[0]
        block = step.split('        run: |\n', 1)[1]
        self.script = '\n'.join(line[10:] if line.startswith('          ') else line for line in block.splitlines())
        self.script = self.script.replace('${{ env.SUPABASE_WORKDIR }}', '${SUPABASE_WORKDIR}')
        self.migrations = [f'Shink/Database/migrations/2026103100000{i}_test.sql' for i in range(3)]
        for migration in self.migrations:
            path = self.root / migration
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text('select 1;')
        self.environment = {**os.environ, 'PATH': str(self.root) + os.pathsep + os.environ['PATH'],
                            'SUPABASE_TEST_CALLS': str(self.calls), 'SUPABASE_WORKDIR': str(self.root),
                            'GITHUB_WORKSPACE': str(self.root), 'GITHUB_SHA': 'fixture-sha',
                            'MIGRATION_FILES': '\n'.join(self.migrations)}

    def execute(self, preflight, applied='false'):
        environment = {**self.environment, 'SUPABASE_TEST_APPLIED': applied}
        if preflight is not None:
            environment['PREFLIGHT_ONLY'] = preflight
        else:
            environment.pop('PREFLIGHT_ONLY', None)
        result = subprocess.run(['bash', '-c', self.script], env=environment,
                                capture_output=True, text=True, timeout=10)
        calls = [json.loads(line) for line in self.calls.read_text().splitlines()] if self.calls.exists() else []
        return result, calls

    def test_preflight_only_reads_history_and_never_applies_sql_or_creates_history(self):
        result, calls = self.execute('true')
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual(3, len(calls))
        self.assertTrue(all('--output' in call and call[-1].startswith('select exists(') for call in calls))
        self.assertFalse(any('--file' in call for call in calls))
        self.assertEqual(3, result.stdout.count('Pending Supabase migration (preflight only)'))

    def test_scheduled_release_applies_pending_migrations_in_order_and_records_them(self):
        result, calls = self.execute('false')
        self.assertEqual(0, result.returncode, result.stderr)
        applied = [call[call.index('--file') + 1] for call in calls if '--file' in call]
        self.assertEqual([str(self.root / path) for path in self.migrations], applied)
        self.assertTrue(calls[0][-1].startswith('create schema if not exists'))
        self.assertEqual(3, sum(call[-1].startswith('insert into supabase_migrations.shink_action_migrations') for call in calls))

    def test_already_applied_migrations_remain_skipped_during_preflight(self):
        result, calls = self.execute('true', applied='true')
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual(3, result.stdout.count('Skipping previously applied migration'))
        self.assertFalse(any('--file' in call for call in calls))

    def test_missing_preflight_flag_fails_before_any_database_command(self):
        result, calls = self.execute(None)
        self.assertNotEqual(0, result.returncode)
        self.assertEqual([], calls)


if __name__ == '__main__':
    unittest.main()
