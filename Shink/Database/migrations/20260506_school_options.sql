alter table public.subscriptions
    drop constraint if exists subscriptions_source_system_check;

alter table public.subscriptions
    add constraint subscriptions_source_system_check
    check (source_system in ('shink_app', 'wordpress_pmpro', 'admin_override', 'discount_code', 'school_seat'));

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
        'school_small_yearly',
        'Skool Klein',
        'Jaarlikse skooltoegang vir 4 klaskamers.',
        12,
        6250.00,
        'skool-klein-jaarliks',
        true
    ),
    (
        'school_medium_yearly',
        'Skool Medium',
        'Jaarlikse skooltoegang vir 6 klaskamers.',
        12,
        8640.00,
        'skool-medium-jaarliks',
        true
    ),
    (
        'school_large_yearly',
        'Skool Groot',
        'Jaarlikse skooltoegang vir 8 klaskamers.',
        12,
        11520.00,
        'skool-groot-jaarliks',
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

create table if not exists public.school_accounts (
    school_account_id uuid primary key default gen_random_uuid(),
    school_name text not null default 'My skool',
    admin_email text not null,
    plan_tier_code text not null references public.subscription_tiers(tier_code) on delete restrict,
    plan_name text not null,
    slot_limit integer not null check (slot_limit > 0),
    admin_uses_slot boolean not null default true,
    status text not null default 'active' check (status in ('active', 'paused', 'cancelled')),
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint school_accounts_admin_email_format check (position('@' in admin_email) > 1)
);

create unique index if not exists uq_school_accounts_admin_email_active
    on public.school_accounts(lower(admin_email))
    where status = 'active';

create index if not exists idx_school_accounts_plan_tier
    on public.school_accounts(plan_tier_code);

create table if not exists public.school_seats (
    school_seat_id uuid primary key default gen_random_uuid(),
    school_account_id uuid not null references public.school_accounts(school_account_id) on delete cascade,
    email text not null,
    display_name text,
    role text not null default 'teacher' check (role in ('school_admin', 'teacher')),
    status text not null default 'invited' check (status in ('invited', 'accepted', 'removed')),
    invited_by_email text not null,
    invited_at timestamptz not null default now(),
    accepted_at timestamptz,
    removed_at timestamptz,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint school_seats_email_format check (position('@' in email) > 1),
    constraint school_seats_invited_by_email_format check (position('@' in invited_by_email) > 1)
);

create unique index if not exists uq_school_seats_account_email
    on public.school_seats(school_account_id, email);

create index if not exists idx_school_seats_account_status
    on public.school_seats(school_account_id, status);

drop trigger if exists trg_school_accounts_set_updated_at on public.school_accounts;
create trigger trg_school_accounts_set_updated_at
before update on public.school_accounts
for each row execute function public.set_updated_at();

drop trigger if exists trg_school_seats_set_updated_at on public.school_seats;
create trigger trg_school_seats_set_updated_at
before update on public.school_seats
for each row execute function public.set_updated_at();

alter table public.school_accounts enable row level security;
alter table public.school_seats enable row level security;
