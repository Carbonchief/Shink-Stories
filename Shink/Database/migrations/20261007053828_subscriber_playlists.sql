-- Personal playlists are private and accessed through authenticated Blazor server
-- services. RPCs accept the server session's email; only service_role may call them.
create table public.subscriber_playlists (
    playlist_id uuid primary key default gen_random_uuid(),
    subscriber_id uuid not null references public.subscribers(subscriber_id) on delete cascade,
    title text not null check (char_length(btrim(title)) between 1 and 80),
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now()
);
create index idx_subscriber_playlists_owner on public.subscriber_playlists(subscriber_id, updated_at desc);

create table public.subscriber_playlist_items (
    playlist_id uuid not null references public.subscriber_playlists(playlist_id) on delete cascade,
    story_slug text not null references public.stories(slug) on delete cascade on update cascade,
    sort_order integer not null check (sort_order between 0 and 99),
    primary key (playlist_id, story_slug),
    unique (playlist_id, sort_order)
);
create index idx_subscriber_playlist_items_story on public.subscriber_playlist_items(story_slug);

alter table public.subscriber_playlists enable row level security;
alter table public.subscriber_playlist_items enable row level security;
revoke all on public.subscriber_playlists, public.subscriber_playlist_items from public, anon, authenticated;
grant select, insert, update, delete on public.subscriber_playlists, public.subscriber_playlist_items to service_role;

create trigger trg_subscriber_playlists_updated_at
before update on public.subscriber_playlists
for each row execute function public.set_updated_at();

create function public.get_subscriber_playlists(p_email text, p_playlist_id uuid default null)
returns table (playlist_id uuid, title text, story_slugs text[])
language sql stable security invoker set search_path = pg_catalog
as $$
    select p.playlist_id, p.title,
        coalesce((select array_agg(i.story_slug order by i.sort_order)
            from public.subscriber_playlist_items i where i.playlist_id = p.playlist_id), '{}'::text[])
    from public.subscriber_playlists p
    join public.subscribers s on s.subscriber_id = p.subscriber_id
    where s.email = lower(btrim(p_email))
      and (p_playlist_id is null or p.playlist_id = p_playlist_id)
    order by p.updated_at desc, p.playlist_id
    limit 50;
$$;

create function public.save_subscriber_playlist(p_email text, p_playlist_id uuid, p_title text, p_story_slugs text[])
returns table (playlist_id uuid, title text, story_slugs text[])
language plpgsql security invoker set search_path = pg_catalog
as $$
declare
    owner_id uuid;
    saved_id uuid;
    normalized_email text := lower(btrim(coalesce(p_email, '')));
    normalized_title text := btrim(coalesce(p_title, ''));
    slugs text[] := coalesce(p_story_slugs, '{}'::text[]);
begin
    if normalized_email = '' or position('@' in normalized_email) <= 1 then
        raise exception 'Invalid account' using errcode = '22023';
    end if;
    if char_length(normalized_title) not between 1 and 80 or cardinality(slugs) > 100 then
        raise exception 'Invalid playlist name or size' using errcode = '22023';
    end if;
    if exists (select 1 from unnest(slugs) slug
        where slug is null or btrim(slug) = '' or slug <> lower(btrim(slug)))
       or (select count(distinct slug) from unnest(slugs) slug) <> cardinality(slugs) then
        raise exception 'Invalid or duplicate story' using errcode = '22023';
    end if;
    if exists (select 1 from unnest(slugs) as requested(story_slug) where not exists (
        select 1 from public.stories s where s.slug = requested.story_slug and s.status = 'published'
        and s.deleted_at is null and coalesce(s.story_type, 'story') <> 'video')) then
        raise exception 'Story unavailable' using errcode = '22023';
    end if;

    insert into public.subscribers (email) values (normalized_email) on conflict (email) do nothing;
    -- Serialize saves for one account so concurrent creates cannot exceed its limit.
    select s.subscriber_id into owner_id from public.subscribers s
        where s.email = normalized_email for update;

    if p_playlist_id is null then
        if (select count(*) from public.subscriber_playlists p where p.subscriber_id = owner_id) >= 50 then
            raise exception 'Playlist limit reached' using errcode = '22023';
        end if;
        insert into public.subscriber_playlists (subscriber_id, title) values (owner_id, normalized_title)
            returning subscriber_playlists.playlist_id into saved_id;
    else
        update public.subscriber_playlists p set title = normalized_title
            where p.playlist_id = p_playlist_id and p.subscriber_id = owner_id
            returning p.playlist_id into saved_id;
        if saved_id is null then
            raise exception 'Playlist unavailable' using errcode = '22023';
        end if;
    end if;

    -- Replace title and ordered items in one transaction; failures preserve the old list.
    delete from public.subscriber_playlist_items i where i.playlist_id = saved_id;
    insert into public.subscriber_playlist_items (playlist_id, story_slug, sort_order)
        select saved_id, slug, (position - 1)::integer from unnest(slugs) with ordinality as items(slug, position);
    return query select saved_id, normalized_title, slugs;
end;
$$;

create function public.delete_subscriber_playlist(p_email text, p_playlist_id uuid)
returns boolean language plpgsql security invoker set search_path = pg_catalog
as $$
declare deleted_id uuid;
begin
    delete from public.subscriber_playlists p using public.subscribers s
    where p.playlist_id = p_playlist_id and s.subscriber_id = p.subscriber_id
      and s.email = lower(btrim(p_email))
    returning p.playlist_id into deleted_id;
    return deleted_id is not null;
end;
$$;

revoke all on function public.get_subscriber_playlists(text, uuid) from public, anon, authenticated;
revoke all on function public.save_subscriber_playlist(text, uuid, text, text[]) from public, anon, authenticated;
revoke all on function public.delete_subscriber_playlist(text, uuid) from public, anon, authenticated;
grant execute on function public.get_subscriber_playlists(text, uuid) to service_role;
grant execute on function public.save_subscriber_playlist(text, uuid, text, text[]) to service_role;
grant execute on function public.delete_subscriber_playlist(text, uuid) to service_role;

-- Keep personal playlists covered by the existing account deletion flow.
create or replace function public.delete_account_personal_data(p_email text)
returns jsonb
language plpgsql
security invoker
set search_path = pg_catalog
as $$
declare
    requested_email text := lower(btrim(coalesce(p_email, '')));
    subscriber_uuid uuid;
    deleted_email text;
    avatar_object_key text;
begin
    if requested_email = '' or position('@' in requested_email) <= 1 then
        return jsonb_build_object(
            'deleted', false,
            'message', 'Kon nie jou rekening se e-posadres lees nie.'
        );
    end if;

    select
        subscriber_id,
        profile_image_object_key
    into
        subscriber_uuid,
        avatar_object_key
    from public.subscribers
    where lower(email) = requested_email
    limit 1
    for update;

    if subscriber_uuid is null then
        delete from public.auth_sessions
        where lower(email) = requested_email;

        return jsonb_build_object(
            'deleted', true,
            'profile_image_object_key', null
        );
    end if;

    deleted_email := 'deleted+' || replace(subscriber_uuid::text, '-', '') || '@example.invalid';

    delete from public.story_views where subscriber_id = subscriber_uuid;
    delete from public.story_listen_events where subscriber_id = subscriber_uuid;
    delete from public.subscriber_playlists where subscriber_id = subscriber_uuid;
    delete from public.story_favorites where subscriber_id = subscriber_uuid;
    delete from public.story_favourites where subscriber_id = subscriber_uuid;
    delete from public.subscriber_notifications where subscriber_id = subscriber_uuid;
    delete from public.character_audio_plays where subscriber_id = subscriber_uuid;
    delete from public.subscriber_character_unlock_states where subscriber_id = subscriber_uuid;
    delete from public.subscriber_admin_audit where subscriber_id = subscriber_uuid;
    delete from public.subscription_cancellation_feedback where subscriber_id = subscriber_uuid;
    delete from public.subscription_payment_pauses where subscriber_id = subscriber_uuid;
    delete from public.subscription_plan_changes where subscriber_id = subscriber_uuid;

    update public.resource_document_download_events
    set subscriber_id = null
    where subscriber_id = subscriber_uuid;

    update public.blog_visit_events
    set subscriber_id = null
    where subscriber_id = subscriber_uuid;

    update public.oortjies_click_events
    set subscriber_id = null
    where subscriber_id = subscriber_uuid;

    update public.subscription_discount_code_redemptions
    set
        subscriber_id = null,
        email = deleted_email,
        metadata = null,
        updated_at = now()
    where subscriber_id = subscriber_uuid
       or lower(email) = requested_email;

    update public.subscription_events
    set payload = '{}'::jsonb
    where subscription_id in (
        select subscription_id
        from public.subscriptions
        where subscriber_id = subscriber_uuid
    );

    update public.subscriptions
    set
        status = 'cancelled',
        cancelled_at = coalesce(cancelled_at, now()),
        provider_token = null,
        provider_email_token = null,
        updated_at = now()
    where subscriber_id = subscriber_uuid;

    delete from public.auth_sessions
    where lower(email) = requested_email;

    delete from public.abandoned_cart_recoveries
    where lower(customer_email) = requested_email;

    delete from public.paystack_checkout_sessions
    where lower(customer_email) = requested_email;

    update public.app_error_logs
    set
        user_email = null,
        metadata = metadata - 'email' - 'user_email'
    where lower(user_email) = requested_email;

    update public.store_orders
    set
        customer_name = 'Deleted customer',
        customer_email = deleted_email,
        customer_phone = '',
        delivery_address_line_1 = 'Removed after account deletion',
        delivery_address_line_2 = null,
        delivery_suburb = null,
        delivery_city = 'Removed',
        delivery_postal_code = '0000',
        notes = null,
        raw_verify_response = null,
        raw_webhook_payload = null,
        updated_at = now()
    where lower(customer_email) = requested_email;

    delete from public.school_seats
    where lower(email) = requested_email;

    update public.school_seats
    set invited_by_email = deleted_email
    where lower(invited_by_email) = requested_email;

    update public.school_accounts
    set
        admin_email = deleted_email,
        status = 'cancelled',
        updated_at = now()
    where lower(admin_email) = requested_email;

    delete from private.wordpress_membership_periods
    where lower(wordpress_membership_periods.normalized_email) = requested_email;

    delete from private.wordpress_membership_orders
    where lower(wordpress_membership_orders.normalized_email) = requested_email;

    delete from private.wordpress_subscriptions
    where lower(wordpress_subscriptions.normalized_email) = requested_email;

    delete from private.wordpress_users
    where lower(wordpress_users.normalized_email) = requested_email;

    update public.subscribers
    set
        email = deleted_email,
        first_name = null,
        last_name = null,
        display_name = null,
        mobile_number = null,
        profile_image_url = null,
        profile_image_object_key = null,
        profile_image_content_type = null,
        last_login_at = null,
        disabled_at = now(),
        disabled_by_admin_email = 'self_service_deletion',
        disabled_reason = 'Persoonlike data deur gebruiker verwyder.',
        updated_at = now()
    where subscriber_id = subscriber_uuid;

    return jsonb_build_object(
        'deleted', true,
        'profile_image_object_key', avatar_object_key
    );
end;
$$;

grant delete on table
    private.wordpress_membership_periods,
    private.wordpress_membership_orders,
    private.wordpress_subscriptions,
    private.wordpress_users
to service_role;

revoke all on function public.delete_account_personal_data(text) from public, anon, authenticated;
grant execute on function public.delete_account_personal_data(text) to service_role;
