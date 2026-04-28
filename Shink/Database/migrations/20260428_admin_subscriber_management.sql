alter table public.subscribers
    add column if not exists disabled_at timestamptz,
    add column if not exists disabled_by_admin_email text,
    add column if not exists disabled_reason text;

create index if not exists idx_subscribers_disabled_at
    on public.subscribers(disabled_at)
    where disabled_at is not null;

alter table public.subscriptions
    drop constraint if exists subscriptions_source_system_check;

alter table public.subscriptions
    add constraint subscriptions_source_system_check
    check (source_system in ('shink_app', 'wordpress_pmpro', 'admin_override'));

create index if not exists idx_subscriptions_admin_override_active
    on public.subscriptions(subscriber_id, tier_code, next_renewal_at)
    where source_system = 'admin_override' and status = 'active';

create table if not exists public.subscriber_admin_audit (
    audit_id uuid primary key default gen_random_uuid(),
    subscriber_id uuid not null references public.subscribers(subscriber_id) on delete cascade,
    admin_email text not null,
    action_key text not null,
    notes text,
    metadata jsonb not null default '{}'::jsonb,
    created_at timestamptz not null default now(),
    constraint subscriber_admin_audit_admin_email_format check (position('@' in admin_email) > 1),
    constraint subscriber_admin_audit_action_key_not_blank check (btrim(action_key) <> '')
);

create index if not exists idx_subscriber_admin_audit_subscriber_created
    on public.subscriber_admin_audit(subscriber_id, created_at desc);

alter table public.subscriber_admin_audit enable row level security;
