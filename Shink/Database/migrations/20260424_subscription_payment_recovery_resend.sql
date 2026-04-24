create table if not exists public.subscription_payment_recoveries (
    recovery_id uuid primary key default gen_random_uuid(),
    subscription_id uuid not null references public.subscriptions(subscription_id) on delete cascade,
    provider text not null,
    provider_payment_id text not null,
    first_failed_at timestamptz not null,
    grace_ends_at timestamptz not null,
    immediate_email_id text,
    warning_email_id text,
    suspension_email_id text,
    resolved_at timestamptz,
    resolution text check (resolution in ('recovered', 'suspended', 'cancelled')),
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now()
);

create unique index if not exists uq_subscription_payment_recoveries_active
    on public.subscription_payment_recoveries(subscription_id)
    where resolved_at is null;

create index if not exists idx_subscription_payment_recoveries_grace
    on public.subscription_payment_recoveries(grace_ends_at)
    where resolved_at is null;

create index if not exists idx_subscription_payment_recoveries_provider_payment
    on public.subscription_payment_recoveries(provider, provider_payment_id);

alter table public.subscription_payment_recoveries enable row level security;

drop trigger if exists trg_subscription_payment_recoveries_set_updated_at on public.subscription_payment_recoveries;
create trigger trg_subscription_payment_recoveries_set_updated_at
before update on public.subscription_payment_recoveries
for each row execute function public.set_updated_at();
