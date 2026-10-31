-- Acknowledge signed card-verification/refund webhooks after one durable write.
-- Provider lookups, card-binding checks and recovery charges run in a worker.
create table if not exists public.paystack_card_update_jobs (
    job_id uuid primary key default gen_random_uuid(),
    job_key text not null unique check (btrim(job_key) <> ''),
    payload jsonb not null check (jsonb_typeof(payload) = 'object'),
    status text not null default 'pending' check (status in ('pending', 'processing', 'completed', 'needs_attention')),
    attempts integer not null default 0 check (attempts >= 0),
    available_at timestamptz not null default now(),
    lease_token uuid,
    locked_until timestamptz,
    subscription_id uuid references public.subscriptions(subscription_id) on delete set null,
    result_code text,
    last_error text,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint paystack_card_update_processing_lease check (
        status <> 'processing' or (lease_token is not null and locked_until is not null)
    )
);

create index if not exists idx_paystack_card_update_jobs_due
    on public.paystack_card_update_jobs(available_at, created_at) where status = 'pending';
create index if not exists idx_paystack_card_update_jobs_expired_lease
    on public.paystack_card_update_jobs(locked_until) where status = 'processing';

alter table public.paystack_card_update_jobs enable row level security;
revoke all on public.paystack_card_update_jobs from public, anon, authenticated;
grant select, insert, update on public.paystack_card_update_jobs to service_role;

create or replace function public.claim_paystack_card_update_job()
returns setof public.paystack_card_update_jobs
language sql
security invoker
set search_path = ''
as $$
    with candidate as (
        select job_id
          from public.paystack_card_update_jobs
         where (status = 'pending' and available_at <= now())
            or (status = 'processing' and locked_until <= now())
         order by available_at, created_at
         limit 1
         for update skip locked
    )
    update public.paystack_card_update_jobs as job
       set status = 'processing',
           lease_token = gen_random_uuid(),
           locked_until = now() + interval '5 minutes',
           attempts = attempts + 1,
           updated_at = now()
      from candidate
     where job.job_id = candidate.job_id
    returning job.*;
$$;

revoke all on function public.claim_paystack_card_update_job() from public, anon, authenticated;
grant execute on function public.claim_paystack_card_update_job() to service_role;

-- Reserve the outstanding renewal, rather than just its webhook job. Distinct
-- card verifications must compete for the same recovery before any provider POST.
-- The reference survives a crash before/after the POST; only that reference may
-- be retried until its provider outcome is confirmed.
create or replace function public.reserve_paystack_card_update_attempt(
    p_recovery_id uuid, p_expected_status text, p_expected_reference text, p_attempt_reference text)
returns setof public.subscription_payment_recoveries
language sql
security invoker
set search_path = ''
as $$
    update public.subscription_payment_recoveries
       set authorization_retry_status = 'pending',
           authorization_retry_reference = p_attempt_reference,
           authorization_retry_attempted_at = now(),
           authorization_retry_due_at = null
     where recovery_id = p_recovery_id
       and resolved_at is null
       and (authorization_retry_status in ('failed', 'skipped')
            or (authorization_retry_status = 'pending'
                and authorization_retry_reference like 'card-update-retry-%'
                and authorization_retry_reference <> p_attempt_reference))
       and authorization_retry_status is not distinct from p_expected_status
       and authorization_retry_reference is not distinct from p_expected_reference
       and p_attempt_reference like 'card-update-retry-%'
    returning *;
$$;

-- Expiry must recheck the recovery in the same transaction as access removal.
-- An already-collected or still-ambiguous attempt cannot suspend a paid account.
create or replace function public.suspend_expired_payment_recovery(p_recovery_id uuid)
returns setof public.subscription_payment_recoveries
language plpgsql
security invoker
set search_path = ''
as $$
declare recovery public.subscription_payment_recoveries;
begin
    select * into recovery from public.subscription_payment_recoveries
     where recovery_id = p_recovery_id for update;
    if not found or recovery.resolved_at is not null or recovery.grace_ends_at > now()
       or recovery.grace_ends_at is null
       or recovery.authorization_retry_status in ('pending', 'succeeded') then
        return;
    end if;
    update public.subscriptions set status = 'failed'
     where subscription_id = recovery.subscription_id;
    return query update public.subscription_payment_recoveries
       set resolved_at = now(), resolution = 'suspended'
     where recovery_id = p_recovery_id returning *;
end;
$$;

revoke all on function public.reserve_paystack_card_update_attempt(uuid,text,text,text) from public, anon, authenticated;
grant execute on function public.reserve_paystack_card_update_attempt(uuid,text,text,text) to service_role;
revoke all on function public.suspend_expired_payment_recovery(uuid) from public, anon, authenticated;
grant execute on function public.suspend_expired_payment_recovery(uuid) to service_role;
