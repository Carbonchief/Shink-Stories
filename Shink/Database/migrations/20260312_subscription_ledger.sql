create extension if not exists pgcrypto;

create table if not exists public.subscription_tiers (
    tier_code text primary key,
    display_name text not null,
    description text,
    billing_period_months integer not null check (billing_period_months > 0),
    price_zar numeric(10, 2) not null check (price_zar >= 0),
    payfast_plan_slug text not null unique,
    is_active boolean not null default true,
    created_at timestamptz not null default now(),
    constraint subscription_tiers_tier_code_format check (tier_code ~ '^[a-z0-9_]+$')
);

create table if not exists public.subscribers (
    subscriber_id uuid primary key default gen_random_uuid(),
    email text not null unique,
    first_name text,
    last_name text,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint subscribers_email_format check (position('@' in email) > 1)
);

create table if not exists public.subscriptions (
    subscription_id uuid primary key default gen_random_uuid(),
    subscriber_id uuid not null references public.subscribers(subscriber_id) on delete restrict,
    tier_code text not null references public.subscription_tiers(tier_code) on delete restrict,
    provider text not null default 'payfast',
    provider_payment_id text not null unique,
    provider_transaction_id text unique,
    provider_token text,
    status text not null check (status in ('pending', 'active', 'cancelled', 'failed')),
    subscribed_at timestamptz not null,
    next_renewal_at timestamptz,
    cancelled_at timestamptz,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now()
);

create index if not exists idx_subscriptions_subscriber_status
    on public.subscriptions(subscriber_id, status);

create index if not exists idx_subscriptions_next_renewal
    on public.subscriptions(next_renewal_at);

create table if not exists public.subscription_events (
    event_id bigint generated always as identity primary key,
    subscription_id uuid references public.subscriptions(subscription_id) on delete set null,
    provider text not null default 'payfast',
    provider_payment_id text,
    provider_transaction_id text,
    event_type text not null,
    event_status text,
    received_at timestamptz not null default now(),
    payload jsonb not null
);

create index if not exists idx_subscription_events_subscription_id
    on public.subscription_events(subscription_id);

create index if not exists idx_subscription_events_received_at
    on public.subscription_events(received_at desc);

create index if not exists idx_subscription_events_payload_gin
    on public.subscription_events using gin (payload jsonb_path_ops);

alter table public.subscription_tiers enable row level security;
alter table public.subscribers enable row level security;
alter table public.subscriptions enable row level security;
alter table public.subscription_events enable row level security;

drop policy if exists subscription_tiers_read on public.subscription_tiers;
create policy subscription_tiers_read
on public.subscription_tiers
for select
to anon, authenticated
using (is_active = true);

create or replace function public.set_updated_at()
returns trigger
language plpgsql
set search_path = pg_catalog, public
as $$
begin
    new.updated_at = now();
    return new;
end;
$$;

drop trigger if exists trg_subscribers_set_updated_at on public.subscribers;
create trigger trg_subscribers_set_updated_at
before update on public.subscribers
for each row execute function public.set_updated_at();

drop trigger if exists trg_subscriptions_set_updated_at on public.subscriptions;
create trigger trg_subscriptions_set_updated_at
before update on public.subscriptions
for each row execute function public.set_updated_at();

insert into public.subscription_tiers (
    tier_code,
    display_name,
    description,
    billing_period_months,
    price_zar,
    payfast_plan_slug,
    is_active
)
values
    (
        'story_corner_monthly',
        'Story Corner Monthly',
        'Monthly subscription for Story Corner content.',
        1,
        55.00,
        'storie-hoekie-maandeliks',
        true
    ),
    (
        'all_stories_monthly',
        'All Stories Monthly',
        'Monthly subscription for full Schink Stories access.',
        1,
        79.00,
        'schink-stories-maandeliks',
        true
    ),
    (
        'all_stories_yearly',
        'All Stories Yearly',
        'Yearly subscription for full Schink Stories access.',
        12,
        790.00,
        'schink-stories-jaarliks',
        true
    )
on conflict (tier_code) do update
set
    display_name = excluded.display_name,
    description = excluded.description,
    billing_period_months = excluded.billing_period_months,
    price_zar = excluded.price_zar,
    payfast_plan_slug = excluded.payfast_plan_slug,
    is_active = excluded.is_active;
