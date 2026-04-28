alter table public.subscribers
    add column if not exists last_login_at timestamptz;

create index if not exists idx_subscribers_last_login_at
    on public.subscribers(last_login_at desc);

alter table private.wordpress_users
    add column if not exists last_login_at timestamptz;

create index if not exists idx_wordpress_users_last_login_at
    on private.wordpress_users(last_login_at desc);

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
            last_login_at timestamptz,
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
            last_login_at,
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
            rows.last_login_at,
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
            last_login_at = excluded.last_login_at,
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
        last_login_at,
        profile_image_url,
        profile_image_object_key,
        profile_image_content_type
    from private.wordpress_users
    where normalized_email = lower(btrim(coalesce(p_normalized_email, '')))
    limit 1;
$$;

revoke all on function public.import_wordpress_users(jsonb) from public, anon, authenticated;
revoke all on function public.import_wordpress_subscribers(jsonb) from public, anon, authenticated;
revoke all on function public.get_wordpress_user_for_auth(text) from public, anon, authenticated;

grant execute on function public.import_wordpress_users(jsonb) to service_role;
grant execute on function public.import_wordpress_subscribers(jsonb) to service_role;
grant execute on function public.get_wordpress_user_for_auth(text) to service_role;
