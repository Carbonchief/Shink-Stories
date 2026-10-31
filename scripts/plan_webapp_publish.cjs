// All branches publish to the same Azure app. A branch rename must not reset
// migration selection, and a successful preflight is not a successful deploy.
const ACTIVATION_STEP = 'Stage, validate, and activate with automatic rollback';
const RELEVANT_PREFIXES = [
  'Shink/', 'Directory.Build.props', 'Directory.Build.targets', 'global.json',
  '.github/workflows/master_schink.yml', 'scripts/plan_webapp_publish.cjs',
  'scripts/deploy-webapp-quiesced.sh', 'scripts/deploy_webapp.py',
  'scripts/webapp_deployment_worker.py',
];

async function createPublishPlan(github, context) {
  const repository = { owner: context.repo.owner, repo: context.repo.repo };
  let deployed;
  for await (const page of github.paginate.iterator(github.rest.actions.listWorkflowRuns, {
    ...repository, workflow_id: 'master_schink.yml', status: 'completed', per_page: 100,
  })) {
    // Octokit's pagination plugin normalizes workflow_runs into a data array.
    for (const run of page.data) {
      if (!['schedule', 'workflow_dispatch'].includes(run.event) || run.conclusion !== 'success') continue;
      const jobs = await github.paginate(github.rest.actions.listJobsForWorkflowRun, {
        ...repository, run_id: run.id, filter: 'latest', per_page: 100,
      });
      if (jobs.some(job => job.name === 'publish' && job.conclusion === 'success' &&
        job.steps?.some(step => step.name === ACTIVATION_STEP && step.conclusion === 'success'))) {
        deployed = run;
        break;
      }
    }
    if (deployed) break;
  }
  if (!deployed) throw new Error('No successful Azure activation baseline was found. Refusing to deploy without a migration baseline.');
  const plan = { shouldPublish: false, migrationFiles: [], baselineSha: deployed.head_sha, baselineRunId: deployed.id };
  if (deployed.head_sha === context.sha) return { ...plan, reason: 'same commit already published' };

  const { data: comparison } = await github.rest.repos.compareCommits({ ...repository, base: deployed.head_sha, head: context.sha });
  if (!['ahead', 'identical'].includes(comparison.status)) throw new Error('The deployment baseline is not an ancestor of this release. Refusing an automatic rollback or divergent publish.');
  if (!Array.isArray(comparison.files) || comparison.files.length >= 300) throw new Error('The changed-file list is incomplete or may be truncated. Refusing to omit deployment migrations.');
  const shouldPublish = comparison.files.some(file => RELEVANT_PREFIXES.some(prefix => file.filename === prefix || file.filename.startsWith(prefix)));
  const migrationFiles = comparison.files
    .filter(file => file.status !== 'removed' && file.filename.startsWith('Shink/Database/migrations/') && file.filename.endsWith('.sql'))
    .map(file => file.filename).sort();
  return { ...plan, shouldPublish, migrationFiles, reason: shouldPublish ? 'relevant files changed since last Azure activation' : 'no relevant files changed since last Azure activation' };
}

module.exports = { ACTIVATION_STEP, createPublishPlan };
