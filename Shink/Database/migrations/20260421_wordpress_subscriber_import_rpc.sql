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
        nullif(trim(rows.profile_image_url), '') as profile_image_url,
        nullif(trim(rows.profile_image_object_key), '') as profile_image_object_key,
        nullif(trim(rows.profile_image_content_type), '') as profile_image_content_type
    from jsonb_to_recordset(coalesce(payload, '[]'::jsonb)) as rows(
        email text,
        first_name text,
        last_name text,
        display_name text,
        mobile_number text,
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
        profile_image_url = excluded.profile_image_url,
        profile_image_object_key = excluded.profile_image_object_key,
        profile_image_content_type = excluded.profile_image_content_type,
        updated_at = now()
    returning subscribers.subscriber_id, subscribers.email;
$$;

revoke all on function public.import_wordpress_subscribers(jsonb) from public;
revoke all on function public.import_wordpress_subscribers(jsonb) from anon;
revoke all on function public.import_wordpress_subscribers(jsonb) from authenticated;
grant execute on function public.import_wordpress_subscribers(jsonb) to service_role;
