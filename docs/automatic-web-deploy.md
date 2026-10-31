# Automatic website deployment

GitHub's default branch is `main`. The website workflow runs each day at
00:00 UTC (02:00 South Africa time). GitHub may start scheduled runs later than
their configured time. A push to `main` runs build and deployment-safety checks.
The scheduled run publishes the latest `main` commit.

## Deployment baseline and migrations

All historical branches target the same Azure app. The planner in
`scripts/plan_webapp_publish.cjs` therefore searches deployment history across
branches. It accepts only a successful run whose Azure activation step actually
succeeded. Successful builds, skipped schedules and preflight-only runs cannot
advance the deployment baseline.

The planner compares the last activated commit to the candidate commit and
selects changed SQL migrations in filename order. No activation history,
divergent history or a potentially truncated file comparison stops publication
instead of silently skipping migrations. Existing database migration history
still prevents reapplying migrations.
The Supabase CLI is pinned to the verified stable version `2.107.0`.

At the 31 October 2026 readiness check, the production baseline was `8fbda62`.
The next release has these migration candidates:

1. `20261029135259_mobile_push_notifications.sql`
2. `20261031163913_admin_ops_alert_delivery.sql`
3. `20261031174228_paystack_card_update_jobs.sql`

Mobile push remains disabled by the application's configuration. The new card
update and alert workers use the existing enabled background-job default.

## Read-only preflight

Manual workflow dispatch defaults to `preflight_only=true`. To test the same
release path and GitHub credentials without deploying early:

```sh
gh workflow run master_schink.yml --ref main -f preflight_only=true
```

Preflight tests deployment planning and rollback, publishes and verifies the
application package, links Supabase and reads migration history, authenticates
to Azure and checks the app prerequisites, and checks current public health.
It does not apply migrations, create migration-history tables, stage an Azure
deployment or invoke deployment recovery. A preflight result cannot cause the
next scheduled release to be mistaken for an already-deployed commit.

An explicitly approved immediate deployment uses `preflight_only=false`.
Scheduled runs use the deployment path automatically. Azure authentication is
checked before applying SQL. Activation retains automatic rollback and a final
public health check.

The workflow planner, migration preflight shell and deployment transaction have
regression tests under `scripts/tests/`. The migration shell tests execute the
actual workflow script against a local Supabase command double and assert that
preflight performs only history reads.
