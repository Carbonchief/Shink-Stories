-- Service-only delivery claims survive webhook retries and app restarts.
create table if not exists public.admin_ops_alert_deliveries (
    alert_key text primary key,
    request_payload jsonb not null,
    first_attempt_at timestamptz,
    lease_token uuid not null default gen_random_uuid(),
    lease_expires_at timestamptz not null default now(),
    sent_at timestamptz,
    provider_email_id text,
    constraint admin_ops_alert_key_not_blank check (btrim(alert_key) <> ''),
    constraint admin_ops_alert_request_object check (jsonb_typeof(request_payload) = 'object')
);

alter table public.admin_ops_alert_deliveries enable row level security;
revoke all on public.admin_ops_alert_deliveries from public, anon, authenticated;
grant select, insert, update on public.admin_ops_alert_deliveries to service_role;

-- The first payload (including its timestamp) is immutable. Retrying within
-- Resend's 24-hour window must use both the same key and the same payload.
-- Stop after 23 hours if delivery remains uncertain: never resend an old
-- incident after the provider's duplicate protection has expired.
create or replace function public.claim_admin_ops_alert(p_alert_key text, p_request_payload jsonb)
returns table (alert_key text, request_payload jsonb, lease_token uuid)
language sql
security invoker
set search_path = ''
as $$
    insert into public.admin_ops_alert_deliveries as delivery (alert_key, request_payload, first_attempt_at, lease_expires_at)
    values (p_alert_key, p_request_payload, now(), now() + interval '5 minutes')
    on conflict (alert_key) do update
       set first_attempt_at = coalesce(delivery.first_attempt_at, now()),
           lease_token = gen_random_uuid(),
           lease_expires_at = now() + interval '5 minutes'
     where delivery.sent_at is null
       and delivery.lease_expires_at < now()
       and (delivery.first_attempt_at is null or delivery.first_attempt_at > now() - interval '23 hours')
    returning delivery.alert_key, delivery.request_payload, delivery.lease_token;
$$;

revoke all on function public.claim_admin_ops_alert(text, jsonb) from public, anon, authenticated;
grant execute on function public.claim_admin_ops_alert(text, jsonb) to service_role;

-- Dispatch previously queued or ambiguously delivered alerts without requiring
-- another webhook occurrence. Unsent backlog has no provider clock until claimed.
create index if not exists idx_admin_ops_alert_deliveries_pending
    on public.admin_ops_alert_deliveries(lease_expires_at) where sent_at is null;

create or replace function public.claim_pending_admin_ops_alert()
returns setof public.admin_ops_alert_deliveries
language sql
security invoker
set search_path = ''
as $$
    with candidate as (
        select alert_key from public.admin_ops_alert_deliveries
         where sent_at is null and lease_expires_at <= now()
           and (first_attempt_at is null or first_attempt_at > now() - interval '23 hours')
         order by lease_expires_at, alert_key limit 1 for update skip locked
    )
    update public.admin_ops_alert_deliveries as delivery
       set first_attempt_at = coalesce(delivery.first_attempt_at, now()),
           lease_token = gen_random_uuid(), lease_expires_at = now() + interval '5 minutes'
      from candidate where delivery.alert_key = candidate.alert_key
    returning delivery.*;
$$;

revoke all on function public.claim_pending_admin_ops_alert() from public, anon, authenticated;
grant execute on function public.claim_pending_admin_ops_alert() to service_role;
