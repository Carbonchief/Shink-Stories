# Paystack card verification and operational alerts

## Card updates

The hosted management link remains the customer entry point, as described in
[Paystack's subscription guide](https://paystack.com/docs/payments/subscriptions/#updating-the-card-on-a-subscription).
Paystack sends a small `charge.success` transaction with
`metadata.transaction_type=update_card`. Its subscription code lives in
`metadata.custom_fields`, and its plan can be empty. This is a card verification.

Following [Paystack's webhook guidance](https://paystack.com/docs/payments/webhooks/),
the HTTP handler validates the signature, records one durable
`paystack_card_update_jobs` row, then returns HTTP 200. It makes no provider
requests, recovery charges, marketing changes or email sends before acknowledging
these events. A failed queue write returns HTTP 500 so Paystack can retry. The
write has a five-second timeout and duplicates retain the original job. Small
processed refunds that may belong to card verification follow the same queue;
the worker still checks for plan-change refunds before card recovery.

The worker runs every 30 seconds when `BackgroundJobs:Enabled` is true. Each job
has a five-minute exclusive database lease and a 90-second processing timeout.
Expired leases and abandoned ledger claims can be recovered after a restart.

The worker verifies the transaction, reusable authorization, amount, currency,
subscription code and local account owner. It then fetches the actual Paystack
subscription and requires its authorization to match the verified card before
saving the local card or attempting recovery. Tokenizing a card on the customer
account alone does not establish that the subscription card changed. This check
also applies to card events already marked processed by the earlier handler.

An unmatched provider card stays pending with result code `provider_card_update`.
Backoff is one, two, four, eight and then fifteen minutes. After fifteen minutes,
the worker raises one durable operations alert per verification reference and
reason. A verification and its later refund share that incident key. Checks stop
after 24 hours with `needs_attention`; they never silently change the provider's
subscription, create a replacement schedule, refund money or grant access.
An existing incident keeps its original reason and alert key at this cutoff.
The job cannot terminate until its operations alert is durably queued; an alert
storage failure retains the job for another attempt after its lease expires.
These retry intervals are Schink policy, not Paystack completion guarantees.

## Separate payment recovery

After provider card binding is confirmed, the worker may recover an existing,
terminally failed renewal using its stored billing amount. A pending scheduled
retry retains ownership of its attempt. Any previous reference is verified first:
success is reconciled without another collection; queued transactions stay in
`payment_confirmation`; old card-verification events cannot collect a later
month's debt. Suspended accounts require reconciliation rather than an automatic
new charge.
Healthy subscriptions can save a verified card even when a legacy billing amount
is missing. An unpaid recovery still requires its stored amount before collection.

The recovery uses a deterministic reference for the subscription and verification
transaction. The verification and refund therefore cannot independently collect
the same renewal. Before the provider request, an atomic compare-and-swap reserves
the recovery and persists its payment reference. Different card updates compete
for this same recovery, rather than just separate webhook jobs. A reserved attempt
has no scheduled retry due date; its card job owns verification and resumption.
Even after an ambiguous request or restart, retries use that same reference.
An unconfirmed payment stays pending. A different card may attempt collection
only after verifying that the previous payment failed. Stale result writes are
filtered by the exact reserved reference. Provider API acceptance alone does not grant access: the exact
payment reference, full amount, currency, customer and successful transaction
status are verified first. Failed persistence leaves the job retryable. Only a
confirmed full payment restores access, using Paystack's next billing date when
available. The R1 verification never creates or extends a paid subscription.
Succeeded-but-unresolved recoveries remain eligible for reconciliation after a
restart. Expiry rechecks the current recovery and removes access atomically;
pending payments, successful attempts and resolved recoveries cannot be suspended
from an old expiry snapshot.

The job result `card_update_applied` describes card binding, not payment success;
renewal status remains in the payment event and recovery record. Completing a
card job never marks an abandoned checkout paid or changes free-email marketing
state. Successful full recovery continues through the existing recovery flow.

This code can detect an incomplete provider card update; it cannot repair
Paystack's internal card attachment. That still requires completing the hosted
update or provider assistance. A replacement subscription requires separate
approval and verification that exactly one schedule can bill.

## Operational alert delivery

Apply these two new migrations before deploying the matching application:

- `Shink/Database/migrations/20261031163913_admin_ops_alert_delivery.sql`
- `Shink/Database/migrations/20261031174228_paystack_card_update_jobs.sql`

They create service-only tables and `SECURITY INVOKER` claim functions. Anonymous
and signed-in clients cannot read card-update envelopes or operational email
payloads, enqueue jobs, or obtain leases.

Each alert key has one durable incident record. An atomic five-minute lease
prevents competing app instances from sending simultaneously. The first published
Resend template request is preserved, including its original timestamp, so a
retry uses the same provider key **and** payload. Accepted delivery is recorded
with the provider email ID and remains suppressed permanently.

An independent dispatcher checks stored alerts every thirty seconds when
`BackgroundJobs:Enabled` is true. It can reclaim an expired lease after an ambiguous
delivery or failed completion write, without another incident occurrence.
Retries remain within 23 hours of the first send attempt. Queueing alone does not
start this clock, so unsent backlog can still receive its first delivery. This stays
inside Resend's 24-hour idempotency window. Older uncertain deliveries require
manual review rather than risking a duplicate email.
Storage or migration failures log a warning and suppress sending
without duplicate protection.

Do not run all outstanding migrations as part of this rollout. Apply only the
two migrations listed above after explicit production approval, then deploy the
tested billing changes. No migration, deployment, or Git remote update was
performed while preparing this fix.

## Validation

On 31 October 2026, all 1,210 tests in `Shink.Tests` and all 13 tests in
`Shink.Mobile.Audio.Tests` passed (1,223 total). The web application's
Release build succeeded. The build retains existing ImageSharp dependency
advisories. Both migrations and their SQL assertions passed on isolated
PostgreSQL 17; eight concurrent duplicate inserts produced one card job, and
eight competing workers obtained one lease. Eight distinct card-update attempts
produced one recovery reservation, and eight alert dispatchers obtained one lease.

The .NET regression suite covers:

- The actual webhook route returns 200 only after durable queueing, 401 for an
  invalid signature, and 500 when queue storage fails, for verification and refund
  events alike. Unavailable provider APIs do not delay acknowledgement.
- Duplicate events, app restarts, provider outages and abandoned ledger claims.
- An unattached provider card stays pending and cannot trigger a charge, alter
  the local card or extend access; later confirmed binding can finish recovery.
- Legacy processed events still require the actual provider binding check.
- Failed, queued, already-paid, wrong-amount, wrong-currency and wrong-owner
  recovery payments, plus a persistence failure after collection.
- Verification/refund incident keys and durable email suppression.
- Two different verification jobs racing on the same outstanding renewal.
- A crash between recovery reservation and provider acceptance, and a resolution
  failure after the succeeded write, followed by expiry and worker restarts.
- Healthy legacy subscriptions with null or zero billing amounts, while missing
  amounts on unpaid recoveries still prevent collection.
- Terminal alert storage failure, independent retry after delivery failure or a
  failed completion write, and incident-key preservation at the 24-hour cutoff.

The SQL regressions in `Shink.Tests/Database/` run against isolated PostgreSQL 17.
They check immutable alert payloads, bounded uncertain-delivery retries, permanent
suppression, job deduplication, exclusive and expired leases, stale-worker
completion rejection, retry backoff and service-only permissions. Concurrent
inserts and claimers are also checked against the real local database. Recovery
assertions cover atomic reservations, stale-reference result rejection, resolved
debt, and expiry protection for pending and already-paid attempts.

Validation uses synthetic provider responses and an isolated database. It does
not load production app settings, start the production application, send real
emails or make payment requests. No live migration, deployment, provider change
or Git remote update is part of this refinement.
