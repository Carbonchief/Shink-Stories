create or replace function public.import_wordpress_current_subscriptions(payload jsonb)
returns table(subscription_id uuid)
language sql
security definer
set search_path = public
as $$
    insert into public.subscriptions (
        subscriber_id,
        tier_code,
        provider,
        provider_payment_id,
        provider_transaction_id,
        provider_token,
        provider_email_token,
        status,
        subscribed_at,
        next_renewal_at,
        cancelled_at,
        billing_amount_zar,
        billing_period_months,
        billing_amount_source,
        source_system
    )
    select
        rows.subscriber_id,
        rows.tier_code,
        rows.provider,
        rows.provider_payment_id,
        rows.provider_transaction_id,
        rows.provider_token,
        rows.provider_email_token,
        rows.status,
        coalesce(rows.subscribed_at, now()),
        rows.next_renewal_at,
        rows.cancelled_at,
        case when rows.billing_amount_zar > 0 then round(rows.billing_amount_zar, 2) else null end,
        rows.billing_period_months,
        case when rows.billing_amount_zar > 0 then coalesce(nullif(rows.billing_amount_source, ''), 'wordpress_import') else null end,
        coalesce(rows.source_system, 'wordpress_pmpro')
    from jsonb_to_recordset(coalesce(payload, '[]'::jsonb)) as rows(
        subscriber_id uuid,
        tier_code text,
        provider text,
        provider_payment_id text,
        provider_transaction_id text,
        provider_token text,
        provider_email_token text,
        status text,
        subscribed_at timestamptz,
        next_renewal_at timestamptz,
        cancelled_at timestamptz,
        billing_amount_zar numeric,
        billing_period_months integer,
        billing_amount_source text,
        source_system text
    )
    where rows.subscriber_id is not null
      and nullif(trim(rows.tier_code), '') is not null
      and nullif(trim(rows.provider), '') is not null
      and nullif(trim(rows.provider_payment_id), '') is not null
    on conflict (provider, provider_payment_id) do update
    set subscriber_id = excluded.subscriber_id,
        tier_code = excluded.tier_code,
        provider_transaction_id = coalesce(excluded.provider_transaction_id, subscriptions.provider_transaction_id),
        provider_token = coalesce(excluded.provider_token, subscriptions.provider_token),
        provider_email_token = coalesce(excluded.provider_email_token, subscriptions.provider_email_token),
        status = excluded.status,
        subscribed_at = excluded.subscribed_at,
        next_renewal_at = excluded.next_renewal_at,
        cancelled_at = excluded.cancelled_at,
        billing_amount_zar = case
            when excluded.billing_amount_zar is not null and excluded.billing_amount_zar > 0
                then excluded.billing_amount_zar
            else subscriptions.billing_amount_zar
        end,
        billing_period_months = coalesce(excluded.billing_period_months, subscriptions.billing_period_months),
        billing_amount_source = case
            when excluded.billing_amount_zar is not null and excluded.billing_amount_zar > 0
                then coalesce(excluded.billing_amount_source, 'wordpress_import')
            else subscriptions.billing_amount_source
        end,
        source_system = excluded.source_system,
        updated_at = now()
    returning subscriptions.subscription_id;
$$;

drop function if exists public.get_wordpress_current_entitlements(text);

create function public.get_wordpress_current_entitlements(p_normalized_email text)
returns table(
    wp_membership_period_id bigint,
    tier_code text,
    provider text,
    provider_payment_id text,
    provider_transaction_id text,
    provider_token text,
    subscribed_at timestamptz,
    next_renewal_at timestamptz,
    billing_amount_zar numeric,
    billing_period_months integer
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
        coalesce(s.next_payment_date, p.enddate) as next_renewal_at,
        round(coalesce(
            case when s.billing_amount > 0 then s.billing_amount end,
            case when p.billing_amount > 0 then p.billing_amount end,
            case when nullif(o.total, '') ~ '^[0-9]+(\.[0-9]+)?$' then nullif(o.total, '')::numeric end
        ), 2) as billing_amount_zar,
        coalesce(
            case
                when coalesce(s.cycle_number, p.cycle_number) > 0
                 and lower(coalesce(s.cycle_period, p.cycle_period, '')) in ('month', 'months')
                    then coalesce(s.cycle_number, p.cycle_number)
                when coalesce(s.cycle_number, p.cycle_number) > 0
                 and lower(coalesce(s.cycle_period, p.cycle_period, '')) in ('year', 'years')
                    then coalesce(s.cycle_number, p.cycle_number) * 12
            end,
            tier.billing_period_months
        ) as billing_period_months
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
    ) o on true
    left join public.subscription_tiers tier
        on tier.tier_code = p.tier_code;
$$;

with wordpress_billing as (
    select distinct on (p.wp_membership_period_id)
        'wp-pmpro-current-' || p.wp_membership_period_id::text as provider_payment_id,
        nullif(s.subscription_transaction_id, '') as provider_transaction_id,
        round(coalesce(
            case when s.billing_amount > 0 then s.billing_amount end,
            case when p.billing_amount > 0 then p.billing_amount end
        ), 2) as billing_amount_zar,
        coalesce(
            case
                when coalesce(s.cycle_number, p.cycle_number) > 0
                 and lower(coalesce(s.cycle_period, p.cycle_period, '')) in ('month', 'months')
                    then coalesce(s.cycle_number, p.cycle_number)
                when coalesce(s.cycle_number, p.cycle_number) > 0
                 and lower(coalesce(s.cycle_period, p.cycle_period, '')) in ('year', 'years')
                    then coalesce(s.cycle_number, p.cycle_number) * 12
            end,
            tier.billing_period_months
        ) as billing_period_months
    from private.wordpress_membership_periods p
    left join lateral (
        select s.*
        from private.wordpress_subscriptions s
        where s.wp_user_id = p.wp_user_id
          and s.membership_level_id = p.membership_level_id
          and lower(coalesce(s.status, '')) = 'active'
        order by coalesce(s.modified_at, s.startdate, s.last_synced_at) desc, s.wp_subscription_id desc
        limit 1
    ) s on true
    left join public.subscription_tiers tier
        on tier.tier_code = p.tier_code
    where p.tier_code is not null
      and coalesce(s.billing_amount, p.billing_amount) > 0
    order by p.wp_membership_period_id, coalesce(s.modified_at, p.modified_at, p.startdate, p.last_synced_at) desc
)
update public.subscriptions sub
set billing_amount_zar = wordpress_billing.billing_amount_zar,
    billing_period_months = coalesce(sub.billing_period_months, wordpress_billing.billing_period_months),
    billing_amount_source = 'wordpress_import',
    updated_at = now()
from wordpress_billing
where sub.provider = 'paystack'
  and coalesce(sub.billing_amount_zar, 0) = 0
  and wordpress_billing.billing_amount_zar > 0
  and (
      sub.provider_payment_id = wordpress_billing.provider_payment_id
      or (
          wordpress_billing.provider_transaction_id is not null
          and (
              sub.provider_payment_id = wordpress_billing.provider_transaction_id
              or sub.provider_transaction_id = wordpress_billing.provider_transaction_id
          )
      )
  );

revoke all on function public.import_wordpress_current_subscriptions(jsonb) from public, anon, authenticated;
revoke all on function public.get_wordpress_current_entitlements(text) from public, anon, authenticated;

grant execute on function public.import_wordpress_current_subscriptions(jsonb) to service_role;
grant execute on function public.get_wordpress_current_entitlements(text) to service_role;
