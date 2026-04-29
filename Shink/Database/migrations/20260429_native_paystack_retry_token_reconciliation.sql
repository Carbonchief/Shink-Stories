create or replace function public.reconcile_native_paystack_retry_tokens()
returns table(updated_subscription_id uuid)
language sql
security definer
set search_path = public
as $$
    with candidate_pairs as (
        select
            native.subscription_id as native_subscription_id,
            native.provider_transaction_id as native_provider_transaction_id,
            native.provider_token as native_provider_token,
            native.provider_email_token as native_provider_email_token,
            wp.provider_transaction_id as wp_provider_transaction_id,
            wp.provider_token as wp_provider_token,
            wp.provider_email_token as wp_provider_email_token,
            count(*) over (partition by native.subscription_id) as matches_per_native,
            count(*) over (partition by wp.subscription_id) as matches_per_wp
        from public.subscriptions native
        join public.subscriptions wp
          on wp.subscriber_id = native.subscriber_id
         and wp.provider = native.provider
        where native.source_system = 'shink_app'
          and wp.source_system = 'wordpress_pmpro'
          and native.provider = 'paystack'
          and (
                (native.provider_payment_id is not null and wp.provider_transaction_id = native.provider_payment_id)
             or (native.provider_transaction_id is not null and wp.provider_transaction_id = native.provider_transaction_id)
             or (native.provider_payment_id is not null and wp.provider_payment_id = native.provider_payment_id)
          )
    ),
    safe_pairs as (
        select *
        from candidate_pairs
        where matches_per_native = 1
          and matches_per_wp = 1
          and (
                (nullif(trim(native_provider_token), '') is null and nullif(trim(wp_provider_token), '') is not null)
             or (nullif(trim(native_provider_email_token), '') is null and nullif(trim(wp_provider_email_token), '') is not null)
          )
    ),
    updated_rows as (
        update public.subscriptions native
           set provider_token = coalesce(native.provider_token, safe_pairs.wp_provider_token),
               provider_email_token = coalesce(native.provider_email_token, safe_pairs.wp_provider_email_token),
               updated_at = timezone('utc', now())
          from safe_pairs
         where native.subscription_id = safe_pairs.native_subscription_id
        returning native.subscription_id
    )
    select updated_rows.subscription_id as updated_subscription_id
    from updated_rows;
$$;
