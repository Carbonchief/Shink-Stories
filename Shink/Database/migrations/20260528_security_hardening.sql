create or replace function public.redeem_signup_discount_code(
    p_email text,
    p_code text,
    p_selected_tier_code text default null
)
returns jsonb
language plpgsql
security definer
set search_path = pg_catalog, public
as $$
declare
    normalized_email text := lower(btrim(coalesce(p_email, '')));
    normalized_input_code text := lower(btrim(coalesce(p_code, '')));
    normalized_selected_tier text := lower(nullif(btrim(coalesce(p_selected_tier_code, '')), ''));
    now_utc timestamptz := now();
    bypass_enabled boolean;
    code_row public.subscription_discount_codes%rowtype;
    mapping_row public.subscription_discount_code_tiers%rowtype;
    mapping_count integer;
    non_gratis_mapping_count integer;
    redemption_count integer;
    email_redemption_count integer;
    subscriber_uuid uuid;
    subscription_uuid uuid;
    access_expires_at timestamptz;
begin
    if position('@' in normalized_email) <= 1 then
        return jsonb_build_object('success', false, 'message', 'Gebruik asseblief ''n geldige e-posadres.');
    end if;

    if normalized_input_code = '' then
        return jsonb_build_object('success', false, 'message', 'Voer asseblief ''n geldige kode in.');
    end if;

    select coalesce(
        (
            select case
                when lower(setting_value::text) in ('false', '"false"') then false
                else true
            end
            from public.site_settings
            where setting_key = 'subscription_code_signup_bypass_enabled'
            limit 1
        ),
        true
    )
    into bypass_enabled;

    if not bypass_enabled then
        return jsonb_build_object('success', false, 'message', 'Kodebetalings is tans afgeskakel.');
    end if;

    select *
    into code_row
    from public.subscription_discount_codes
    where subscription_discount_codes.normalized_code = normalized_input_code
    for update;

    if code_row.discount_code_id is null then
        return jsonb_build_object('success', false, 'message', 'Daardie kode bestaan nie of is nie beskikbaar nie.');
    end if;

    if code_row.discount_kind <> 'free_access' then
        return jsonb_build_object('success', false, 'message', 'Hierdie kode gee afslag op Paystack en kan nie gratis toegang aktiveer nie.');
    end if;

    if not code_row.is_active then
        return jsonb_build_object('success', false, 'message', 'Hierdie kode is nie aktief nie.');
    end if;

    if code_row.starts_at is not null and code_row.starts_at > now_utc then
        return jsonb_build_object('success', false, 'message', 'Hierdie kode is nog nie beskikbaar nie.');
    end if;

    if code_row.expires_at is not null and code_row.expires_at < now_utc then
        return jsonb_build_object('success', false, 'message', 'Hierdie kode het verval.');
    end if;

    if not code_row.bypass_payment then
        return jsonb_build_object('success', false, 'message', 'Hierdie kode kan nie nou betaling omseil nie.');
    end if;

    select count(*)
    into redemption_count
    from public.subscription_discount_code_redemptions
    where discount_code_id = code_row.discount_code_id;

    if code_row.max_uses > 0 and redemption_count >= code_row.max_uses then
        return jsonb_build_object('success', false, 'message', 'Hierdie kode het sy maksimum gebruike bereik.');
    end if;

    select count(*)
    into email_redemption_count
    from public.subscription_discount_code_redemptions
    where discount_code_id = code_row.discount_code_id
      and lower(email) = normalized_email;

    if code_row.one_use_per_user and email_redemption_count > 0 then
        return jsonb_build_object('success', false, 'message', 'Hierdie kode is reeds vir hierdie e-pos gebruik.');
    end if;

    select count(*),
           count(*) filter (where tier_code <> 'gratis')
    into mapping_count, non_gratis_mapping_count
    from public.subscription_discount_code_tiers
    where discount_code_id = code_row.discount_code_id;

    if mapping_count = 0 then
        return jsonb_build_object('success', false, 'message', 'Hierdie kode het nie ''n intekeningsoort nie.');
    end if;

    if normalized_selected_tier is not null then
        select *
        into mapping_row
        from public.subscription_discount_code_tiers
        where discount_code_id = code_row.discount_code_id
          and tier_code = normalized_selected_tier
        limit 1;
    elsif non_gratis_mapping_count = 1 then
        select *
        into mapping_row
        from public.subscription_discount_code_tiers
        where discount_code_id = code_row.discount_code_id
          and tier_code <> 'gratis'
        limit 1;
    elsif mapping_count = 1 then
        select *
        into mapping_row
        from public.subscription_discount_code_tiers
        where discount_code_id = code_row.discount_code_id
        limit 1;
    end if;

    if mapping_row.discount_code_tier_id is null then
        return jsonb_build_object('success', false, 'message', 'Kies asseblief ''n geldige intekeningsoort vir hierdie kode.');
    end if;

    if mapping_row.expiration_number is not null and mapping_row.expiration_number > 0 then
        access_expires_at := case lower(coalesce(mapping_row.expiration_period, ''))
            when 'hour' then now_utc + make_interval(hours => mapping_row.expiration_number)
            when 'hours' then now_utc + make_interval(hours => mapping_row.expiration_number)
            when 'day' then now_utc + make_interval(days => mapping_row.expiration_number)
            when 'days' then now_utc + make_interval(days => mapping_row.expiration_number)
            when 'week' then now_utc + make_interval(days => mapping_row.expiration_number * 7)
            when 'weeks' then now_utc + make_interval(days => mapping_row.expiration_number * 7)
            when 'month' then now_utc + make_interval(months => mapping_row.expiration_number)
            when 'months' then now_utc + make_interval(months => mapping_row.expiration_number)
            when 'year' then now_utc + make_interval(years => mapping_row.expiration_number)
            when 'years' then now_utc + make_interval(years => mapping_row.expiration_number)
            else null
        end;
    end if;

    select subscriber_id
    into subscriber_uuid
    from public.subscribers
    where lower(email) = normalized_email
    limit 1;

    if subscriber_uuid is null then
        return jsonb_build_object('success', false, 'message', 'Kon nie jou intekenaarprofiel vind nie.');
    end if;

    if mapping_row.tier_code <> 'gratis' then
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
        values (
            subscriber_uuid,
            mapping_row.tier_code,
            'free',
            'discount-code-' || replace(gen_random_uuid()::text, '-', ''),
            null,
            null,
            'active',
            now_utc,
            access_expires_at,
            null,
            'discount_code'
        )
        returning subscription_id into subscription_uuid;
    end if;

    insert into public.subscription_discount_code_redemptions (
        discount_code_id,
        subscriber_id,
        email,
        tier_code,
        redeemed_at,
        access_expires_at,
        granted_subscription_id,
        source_system,
        bypassed_payment
    )
    values (
        code_row.discount_code_id,
        subscriber_uuid,
        normalized_email,
        mapping_row.tier_code,
        now_utc,
        access_expires_at,
        subscription_uuid,
        'shink_app',
        true
    );

    return jsonb_build_object(
        'success', true,
        'message', null,
        'tier_code', mapping_row.tier_code,
        'access_expires_at', access_expires_at,
        'subscription_id', subscription_uuid
    );
end;
$$;

revoke all on function public.redeem_signup_discount_code(text, text, text) from public, anon, authenticated;
grant execute on function public.redeem_signup_discount_code(text, text, text) to service_role;

revoke all on function public.get_admin_analytics_snapshot() from public, anon, authenticated;
revoke all on function public.get_admin_analytics_snapshot_base() from public, anon, authenticated;
grant execute on function public.get_admin_analytics_snapshot() to service_role;
grant execute on function public.get_admin_analytics_snapshot_base() to service_role;
