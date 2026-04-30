create table if not exists public.site_settings (
    setting_key text primary key,
    setting_value jsonb not null,
    updated_at timestamptz not null default now(),
    updated_by text,
    constraint site_settings_setting_key_format check (setting_key ~ '^[a-z0-9_]+$')
);

alter table public.site_settings enable row level security;

insert into public.site_settings (setting_key, setting_value)
values ('subscription_code_signup_bypass_enabled', 'true'::jsonb)
on conflict (setting_key) do nothing;

alter table public.subscriptions
    drop constraint if exists subscriptions_source_system_check;

alter table public.subscriptions
    add constraint subscriptions_source_system_check
    check (source_system in ('shink_app', 'wordpress_pmpro', 'admin_override', 'discount_code'));

create table if not exists public.subscription_discount_codes (
    discount_code_id uuid primary key default gen_random_uuid(),
    code text not null default '',
    normalized_code text not null,
    display_name text,
    description text,
    is_group_code boolean not null default false,
    parent_discount_code_id uuid references public.subscription_discount_codes(discount_code_id) on delete set null,
    starts_at timestamptz,
    expires_at timestamptz,
    max_uses integer not null default 0 check (max_uses >= 0),
    one_use_per_user boolean not null default false,
    bypass_payment boolean not null default true,
    is_active boolean not null default true,
    source_system text not null default 'shink_app',
    source_discount_code_id bigint unique,
    source_group_code_id bigint unique,
    source_parent_discount_code_id bigint,
    source_order_id bigint,
    raw_source jsonb,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint subscription_discount_codes_source_system_check check (source_system in ('shink_app', 'wordpress_pmpro')),
    constraint subscription_discount_codes_manual_code_required check (
        source_group_code_id is not null
        or source_discount_code_id is not null
        or nullif(btrim(code), '') is not null
    )
);

create unique index if not exists uq_subscription_discount_codes_normalized_code
    on public.subscription_discount_codes(normalized_code);

create index if not exists idx_subscription_discount_codes_parent
    on public.subscription_discount_codes(parent_discount_code_id);

create index if not exists idx_subscription_discount_codes_source_system
    on public.subscription_discount_codes(source_system, is_active, expires_at);

create table if not exists public.subscription_discount_code_tiers (
    discount_code_tier_id uuid primary key default gen_random_uuid(),
    discount_code_id uuid not null references public.subscription_discount_codes(discount_code_id) on delete cascade,
    tier_code text not null references public.subscription_tiers(tier_code) on delete cascade,
    initial_payment_zar numeric(18, 8) not null default 0,
    billing_amount_zar numeric(18, 8) not null default 0,
    cycle_number integer not null default 0,
    cycle_period text,
    billing_limit integer,
    trial_amount_zar numeric(18, 8) not null default 0,
    trial_limit integer not null default 0,
    expiration_number integer,
    expiration_period text,
    source_level_id integer,
    source_membership_level_name text,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint subscription_discount_code_tiers_cycle_number_check check (cycle_number >= 0),
    constraint subscription_discount_code_tiers_trial_limit_check check (trial_limit >= 0),
    constraint subscription_discount_code_tiers_expiration_number_check check (expiration_number is null or expiration_number >= 0),
    constraint subscription_discount_code_tiers_unique unique (discount_code_id, tier_code)
);

create index if not exists idx_subscription_discount_code_tiers_tier_code
    on public.subscription_discount_code_tiers(tier_code);

create table if not exists public.subscription_discount_code_redemptions (
    redemption_id uuid primary key default gen_random_uuid(),
    discount_code_id uuid not null references public.subscription_discount_codes(discount_code_id) on delete cascade,
    subscriber_id uuid references public.subscribers(subscriber_id) on delete set null,
    email text not null,
    tier_code text references public.subscription_tiers(tier_code) on delete set null,
    redeemed_at timestamptz not null,
    access_expires_at timestamptz,
    granted_subscription_id uuid references public.subscriptions(subscription_id) on delete set null,
    source_system text not null default 'shink_app',
    source_redemption_id bigint,
    source_order_id bigint,
    source_wordpress_user_id bigint,
    bypassed_payment boolean not null default true,
    metadata jsonb,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint subscription_discount_code_redemptions_source_system_check check (source_system in ('shink_app', 'wordpress_pmpro')),
    constraint subscription_discount_code_redemptions_email_format check (position('@' in email) > 1)
);

create unique index if not exists uq_subscription_discount_code_redemptions_source
    on public.subscription_discount_code_redemptions(source_system, source_redemption_id);

create index if not exists idx_subscription_discount_code_redemptions_code_redeemed
    on public.subscription_discount_code_redemptions(discount_code_id, redeemed_at desc);

create index if not exists idx_subscription_discount_code_redemptions_email
    on public.subscription_discount_code_redemptions(email, redeemed_at desc);

alter table public.subscription_discount_codes enable row level security;
alter table public.subscription_discount_code_tiers enable row level security;
alter table public.subscription_discount_code_redemptions enable row level security;

drop trigger if exists trg_site_settings_set_updated_at on public.site_settings;
create trigger trg_site_settings_set_updated_at
before update on public.site_settings
for each row execute function public.set_updated_at();

drop trigger if exists trg_subscription_discount_codes_set_updated_at on public.subscription_discount_codes;
create trigger trg_subscription_discount_codes_set_updated_at
before update on public.subscription_discount_codes
for each row execute function public.set_updated_at();

drop trigger if exists trg_subscription_discount_code_tiers_set_updated_at on public.subscription_discount_code_tiers;
create trigger trg_subscription_discount_code_tiers_set_updated_at
before update on public.subscription_discount_code_tiers
for each row execute function public.set_updated_at();

drop trigger if exists trg_subscription_discount_code_redemptions_set_updated_at on public.subscription_discount_code_redemptions;
create trigger trg_subscription_discount_code_redemptions_set_updated_at
before update on public.subscription_discount_code_redemptions
for each row execute function public.set_updated_at();

create or replace function public.subscription_discount_codes_set_normalized_code()
returns trigger
language plpgsql
set search_path = pg_catalog, public
as $$
declare
    trimmed_code text;
begin
    trimmed_code := nullif(btrim(new.code), '');
    new.code := coalesce(trimmed_code, '');
    new.normalized_code :=
        case
            when trimmed_code is not null then lower(trimmed_code)
            when new.source_group_code_id is not null then '__source_group_' || new.source_group_code_id::text
            when new.source_discount_code_id is not null then '__source_code_' || new.source_discount_code_id::text
            else lower(gen_random_uuid()::text)
        end;

    return new;
end;
$$;

drop trigger if exists trg_subscription_discount_codes_set_normalized_code on public.subscription_discount_codes;
create trigger trg_subscription_discount_codes_set_normalized_code
before insert or update on public.subscription_discount_codes
for each row execute function public.subscription_discount_codes_set_normalized_code();

create or replace function public.import_wordpress_subscription_discount_codes(payload jsonb)
returns integer
language plpgsql
security definer
set search_path = public
as $$
declare
    affected_count integer := 0;
    linked_group_count integer := 0;
begin
    with rows as (
        select
            rows.code,
            rows.display_name,
            rows.description,
            coalesce(rows.is_group_code, false) as is_group_code,
            rows.starts_at,
            rows.expires_at,
            coalesce(rows.max_uses, 0) as max_uses,
            coalesce(rows.one_use_per_user, false) as one_use_per_user,
            coalesce(rows.bypass_payment, true) as bypass_payment,
            coalesce(rows.is_active, true) as is_active,
            coalesce(nullif(btrim(rows.source_system), ''), 'wordpress_pmpro') as source_system,
            rows.source_discount_code_id,
            rows.source_group_code_id,
            rows.source_parent_discount_code_id,
            rows.source_order_id,
            rows.raw_source
        from jsonb_to_recordset(coalesce(payload, '[]'::jsonb)) as rows(
            code text,
            display_name text,
            description text,
            is_group_code boolean,
            starts_at timestamptz,
            expires_at timestamptz,
            max_uses integer,
            one_use_per_user boolean,
            bypass_payment boolean,
            is_active boolean,
            source_system text,
            source_discount_code_id bigint,
            source_group_code_id bigint,
            source_parent_discount_code_id bigint,
            source_order_id bigint,
            raw_source jsonb
        )
    ),
    upsert_parent as (
        insert into public.subscription_discount_codes (
            code,
            display_name,
            description,
            is_group_code,
            parent_discount_code_id,
            starts_at,
            expires_at,
            max_uses,
            one_use_per_user,
            bypass_payment,
            is_active,
            source_system,
            source_discount_code_id,
            source_parent_discount_code_id,
            source_order_id,
            raw_source
        )
        select
            coalesce(rows.code, ''),
            nullif(btrim(rows.display_name), ''),
            nullif(btrim(rows.description), ''),
            false,
            null,
            rows.starts_at,
            rows.expires_at,
            rows.max_uses,
            rows.one_use_per_user,
            rows.bypass_payment,
            rows.is_active,
            rows.source_system,
            rows.source_discount_code_id,
            rows.source_parent_discount_code_id,
            rows.source_order_id,
            rows.raw_source
        from rows
        where not rows.is_group_code
          and rows.source_discount_code_id is not null
        on conflict (source_discount_code_id) do update
        set code = excluded.code,
            display_name = excluded.display_name,
            description = excluded.description,
            starts_at = excluded.starts_at,
            expires_at = excluded.expires_at,
            max_uses = excluded.max_uses,
            one_use_per_user = excluded.one_use_per_user,
            bypass_payment = excluded.bypass_payment,
            is_active = excluded.is_active,
            source_system = excluded.source_system,
            source_parent_discount_code_id = excluded.source_parent_discount_code_id,
            source_order_id = excluded.source_order_id,
            raw_source = excluded.raw_source
        returning 1
    ),
    upsert_group as (
        insert into public.subscription_discount_codes (
            code,
            display_name,
            description,
            is_group_code,
            parent_discount_code_id,
            starts_at,
            expires_at,
            max_uses,
            one_use_per_user,
            bypass_payment,
            is_active,
            source_system,
            source_group_code_id,
            source_parent_discount_code_id,
            source_order_id,
            raw_source
        )
        select
            coalesce(rows.code, ''),
            nullif(btrim(rows.display_name), ''),
            nullif(btrim(rows.description), ''),
            true,
            parent.discount_code_id,
            rows.starts_at,
            rows.expires_at,
            case when rows.max_uses <= 0 then 1 else rows.max_uses end,
            true,
            rows.bypass_payment,
            rows.is_active,
            rows.source_system,
            rows.source_group_code_id,
            rows.source_parent_discount_code_id,
            rows.source_order_id,
            rows.raw_source
        from rows
        left join public.subscription_discount_codes parent
            on parent.source_discount_code_id = rows.source_parent_discount_code_id
        where rows.is_group_code
          and rows.source_group_code_id is not null
        on conflict (source_group_code_id) do update
        set code = excluded.code,
            display_name = excluded.display_name,
            description = excluded.description,
            parent_discount_code_id = excluded.parent_discount_code_id,
            starts_at = excluded.starts_at,
            expires_at = excluded.expires_at,
            max_uses = excluded.max_uses,
            one_use_per_user = excluded.one_use_per_user,
            bypass_payment = excluded.bypass_payment,
            is_active = excluded.is_active,
            source_system = excluded.source_system,
            source_parent_discount_code_id = excluded.source_parent_discount_code_id,
            source_order_id = excluded.source_order_id,
            raw_source = excluded.raw_source
        returning 1
    )
    update public.subscription_discount_codes child
    set parent_discount_code_id = parent.discount_code_id
    from public.subscription_discount_codes parent
    where child.is_group_code
      and child.source_system = 'wordpress_pmpro'
      and child.source_parent_discount_code_id is not null
      and parent.source_discount_code_id = child.source_parent_discount_code_id
      and child.parent_discount_code_id is distinct from parent.discount_code_id;

    get diagnostics linked_group_count = row_count;

    select
        coalesce((select count(*) from upsert_parent), 0)
        + coalesce((select count(*) from upsert_group), 0)
        + coalesce(linked_group_count, 0)
    into affected_count;

    return coalesce(affected_count, 0);
end;
$$;

create or replace function public.import_wordpress_subscription_discount_code_tiers(payload jsonb)
returns integer
language plpgsql
security definer
set search_path = public
as $$
declare
    affected_count integer := 0;
begin
    with rows as (
        select
            rows.source_discount_code_id,
            rows.source_group_code_id,
            rows.tier_code,
            coalesce(rows.initial_payment_zar, 0) as initial_payment_zar,
            coalesce(rows.billing_amount_zar, 0) as billing_amount_zar,
            coalesce(rows.cycle_number, 0) as cycle_number,
            nullif(btrim(rows.cycle_period), '') as cycle_period,
            rows.billing_limit,
            coalesce(rows.trial_amount_zar, 0) as trial_amount_zar,
            coalesce(rows.trial_limit, 0) as trial_limit,
            rows.expiration_number,
            nullif(btrim(rows.expiration_period), '') as expiration_period,
            rows.source_level_id,
            nullif(btrim(rows.source_membership_level_name), '') as source_membership_level_name
        from jsonb_to_recordset(coalesce(payload, '[]'::jsonb)) as rows(
            source_discount_code_id bigint,
            source_group_code_id bigint,
            tier_code text,
            initial_payment_zar numeric,
            billing_amount_zar numeric,
            cycle_number integer,
            cycle_period text,
            billing_limit integer,
            trial_amount_zar numeric,
            trial_limit integer,
            expiration_number integer,
            expiration_period text,
            source_level_id integer,
            source_membership_level_name text
        )
    ),
    mapped as (
        select
            code.discount_code_id,
            lower(btrim(rows.tier_code)) as tier_code,
            rows.initial_payment_zar,
            rows.billing_amount_zar,
            rows.cycle_number,
            rows.cycle_period,
            rows.billing_limit,
            rows.trial_amount_zar,
            rows.trial_limit,
            rows.expiration_number,
            rows.expiration_period,
            rows.source_level_id,
            rows.source_membership_level_name
        from rows
        inner join public.subscription_discount_codes code
            on (
                rows.source_group_code_id is not null
                and code.source_group_code_id = rows.source_group_code_id
            ) or (
                rows.source_group_code_id is null
                and rows.source_discount_code_id is not null
                and code.source_discount_code_id = rows.source_discount_code_id
            )
        where nullif(btrim(rows.tier_code), '') is not null
    ),
    upserted as (
        insert into public.subscription_discount_code_tiers (
            discount_code_id,
            tier_code,
            initial_payment_zar,
            billing_amount_zar,
            cycle_number,
            cycle_period,
            billing_limit,
            trial_amount_zar,
            trial_limit,
            expiration_number,
            expiration_period,
            source_level_id,
            source_membership_level_name
        )
        select
            mapped.discount_code_id,
            mapped.tier_code,
            mapped.initial_payment_zar,
            mapped.billing_amount_zar,
            mapped.cycle_number,
            mapped.cycle_period,
            mapped.billing_limit,
            mapped.trial_amount_zar,
            mapped.trial_limit,
            mapped.expiration_number,
            mapped.expiration_period,
            mapped.source_level_id,
            mapped.source_membership_level_name
        from mapped
        on conflict (discount_code_id, tier_code) do update
        set initial_payment_zar = excluded.initial_payment_zar,
            billing_amount_zar = excluded.billing_amount_zar,
            cycle_number = excluded.cycle_number,
            cycle_period = excluded.cycle_period,
            billing_limit = excluded.billing_limit,
            trial_amount_zar = excluded.trial_amount_zar,
            trial_limit = excluded.trial_limit,
            expiration_number = excluded.expiration_number,
            expiration_period = excluded.expiration_period,
            source_level_id = excluded.source_level_id,
            source_membership_level_name = excluded.source_membership_level_name
        returning 1
    )
    select count(*) into affected_count from upserted;

    return coalesce(affected_count, 0);
end;
$$;

create or replace function public.import_wordpress_subscription_discount_code_redemptions(payload jsonb)
returns integer
language plpgsql
security definer
set search_path = public
as $$
declare
    affected_count integer := 0;
begin
    with rows as (
        select
            rows.source_redemption_id,
            rows.source_discount_code_id,
            rows.source_group_code_id,
            rows.source_order_id,
            rows.source_wordpress_user_id,
            lower(trim(rows.email)) as email,
            lower(trim(rows.tier_code)) as tier_code,
            rows.redeemed_at,
            rows.access_expires_at,
            coalesce(rows.bypassed_payment, true) as bypassed_payment,
            rows.metadata
        from jsonb_to_recordset(coalesce(payload, '[]'::jsonb)) as rows(
            source_redemption_id bigint,
            source_discount_code_id bigint,
            source_group_code_id bigint,
            source_order_id bigint,
            source_wordpress_user_id bigint,
            email text,
            tier_code text,
            redeemed_at timestamptz,
            access_expires_at timestamptz,
            bypassed_payment boolean,
            metadata jsonb
        )
        where nullif(trim(rows.email), '') is not null
          and rows.redeemed_at is not null
    ),
    mapped as (
        select
            code.discount_code_id,
            subscriber.subscriber_id,
            rows.email,
            nullif(rows.tier_code, '') as tier_code,
            rows.redeemed_at,
            rows.access_expires_at,
            rows.source_redemption_id,
            rows.source_order_id,
            rows.source_wordpress_user_id,
            rows.bypassed_payment,
            rows.metadata
        from rows
        inner join public.subscription_discount_codes code
            on (
                rows.source_group_code_id is not null
                and code.source_group_code_id = rows.source_group_code_id
            ) or (
                rows.source_group_code_id is null
                and rows.source_discount_code_id is not null
                and code.source_discount_code_id = rows.source_discount_code_id
            )
        left join public.subscribers subscriber
            on subscriber.email = rows.email
    ),
    upserted as (
        insert into public.subscription_discount_code_redemptions (
            discount_code_id,
            subscriber_id,
            email,
            tier_code,
            redeemed_at,
            access_expires_at,
            source_system,
            source_redemption_id,
            source_order_id,
            source_wordpress_user_id,
            bypassed_payment,
            metadata
        )
        select
            mapped.discount_code_id,
            mapped.subscriber_id,
            mapped.email,
            mapped.tier_code,
            mapped.redeemed_at,
            mapped.access_expires_at,
            'wordpress_pmpro',
            mapped.source_redemption_id,
            mapped.source_order_id,
            mapped.source_wordpress_user_id,
            mapped.bypassed_payment,
            mapped.metadata
        from mapped
        on conflict (source_system, source_redemption_id) do update
        set discount_code_id = excluded.discount_code_id,
            subscriber_id = excluded.subscriber_id,
            email = excluded.email,
            tier_code = excluded.tier_code,
            redeemed_at = excluded.redeemed_at,
            access_expires_at = excluded.access_expires_at,
            source_order_id = excluded.source_order_id,
            source_wordpress_user_id = excluded.source_wordpress_user_id,
            bypassed_payment = excluded.bypassed_payment,
            metadata = excluded.metadata
        returning 1
    )
    select count(*) into affected_count from upserted;

    return coalesce(affected_count, 0);
end;
$$;

revoke all on function public.import_wordpress_subscription_discount_codes(jsonb) from public, anon, authenticated;
revoke all on function public.import_wordpress_subscription_discount_code_tiers(jsonb) from public, anon, authenticated;
revoke all on function public.import_wordpress_subscription_discount_code_redemptions(jsonb) from public, anon, authenticated;

grant execute on function public.import_wordpress_subscription_discount_codes(jsonb) to service_role;
grant execute on function public.import_wordpress_subscription_discount_code_tiers(jsonb) to service_role;
grant execute on function public.import_wordpress_subscription_discount_code_redemptions(jsonb) to service_role;
