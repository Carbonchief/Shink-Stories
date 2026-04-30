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
                order by
                    case
                        when lower(coalesce(p.status, '')) = 'active' then 0
                        else 1
                    end,
                    coalesce(p.modified_at, p.startdate, p.enddate, p.last_synced_at) desc,
                    p.wp_membership_period_id desc
            ) as rn
        from private.wordpress_membership_periods p
        where p.normalized_email = lower(btrim(coalesce(p_normalized_email, '')))
          and p.tier_code is not null
          and (
              lower(coalesce(p.status, '')) = 'active'
              or (
                  p.enddate is not null
                  and p.enddate > now()
                  and (
                      p.code_id is not null
                      or exists (
                          select 1
                          from private.wordpress_membership_orders o
                          where o.wp_user_id = p.wp_user_id
                            and o.membership_level_id = p.membership_level_id
                            and nullif(btrim(o.code), '') is not null
                      )
                  )
              )
          )
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
          and lower(coalesce(s.status, '')) = 'active'
        order by coalesce(s.modified_at, s.startdate, s.last_synced_at) desc, s.wp_subscription_id desc
        limit 1
    ) s on true
    left join lateral (
        select o.*
        from private.wordpress_membership_orders o
        where o.wp_user_id = p.wp_user_id
          and o.membership_level_id = p.membership_level_id
          and lower(coalesce(o.status, '')) = 'success'
        order by coalesce(o.order_timestamp, o.last_synced_at) desc, o.wp_order_id desc
        limit 1
    ) o on true;
$$;

create or replace function public.reactivate_wordpress_current_subscriptions(payload jsonb)
returns table(subscription_id uuid)
language sql
security definer
set search_path = public
as $$
    with rows as (
        select distinct on (provider, provider_payment_id, source_system)
            lower(btrim(provider)) as provider,
            btrim(provider_payment_id) as provider_payment_id,
            coalesce(next_renewal_at, null) as next_renewal_at,
            coalesce(nullif(btrim(source_system), ''), 'wordpress_pmpro') as source_system
        from jsonb_to_recordset(coalesce(payload, '[]'::jsonb)) as rows(
            provider text,
            provider_payment_id text,
            next_renewal_at timestamptz,
            source_system text
        )
        where nullif(btrim(provider), '') is not null
          and nullif(btrim(provider_payment_id), '') is not null
        order by provider, provider_payment_id, source_system
    ),
    updated as (
        update public.subscriptions sub
        set status = 'active',
            cancelled_at = null,
            next_renewal_at = coalesce(rows.next_renewal_at, sub.next_renewal_at),
            updated_at = now()
        from rows
        where lower(coalesce(sub.provider, '')) = rows.provider
          and sub.provider_payment_id = rows.provider_payment_id
          and coalesce(sub.source_system, 'wordpress_pmpro') = rows.source_system
          and lower(coalesce(sub.status, '')) = 'cancelled'
        returning sub.subscription_id
    )
    select subscription_id
    from updated;
$$;

revoke all on function public.get_wordpress_current_entitlements(text) from public, anon, authenticated;
revoke all on function public.reactivate_wordpress_current_subscriptions(jsonb) from public, anon, authenticated;

grant execute on function public.get_wordpress_current_entitlements(text) to service_role;
grant execute on function public.reactivate_wordpress_current_subscriptions(jsonb) to service_role;
