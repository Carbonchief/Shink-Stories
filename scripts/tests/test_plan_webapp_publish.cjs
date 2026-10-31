const assert = require('node:assert/strict');
const { test } = require('node:test');
const { ACTIVATION_STEP, createPublishPlan } = require('../plan_webapp_publish.cjs');

const context = { repo: { owner: 'Carbonchief', repo: 'Shink-Stories' }, sha: 'new-main' };
const run = (id, sha, event = 'schedule', branch = 'master') => ({ id, head_sha: sha, event, head_branch: branch, conclusion: 'success' });
const job = conclusion => ({ name: 'publish', conclusion: 'success', steps: [{ name: ACTIVATION_STEP, conclusion }] });

function fixture({ pages = [[run(1, 'deployed-master')]], jobs = { 1: [job('success')] }, files = [], status = 'ahead' } = {}) {
  const calls = [];
  const listWorkflowRuns = () => {};
  const listJobsForWorkflowRun = () => {};
  const paginate = async (method, args) => {
    assert.equal(method, listJobsForWorkflowRun);
    calls.push({ kind: 'jobs', ...args });
    return jobs[args.run_id] ?? [];
  };
  paginate.iterator = async function* (method, args) {
    assert.equal(method, listWorkflowRuns);
    assert.equal(args.branch, undefined, 'The same Azure app uses deployment history across branch renames.');
    for (const workflow_runs of pages) yield { data: workflow_runs };
  };
  return { calls, github: { paginate, rest: { actions: { listWorkflowRuns, listJobsForWorkflowRun }, repos: {
    compareCommits: async args => {
      calls.push({ kind: 'compare', ...args });
      return { data: { status, files } };
    },
  } } } };
}

test('new main uses the actual master deployment and includes all three new migrations', async () => {
  const migrations = [
    'Shink/Database/migrations/20261031174228_paystack_card_update_jobs.sql',
    'Shink/Database/migrations/20261029135259_mobile_push_notifications.sql',
    'Shink/Database/migrations/20261031163913_admin_ops_alert_delivery.sql',
  ];
  const { github, calls } = fixture({ files: migrations.map(filename => ({ filename, status: 'added' })) });
  const result = await createPublishPlan(github, context);
  assert.equal(result.shouldPublish, true);
  assert.equal(result.baselineSha, 'deployed-master');
  assert.deepEqual(result.migrationFiles, [...migrations].sort());
  assert.equal(calls.find(call => call.kind === 'compare').base, 'deployed-master');
});

test('successful preflight, no-op schedule and push cannot hide an unapplied release', async () => {
  const { github } = fixture({
    pages: [[run(4, context.sha, 'workflow_dispatch', 'main'), run(3, context.sha), run(2, context.sha, 'push'), run(1, 'deployed-master')]],
    jobs: { 4: [job('skipped')], 3: [job('skipped')], 1: [job('success')] },
    files: [{ filename: 'Shink/Program.cs', status: 'modified' }],
  });
  const result = await createPublishPlan(github, context);
  assert.equal(result.baselineRunId, 1);
  assert.equal(result.shouldPublish, true);
});

test('unchanged actually-deployed commit skips release without comparing files', async () => {
  const { github, calls } = fixture({ pages: [[run(1, context.sha)]] });
  const result = await createPublishPlan(github, context);
  assert.equal(result.shouldPublish, false);
  assert.deepEqual(result.migrationFiles, []);
  assert.equal(calls.filter(call => call.kind === 'compare').length, 0);
});

test('no actual activation history fails closed rather than publishing without migrations', async () => {
  const { github } = fixture({ pages: [[run(1, context.sha)]], jobs: { 1: [job('skipped')] } });
  await assert.rejects(createPublishPlan(github, context), /without a migration baseline/);
});

test('deployment baseline can be found on a later history page', async () => {
  const { github } = fixture({ pages: [[run(2, 'noop')], [run(1, 'deployed-master')]], jobs: { 2: [job('skipped')], 1: [job('success')] } });
  assert.equal((await createPublishPlan(github, context)).baselineRunId, 1);
});

test('documentation-only changes skip publication', async () => {
  const { github } = fixture({ files: [{ filename: 'docs/readiness.md', status: 'added' }] });
  assert.equal((await createPublishPlan(github, context)).shouldPublish, false);
});

for (const filename of ['scripts/deploy-webapp-quiesced.sh', 'scripts/deploy_webapp.py',
  'scripts/webapp_deployment_worker.py', 'scripts/plan_webapp_publish.cjs']) {
  test(`deployment implementation change triggers publication: ${filename}`, async () => {
    const { github } = fixture({ files: [{ filename, status: 'modified' }] });
    assert.equal((await createPublishPlan(github, context)).shouldPublish, true);
  });
}

test('deleted migration files are excluded from the apply list', async () => {
  const { github } = fixture({ files: [{ filename: 'Shink/Database/migrations/old.sql', status: 'removed' }] });
  assert.deepEqual((await createPublishPlan(github, context)).migrationFiles, []);
});

test('possibly truncated file comparison fails rather than silently omitting migrations', async () => {
  const { github } = fixture({ files: Array.from({ length: 300 }, (_, i) => ({ filename: `Shink/${i}.cs` })) });
  await assert.rejects(createPublishPlan(github, context), /truncated/);
});

for (const status of ['behind', 'diverged']) test(`${status} release is not an automatic rollback`, async () => {
  const { github } = fixture({ status });
  await assert.rejects(createPublishPlan(github, context), /rollback or divergent/);
});
