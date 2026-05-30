alter table public.subscription_events
    add column if not exists event_dedupe_key text,
    add column if not exists processing_status text not null default 'processed',
    add column if not exists processing_error text;

update public.subscription_events
set event_dedupe_key = 'legacy:' || event_id::text
where event_dedupe_key is null;

alter table public.subscription_events
    alter column event_dedupe_key set not null;

do $$
begin
    if not exists (
        select 1
        from pg_constraint
        where conname = 'subscription_events_processing_status_check'
          and conrelid = 'public.subscription_events'::regclass
    ) then
        alter table public.subscription_events
            add constraint subscription_events_processing_status_check
            check (processing_status in ('processing', 'processed', 'failed'));
    end if;
end $$;

create unique index if not exists uq_subscription_events_provider_dedupe_key
    on public.subscription_events(provider, event_dedupe_key);

create index if not exists idx_subscription_events_processing_status
    on public.subscription_events(provider, processing_status, received_at desc);

create table if not exists public.subscription_plan_changes (
    plan_change_id uuid primary key default gen_random_uuid(),
    subscriber_id uuid not null references public.subscribers(subscriber_id) on delete cascade,
    current_subscription_id uuid not null references public.subscriptions(subscription_id) on delete cascade,
    target_subscription_id uuid references public.subscriptions(subscription_id) on delete set null,
    provider text not null default 'paystack',
    provider_payment_id text,
    provider_email_token text,
    current_tier_code text not null,
    target_tier_code text not null,
    target_plan_slug text not null,
    change_type text not null,
    status text not null default 'pending',
    effective_at timestamptz not null,
    charged_amount_zar numeric(10, 2) not null default 0,
    charge_reference text,
    charge_status text,
    new_provider_payment_id text,
    new_provider_email_token text,
    failure_message text,
    completed_at timestamptz,
    failed_at timestamptz,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint subscription_plan_changes_provider_check
        check (provider in ('paystack')),
    constraint subscription_plan_changes_change_type_check
        check (change_type in ('upgrade', 'downgrade', 'billing-change')),
    constraint subscription_plan_changes_status_check
        check (status in (
            'pending',
            'charge_accepted',
            'charge_pending',
            'old_subscription_disabled',
            'old_subscription_scheduled',
            'provider_subscription_created',
            'completed',
            'failed'
        )),
    constraint subscription_plan_changes_amount_check
        check (charged_amount_zar >= 0)
);

alter table public.subscription_plan_changes enable row level security;

create unique index if not exists uq_subscription_plan_changes_current_target_effective
    on public.subscription_plan_changes(current_subscription_id, target_tier_code, effective_at);

create index if not exists idx_subscription_plan_changes_subscriber_status
    on public.subscription_plan_changes(subscriber_id, status, created_at desc);

create index if not exists idx_subscription_plan_changes_current_subscription
    on public.subscription_plan_changes(current_subscription_id, created_at desc);

comment on table public.subscription_plan_changes is
    'Durable Paystack subscription plan-change attempts used to avoid untracked partial upgrade or downgrade state.';
