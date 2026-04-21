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
        status,
        subscribed_at,
        next_renewal_at,
        cancelled_at,
        source_system
    )
    select
        rows.subscriber_id,
        rows.tier_code,
        rows.provider,
        rows.provider_payment_id,
        rows.provider_transaction_id,
        rows.provider_token,
        rows.status,
        coalesce(rows.subscribed_at, now()),
        rows.next_renewal_at,
        rows.cancelled_at,
        coalesce(rows.source_system, 'wordpress_pmpro')
    from jsonb_to_recordset(coalesce(payload, '[]'::jsonb)) as rows(
        subscriber_id uuid,
        tier_code text,
        provider text,
        provider_payment_id text,
        provider_transaction_id text,
        provider_token text,
        status text,
        subscribed_at timestamptz,
        next_renewal_at timestamptz,
        cancelled_at timestamptz,
        source_system text
    )
    where rows.subscriber_id is not null
      and nullif(trim(rows.tier_code), '') is not null
      and nullif(trim(rows.provider), '') is not null
      and nullif(trim(rows.provider_payment_id), '') is not null
    on conflict (provider, provider_payment_id) do update
    set subscriber_id = excluded.subscriber_id,
        tier_code = excluded.tier_code,
        provider_transaction_id = excluded.provider_transaction_id,
        provider_token = excluded.provider_token,
        status = excluded.status,
        subscribed_at = excluded.subscribed_at,
        next_renewal_at = excluded.next_renewal_at,
        cancelled_at = excluded.cancelled_at,
        source_system = excluded.source_system,
        updated_at = now()
    returning subscriptions.subscription_id;
$$;

revoke all on function public.import_wordpress_current_subscriptions(jsonb) from public;
revoke all on function public.import_wordpress_current_subscriptions(jsonb) from anon;
revoke all on function public.import_wordpress_current_subscriptions(jsonb) from authenticated;
grant execute on function public.import_wordpress_current_subscriptions(jsonb) to service_role;
