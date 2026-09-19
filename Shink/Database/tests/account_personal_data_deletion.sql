-- Run through the deployment connection after changing account deletion.
-- All fixtures and cleanup are rolled back, including on any assertion failure.
begin;
set local statement_timeout = '30s';

do $$
declare
    fixture_id uuid := gen_random_uuid();
    fixture_email text := 'account-deletion-check+' || fixture_id::text || '@example.invalid';
    unrelated_email text := 'account-deletion-keep+' || fixture_id::text || '@example.invalid';
    legacy_id bigint := -floor(random() * 1000000000000000 + 1)::bigint;
    fixture_avatar text := 'account-deletion-check/' || fixture_id::text || '.webp';
    result jsonb;
    legacy_table text;
    remaining bigint;
begin
    if (select prosecdef from pg_proc where oid = 'public.delete_account_personal_data(text)'::regprocedure) then
        raise exception 'Account cleanup must retain SECURITY INVOKER';
    end if;
    if has_function_privilege('anon', 'public.delete_account_personal_data(text)', 'EXECUTE')
       or has_function_privilege('authenticated', 'public.delete_account_personal_data(text)', 'EXECUTE') then
        raise exception 'Account cleanup must remain restricted to the server';
    end if;

    insert into public.subscribers (subscriber_id, email, first_name, profile_image_object_key)
    values (fixture_id, fixture_email, 'Deletion verification', fixture_avatar);

    insert into private.wordpress_users (wp_user_id, normalized_email, user_login, user_nicename)
    values (legacy_id, fixture_email, 'deletion-check', 'deletion-check'),
           (legacy_id - 1, unrelated_email, 'deletion-keep', 'deletion-keep');
    insert into private.wordpress_membership_periods (wp_membership_period_id, wp_user_id, normalized_email)
    values (legacy_id, legacy_id, fixture_email), (legacy_id - 1, legacy_id - 1, unrelated_email);
    insert into private.wordpress_membership_orders (wp_order_id, wp_user_id, normalized_email)
    values (legacy_id, legacy_id, fixture_email), (legacy_id - 1, legacy_id - 1, unrelated_email);
    insert into private.wordpress_subscriptions (wp_subscription_id, wp_user_id, normalized_email)
    values (legacy_id, legacy_id, fixture_email), (legacy_id - 1, legacy_id - 1, unrelated_email);

    -- Use the same restricted database role as the application's REST request.
    set local role service_role;
    result := public.delete_account_personal_data(upper(fixture_email));
    if result->>'deleted' is distinct from 'true'
       or result->>'profile_image_object_key' is distinct from fixture_avatar then
        raise exception 'Account cleanup did not return success and the avatar cleanup key';
    end if;
    result := public.delete_account_personal_data(fixture_email);
    if result->>'deleted' is distinct from 'true' then
        raise exception 'A repeated account cleanup must succeed';
    end if;
    reset role;

    if not exists (
        select 1 from public.subscribers
        where subscriber_id = fixture_id
          and email = 'deleted+' || replace(fixture_id::text, '-', '') || '@example.invalid'
          and first_name is null and profile_image_object_key is null
          and disabled_at is not null
    ) then
        raise exception 'Personal subscriber data was not anonymized';
    end if;

    foreach legacy_table in array array[
        'wordpress_membership_periods', 'wordpress_membership_orders',
        'wordpress_subscriptions', 'wordpress_users'
    ] loop
        execute format('select count(*) from private.%I where normalized_email = $1', legacy_table)
            into remaining using fixture_email;
        if remaining <> 0 then
            raise exception 'Matching personal data remains in %', legacy_table;
        end if;
        execute format('select count(*) from private.%I where normalized_email = $1', legacy_table)
            into remaining using unrelated_email;
        if remaining <> 1 then
            raise exception 'Unrelated records changed in %', legacy_table;
        end if;
    end loop;
end;
$$;

rollback;
select 'Account cleanup, retry, isolation and access checks passed; fixtures rolled back.' as verification;
