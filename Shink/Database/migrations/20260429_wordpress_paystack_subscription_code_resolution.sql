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
        case
            when coalesce(nullif(s.gateway, ''), nullif(o.gateway, ''), 'free') = 'paystack'
                then coalesce(
                    case when nullif(s.subscription_transaction_id, '') like 'SUB_%' then nullif(s.subscription_transaction_id, '') end,
                    case when nullif(o.subscription_transaction_id, '') like 'SUB_%' then nullif(o.subscription_transaction_id, '') end,
                    nullif(o.payment_transaction_id, ''),
                    nullif(s.subscription_transaction_id, ''),
                    nullif(o.subscription_transaction_id, '')
                )
            else coalesce(nullif(s.subscription_transaction_id, ''), nullif(o.payment_transaction_id, ''), nullif(o.subscription_transaction_id, ''))
        end as provider_transaction_id,
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

revoke all on function public.get_wordpress_current_entitlements(text) from public, anon, authenticated;
grant execute on function public.get_wordpress_current_entitlements(text) to service_role;
