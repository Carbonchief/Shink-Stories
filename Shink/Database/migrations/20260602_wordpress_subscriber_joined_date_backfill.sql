create or replace function public.import_wordpress_subscribers(payload jsonb)
returns table(subscriber_id uuid, email text)
language sql
security definer
set search_path = public
as $$
    insert into public.subscribers (
        email,
        first_name,
        last_name,
        display_name,
        mobile_number,
        created_at,
        last_login_at,
        profile_image_url,
        profile_image_object_key,
        profile_image_content_type
    )
    select
        lower(trim(rows.email)) as email,
        nullif(trim(rows.first_name), '') as first_name,
        nullif(trim(rows.last_name), '') as last_name,
        nullif(trim(rows.display_name), '') as display_name,
        nullif(trim(rows.mobile_number), '') as mobile_number,
        coalesce(rows.user_registered, now()) as created_at,
        rows.last_login_at,
        nullif(trim(rows.profile_image_url), '') as profile_image_url,
        nullif(trim(rows.profile_image_object_key), '') as profile_image_object_key,
        nullif(trim(rows.profile_image_content_type), '') as profile_image_content_type
    from jsonb_to_recordset(coalesce(payload, '[]'::jsonb)) as rows(
        email text,
        first_name text,
        last_name text,
        display_name text,
        mobile_number text,
        user_registered timestamptz,
        last_login_at timestamptz,
        profile_image_url text,
        profile_image_object_key text,
        profile_image_content_type text
    )
    where nullif(trim(rows.email), '') is not null
    on conflict (email) do update
    set first_name = excluded.first_name,
        last_name = excluded.last_name,
        display_name = excluded.display_name,
        mobile_number = excluded.mobile_number,
        created_at = least(subscribers.created_at, excluded.created_at),
        last_login_at = case
            when excluded.last_login_at is null then subscribers.last_login_at
            when subscribers.last_login_at is null then excluded.last_login_at
            else greatest(subscribers.last_login_at, excluded.last_login_at)
        end,
        profile_image_url = excluded.profile_image_url,
        profile_image_object_key = excluded.profile_image_object_key,
        profile_image_content_type = excluded.profile_image_content_type,
        updated_at = now()
    returning subscribers.subscriber_id, subscribers.email;
$$;

drop function if exists public.get_wordpress_user_for_auth(text);

create function public.get_wordpress_user_for_auth(p_normalized_email text)
returns table(
    wp_user_id bigint,
    normalized_email text,
    password_hash text,
    password_hash_format text,
    first_name text,
    last_name text,
    display_name text,
    mobile_number text,
    user_registered timestamptz,
    last_login_at timestamptz,
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
        user_registered,
        last_login_at,
        profile_image_url,
        profile_image_object_key,
        profile_image_content_type
    from private.wordpress_users
    where normalized_email = lower(btrim(coalesce(p_normalized_email, '')))
    limit 1;
$$;

with candidate_joined_dates as (
    select
        evidence.normalized_email,
        min(evidence.joined_at) as joined_at
    from (
        select
            lower(btrim(normalized_email)) as normalized_email,
            user_registered as joined_at
        from private.wordpress_users
        where user_registered is not null

        union all

        select
            lower(btrim(normalized_email)) as normalized_email,
            startdate as joined_at
        from private.wordpress_membership_periods
        where startdate is not null

        union all

        select
            lower(btrim(normalized_email)) as normalized_email,
            startdate as joined_at
        from private.wordpress_subscriptions
        where startdate is not null

        union all

        select
            lower(btrim(normalized_email)) as normalized_email,
            order_timestamp as joined_at
        from private.wordpress_membership_orders
        where order_timestamp is not null
          and lower(coalesce(status, '')) = 'success'
    ) evidence
    where nullif(evidence.normalized_email, '') is not null
      and evidence.joined_at is not null
    group by evidence.normalized_email
)
update public.subscribers subscriber
set created_at = candidate.joined_at
from candidate_joined_dates candidate
where lower(btrim(subscriber.email)) = candidate.normalized_email
  and candidate.joined_at < subscriber.created_at;

revoke all on function public.import_wordpress_subscribers(jsonb) from public, anon, authenticated;
revoke all on function public.get_wordpress_user_for_auth(text) from public, anon, authenticated;

grant execute on function public.import_wordpress_subscribers(jsonb) to service_role;
grant execute on function public.get_wordpress_user_for_auth(text) to service_role;
