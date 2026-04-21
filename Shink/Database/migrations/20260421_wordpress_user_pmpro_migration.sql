create schema if not exists private;

revoke all on schema private from public;

alter table public.subscribers
    add column if not exists profile_image_url text;

alter table public.subscribers
    add column if not exists profile_image_object_key text;

alter table public.subscribers
    add column if not exists profile_image_content_type text;

alter table public.subscriptions
    add column if not exists source_system text not null default 'shink_app';

update public.subscriptions
set source_system = 'shink_app'
where source_system is null;

create index if not exists idx_subscriptions_source_system_status
    on public.subscriptions(source_system, status);

alter table public.subscriptions
    drop constraint if exists subscriptions_provider_check;

alter table public.subscriptions
    add constraint subscriptions_provider_check
    check (provider in ('payfast', 'paystack', 'free'));

do $$
begin
    if not exists (
        select 1
        from pg_constraint
        where conname = 'subscriptions_source_system_check'
          and conrelid = 'public.subscriptions'::regclass
    ) then
        alter table public.subscriptions
            add constraint subscriptions_source_system_check
            check (source_system in ('shink_app', 'wordpress_pmpro'));
    end if;
end
$$;

create table if not exists private.wordpress_users (
    wp_user_id bigint primary key,
    normalized_email text not null unique,
    user_login text not null,
    user_nicename text not null,
    display_name text,
    first_name text,
    last_name text,
    mobile_number text,
    user_registered timestamptz,
    password_hash text,
    password_hash_format text,
    is_password_migrated boolean not null default false,
    has_duplicate_email boolean not null default false,
    avatar_source_url text,
    avatar_source_attachment_id bigint,
    avatar_source_meta_key text,
    profile_image_url text,
    profile_image_object_key text,
    profile_image_content_type text,
    last_synced_at timestamptz not null default now()
);

create table if not exists private.wordpress_membership_periods (
    wp_membership_period_id bigint primary key,
    wp_user_id bigint not null,
    normalized_email text,
    membership_level_id integer,
    membership_level_name text,
    tier_code text,
    code_id bigint,
    status text,
    startdate timestamptz,
    enddate timestamptz,
    modified_at timestamptz,
    initial_payment numeric(18,8),
    billing_amount numeric(18,8),
    cycle_number integer,
    cycle_period text,
    billing_limit integer,
    trial_amount numeric(18,8),
    trial_limit integer,
    raw_row jsonb not null default '{}'::jsonb,
    last_synced_at timestamptz not null default now()
);

create index if not exists idx_wordpress_membership_periods_email_status
    on private.wordpress_membership_periods(normalized_email, status);

create table if not exists private.wordpress_membership_orders (
    wp_order_id bigint primary key,
    wp_user_id bigint not null,
    normalized_email text,
    membership_level_id integer,
    membership_level_name text,
    tier_code text,
    code text,
    session_id text,
    status text,
    gateway text,
    gateway_environment text,
    payment_type text,
    payment_transaction_id text,
    subscription_transaction_id text,
    billing_name text,
    billing_phone text,
    billing_country text,
    subtotal text,
    tax text,
    couponamount text,
    total text,
    order_timestamp timestamptz,
    raw_meta jsonb not null default '{}'::jsonb,
    raw_row jsonb not null default '{}'::jsonb,
    last_synced_at timestamptz not null default now()
);

create index if not exists idx_wordpress_membership_orders_email_status
    on private.wordpress_membership_orders(normalized_email, status);

create table if not exists private.wordpress_subscriptions (
    wp_subscription_id bigint primary key,
    wp_user_id bigint not null,
    normalized_email text,
    membership_level_id integer,
    membership_level_name text,
    tier_code text,
    gateway text,
    gateway_environment text,
    subscription_transaction_id text,
    status text,
    startdate timestamptz,
    enddate timestamptz,
    next_payment_date timestamptz,
    modified_at timestamptz,
    billing_amount numeric(18,8),
    cycle_number integer,
    cycle_period text,
    billing_limit integer,
    trial_amount numeric(18,8),
    trial_limit integer,
    raw_meta jsonb not null default '{}'::jsonb,
    raw_row jsonb not null default '{}'::jsonb,
    last_synced_at timestamptz not null default now()
);

create index if not exists idx_wordpress_subscriptions_email_status
    on private.wordpress_subscriptions(normalized_email, status);

create or replace function public.import_wordpress_users(payload jsonb)
returns integer
language sql
security definer
set search_path = public, private
as $$
    with rows as (
        select *
        from jsonb_to_recordset(coalesce(payload, '[]'::jsonb)) as x(
            wp_user_id bigint,
            normalized_email text,
            user_login text,
            user_nicename text,
            display_name text,
            first_name text,
            last_name text,
            mobile_number text,
            user_registered timestamptz,
            password_hash text,
            password_hash_format text,
            is_password_migrated boolean,
            has_duplicate_email boolean,
            avatar_source_url text,
            avatar_source_attachment_id bigint,
            avatar_source_meta_key text,
            profile_image_url text,
            profile_image_object_key text,
            profile_image_content_type text
        )
    ),
    upserted as (
        insert into private.wordpress_users (
            wp_user_id,
            normalized_email,
            user_login,
            user_nicename,
            display_name,
            first_name,
            last_name,
            mobile_number,
            user_registered,
            password_hash,
            password_hash_format,
            is_password_migrated,
            has_duplicate_email,
            avatar_source_url,
            avatar_source_attachment_id,
            avatar_source_meta_key,
            profile_image_url,
            profile_image_object_key,
            profile_image_content_type,
            last_synced_at
        )
        select
            rows.wp_user_id,
            lower(btrim(rows.normalized_email)),
            coalesce(rows.user_login, ''),
            coalesce(rows.user_nicename, ''),
            nullif(btrim(rows.display_name), ''),
            nullif(btrim(rows.first_name), ''),
            nullif(btrim(rows.last_name), ''),
            nullif(btrim(rows.mobile_number), ''),
            rows.user_registered,
            nullif(rows.password_hash, ''),
            nullif(rows.password_hash_format, ''),
            coalesce(rows.is_password_migrated, false),
            coalesce(rows.has_duplicate_email, false),
            nullif(rows.avatar_source_url, ''),
            rows.avatar_source_attachment_id,
            nullif(rows.avatar_source_meta_key, ''),
            nullif(rows.profile_image_url, ''),
            nullif(rows.profile_image_object_key, ''),
            nullif(rows.profile_image_content_type, ''),
            now()
        from rows
        on conflict (wp_user_id) do update
        set normalized_email = excluded.normalized_email,
            user_login = excluded.user_login,
            user_nicename = excluded.user_nicename,
            display_name = excluded.display_name,
            first_name = excluded.first_name,
            last_name = excluded.last_name,
            mobile_number = excluded.mobile_number,
            user_registered = excluded.user_registered,
            password_hash = excluded.password_hash,
            password_hash_format = excluded.password_hash_format,
            is_password_migrated = excluded.is_password_migrated,
            has_duplicate_email = excluded.has_duplicate_email,
            avatar_source_url = excluded.avatar_source_url,
            avatar_source_attachment_id = excluded.avatar_source_attachment_id,
            avatar_source_meta_key = excluded.avatar_source_meta_key,
            profile_image_url = excluded.profile_image_url,
            profile_image_object_key = excluded.profile_image_object_key,
            profile_image_content_type = excluded.profile_image_content_type,
            last_synced_at = now()
        returning 1
    )
    select count(*)::integer from upserted;
$$;

create or replace function public.import_wordpress_membership_periods(payload jsonb)
returns integer
language sql
security definer
set search_path = public, private
as $$
    with rows as (
        select *
        from jsonb_to_recordset(coalesce(payload, '[]'::jsonb)) as x(
            wp_membership_period_id bigint,
            wp_user_id bigint,
            normalized_email text,
            membership_level_id integer,
            membership_level_name text,
            tier_code text,
            code_id bigint,
            status text,
            startdate timestamptz,
            enddate timestamptz,
            modified_at timestamptz,
            initial_payment numeric(18,8),
            billing_amount numeric(18,8),
            cycle_number integer,
            cycle_period text,
            billing_limit integer,
            trial_amount numeric(18,8),
            trial_limit integer,
            raw_row jsonb
        )
    ),
    upserted as (
        insert into private.wordpress_membership_periods (
            wp_membership_period_id,
            wp_user_id,
            normalized_email,
            membership_level_id,
            membership_level_name,
            tier_code,
            code_id,
            status,
            startdate,
            enddate,
            modified_at,
            initial_payment,
            billing_amount,
            cycle_number,
            cycle_period,
            billing_limit,
            trial_amount,
            trial_limit,
            raw_row,
            last_synced_at
        )
        select
            rows.wp_membership_period_id,
            rows.wp_user_id,
            lower(nullif(btrim(rows.normalized_email), '')),
            rows.membership_level_id,
            nullif(btrim(rows.membership_level_name), ''),
            nullif(btrim(rows.tier_code), ''),
            rows.code_id,
            nullif(btrim(rows.status), ''),
            rows.startdate,
            rows.enddate,
            rows.modified_at,
            rows.initial_payment,
            rows.billing_amount,
            rows.cycle_number,
            nullif(btrim(rows.cycle_period), ''),
            rows.billing_limit,
            rows.trial_amount,
            rows.trial_limit,
            coalesce(rows.raw_row, '{}'::jsonb),
            now()
        from rows
        on conflict (wp_membership_period_id) do update
        set wp_user_id = excluded.wp_user_id,
            normalized_email = excluded.normalized_email,
            membership_level_id = excluded.membership_level_id,
            membership_level_name = excluded.membership_level_name,
            tier_code = excluded.tier_code,
            code_id = excluded.code_id,
            status = excluded.status,
            startdate = excluded.startdate,
            enddate = excluded.enddate,
            modified_at = excluded.modified_at,
            initial_payment = excluded.initial_payment,
            billing_amount = excluded.billing_amount,
            cycle_number = excluded.cycle_number,
            cycle_period = excluded.cycle_period,
            billing_limit = excluded.billing_limit,
            trial_amount = excluded.trial_amount,
            trial_limit = excluded.trial_limit,
            raw_row = excluded.raw_row,
            last_synced_at = now()
        returning 1
    )
    select count(*)::integer from upserted;
$$;

create or replace function public.import_wordpress_membership_orders(payload jsonb)
returns integer
language sql
security definer
set search_path = public, private
as $$
    with rows as (
        select *
        from jsonb_to_recordset(coalesce(payload, '[]'::jsonb)) as x(
            wp_order_id bigint,
            wp_user_id bigint,
            normalized_email text,
            membership_level_id integer,
            membership_level_name text,
            tier_code text,
            code text,
            session_id text,
            status text,
            gateway text,
            gateway_environment text,
            payment_type text,
            payment_transaction_id text,
            subscription_transaction_id text,
            billing_name text,
            billing_phone text,
            billing_country text,
            subtotal text,
            tax text,
            couponamount text,
            total text,
            order_timestamp timestamptz,
            raw_meta jsonb,
            raw_row jsonb
        )
    ),
    upserted as (
        insert into private.wordpress_membership_orders (
            wp_order_id,
            wp_user_id,
            normalized_email,
            membership_level_id,
            membership_level_name,
            tier_code,
            code,
            session_id,
            status,
            gateway,
            gateway_environment,
            payment_type,
            payment_transaction_id,
            subscription_transaction_id,
            billing_name,
            billing_phone,
            billing_country,
            subtotal,
            tax,
            couponamount,
            total,
            order_timestamp,
            raw_meta,
            raw_row,
            last_synced_at
        )
        select
            rows.wp_order_id,
            rows.wp_user_id,
            lower(nullif(btrim(rows.normalized_email), '')),
            rows.membership_level_id,
            nullif(btrim(rows.membership_level_name), ''),
            nullif(btrim(rows.tier_code), ''),
            nullif(rows.code, ''),
            nullif(rows.session_id, ''),
            nullif(btrim(rows.status), ''),
            nullif(btrim(rows.gateway), ''),
            nullif(btrim(rows.gateway_environment), ''),
            nullif(btrim(rows.payment_type), ''),
            nullif(rows.payment_transaction_id, ''),
            nullif(rows.subscription_transaction_id, ''),
            nullif(btrim(rows.billing_name), ''),
            nullif(btrim(rows.billing_phone), ''),
            nullif(btrim(rows.billing_country), ''),
            nullif(rows.subtotal, ''),
            nullif(rows.tax, ''),
            nullif(rows.couponamount, ''),
            nullif(rows.total, ''),
            rows.order_timestamp,
            coalesce(rows.raw_meta, '{}'::jsonb),
            coalesce(rows.raw_row, '{}'::jsonb),
            now()
        from rows
        on conflict (wp_order_id) do update
        set wp_user_id = excluded.wp_user_id,
            normalized_email = excluded.normalized_email,
            membership_level_id = excluded.membership_level_id,
            membership_level_name = excluded.membership_level_name,
            tier_code = excluded.tier_code,
            code = excluded.code,
            session_id = excluded.session_id,
            status = excluded.status,
            gateway = excluded.gateway,
            gateway_environment = excluded.gateway_environment,
            payment_type = excluded.payment_type,
            payment_transaction_id = excluded.payment_transaction_id,
            subscription_transaction_id = excluded.subscription_transaction_id,
            billing_name = excluded.billing_name,
            billing_phone = excluded.billing_phone,
            billing_country = excluded.billing_country,
            subtotal = excluded.subtotal,
            tax = excluded.tax,
            couponamount = excluded.couponamount,
            total = excluded.total,
            order_timestamp = excluded.order_timestamp,
            raw_meta = excluded.raw_meta,
            raw_row = excluded.raw_row,
            last_synced_at = now()
        returning 1
    )
    select count(*)::integer from upserted;
$$;

create or replace function public.import_wordpress_subscriptions(payload jsonb)
returns integer
language sql
security definer
set search_path = public, private
as $$
    with rows as (
        select *
        from jsonb_to_recordset(coalesce(payload, '[]'::jsonb)) as x(
            wp_subscription_id bigint,
            wp_user_id bigint,
            normalized_email text,
            membership_level_id integer,
            membership_level_name text,
            tier_code text,
            gateway text,
            gateway_environment text,
            subscription_transaction_id text,
            status text,
            startdate timestamptz,
            enddate timestamptz,
            next_payment_date timestamptz,
            modified_at timestamptz,
            billing_amount numeric(18,8),
            cycle_number integer,
            cycle_period text,
            billing_limit integer,
            trial_amount numeric(18,8),
            trial_limit integer,
            raw_meta jsonb,
            raw_row jsonb
        )
    ),
    upserted as (
        insert into private.wordpress_subscriptions (
            wp_subscription_id,
            wp_user_id,
            normalized_email,
            membership_level_id,
            membership_level_name,
            tier_code,
            gateway,
            gateway_environment,
            subscription_transaction_id,
            status,
            startdate,
            enddate,
            next_payment_date,
            modified_at,
            billing_amount,
            cycle_number,
            cycle_period,
            billing_limit,
            trial_amount,
            trial_limit,
            raw_meta,
            raw_row,
            last_synced_at
        )
        select
            rows.wp_subscription_id,
            rows.wp_user_id,
            lower(nullif(btrim(rows.normalized_email), '')),
            rows.membership_level_id,
            nullif(btrim(rows.membership_level_name), ''),
            nullif(btrim(rows.tier_code), ''),
            nullif(btrim(rows.gateway), ''),
            nullif(btrim(rows.gateway_environment), ''),
            nullif(rows.subscription_transaction_id, ''),
            nullif(btrim(rows.status), ''),
            rows.startdate,
            rows.enddate,
            rows.next_payment_date,
            rows.modified_at,
            rows.billing_amount,
            rows.cycle_number,
            nullif(btrim(rows.cycle_period), ''),
            rows.billing_limit,
            rows.trial_amount,
            rows.trial_limit,
            coalesce(rows.raw_meta, '{}'::jsonb),
            coalesce(rows.raw_row, '{}'::jsonb),
            now()
        from rows
        on conflict (wp_subscription_id) do update
        set wp_user_id = excluded.wp_user_id,
            normalized_email = excluded.normalized_email,
            membership_level_id = excluded.membership_level_id,
            membership_level_name = excluded.membership_level_name,
            tier_code = excluded.tier_code,
            gateway = excluded.gateway,
            gateway_environment = excluded.gateway_environment,
            subscription_transaction_id = excluded.subscription_transaction_id,
            status = excluded.status,
            startdate = excluded.startdate,
            enddate = excluded.enddate,
            next_payment_date = excluded.next_payment_date,
            modified_at = excluded.modified_at,
            billing_amount = excluded.billing_amount,
            cycle_number = excluded.cycle_number,
            cycle_period = excluded.cycle_period,
            billing_limit = excluded.billing_limit,
            trial_amount = excluded.trial_amount,
            trial_limit = excluded.trial_limit,
            raw_meta = excluded.raw_meta,
            raw_row = excluded.raw_row,
            last_synced_at = now()
        returning 1
    )
    select count(*)::integer from upserted;
$$;

create or replace function public.get_wordpress_user_for_auth(p_normalized_email text)
returns table(
    wp_user_id bigint,
    normalized_email text,
    password_hash text,
    password_hash_format text,
    first_name text,
    last_name text,
    display_name text,
    mobile_number text,
    profile_image_url text,
    profile_image_object_key text,
    profile_image_content_type text
)
language sql
security definer
set search_path = public, private
as $$
    select
        wp_user_id,
        normalized_email,
        password_hash,
        password_hash_format,
        first_name,
        last_name,
        display_name,
        mobile_number,
        profile_image_url,
        profile_image_object_key,
        profile_image_content_type
    from private.wordpress_users
    where normalized_email = lower(btrim(coalesce(p_normalized_email, '')))
    limit 1;
$$;

create or replace function public.get_wordpress_current_entitlements(p_normalized_email text)
returns table(
    wp_membership_period_id bigint,
    tier_code text,
    provider text,
    provider_payment_id text,
    provider_transaction_id text,
    provider_token text,
    subscribed_at timestamptz,
    next_renewal_at timestamptz
)
language sql
security definer
set search_path = public, private
as $$
    with active_periods as (
        select
            p.*,
            row_number() over (
                partition by p.tier_code
                order by coalesce(p.modified_at, p.startdate, p.last_synced_at) desc, p.wp_membership_period_id desc
            ) as rn
        from private.wordpress_membership_periods p
        where p.normalized_email = lower(btrim(coalesce(p_normalized_email, '')))
          and p.status = 'active'
          and p.tier_code is not null
    ),
    selected_periods as (
        select *
        from active_periods
        where rn = 1
    )
    select
        p.wp_membership_period_id,
        p.tier_code,
        coalesce(nullif(s.gateway, ''), nullif(o.gateway, ''), 'free') as provider,
        'wp-pmpro-current-' || p.wp_membership_period_id::text as provider_payment_id,
        coalesce(nullif(s.subscription_transaction_id, ''), nullif(o.payment_transaction_id, ''), nullif(o.subscription_transaction_id, '')) as provider_transaction_id,
        null::text as provider_token,
        coalesce(p.startdate, s.startdate, o.order_timestamp, now()) as subscribed_at,
        coalesce(s.next_payment_date, p.enddate) as next_renewal_at
    from selected_periods p
    left join lateral (
        select s.*
        from private.wordpress_subscriptions s
        where s.wp_user_id = p.wp_user_id
          and s.membership_level_id = p.membership_level_id
          and s.status = 'active'
        order by coalesce(s.modified_at, s.startdate, s.last_synced_at) desc, s.wp_subscription_id desc
        limit 1
    ) s on true
    left join lateral (
        select o.*
        from private.wordpress_membership_orders o
        where o.wp_user_id = p.wp_user_id
          and o.membership_level_id = p.membership_level_id
          and o.status = 'success'
        order by coalesce(o.order_timestamp, o.last_synced_at) desc, o.wp_order_id desc
        limit 1
    ) o on true;
$$;

create or replace function public.mark_wordpress_user_password_migrated(p_wp_user_id bigint)
returns void
language sql
security definer
set search_path = public, private
as $$
    update private.wordpress_users
    set password_hash = null,
        is_password_migrated = true,
        last_synced_at = now()
    where wp_user_id = p_wp_user_id;
$$;

revoke all on function public.import_wordpress_users(jsonb) from public, anon, authenticated;
revoke all on function public.import_wordpress_membership_periods(jsonb) from public, anon, authenticated;
revoke all on function public.import_wordpress_membership_orders(jsonb) from public, anon, authenticated;
revoke all on function public.import_wordpress_subscriptions(jsonb) from public, anon, authenticated;
revoke all on function public.get_wordpress_user_for_auth(text) from public, anon, authenticated;
revoke all on function public.get_wordpress_current_entitlements(text) from public, anon, authenticated;
revoke all on function public.mark_wordpress_user_password_migrated(bigint) from public, anon, authenticated;

grant execute on function public.import_wordpress_users(jsonb) to service_role;
grant execute on function public.import_wordpress_membership_periods(jsonb) to service_role;
grant execute on function public.import_wordpress_membership_orders(jsonb) to service_role;
grant execute on function public.import_wordpress_subscriptions(jsonb) to service_role;
grant execute on function public.get_wordpress_user_for_auth(text) to service_role;
grant execute on function public.get_wordpress_current_entitlements(text) to service_role;
grant execute on function public.mark_wordpress_user_password_migrated(bigint) to service_role;
