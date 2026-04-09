create extension if not exists pg_cron with schema pg_catalog;

grant usage on schema cron to postgres;
grant all privileges on all tables in schema cron to postgres;

drop trigger if exists trg_story_playlists_protect_system_updates on public.story_playlists;

insert into public.story_playlists (
    slug,
    title,
    description,
    sort_order,
    max_items,
    is_enabled,
    show_on_home,
    logo_image_path,
    backdrop_image_path,
    playlist_type,
    system_key
)
values
    (
        'popular-stories-this-week',
        'Popular Stories this week',
        'Stories wat hierdie week die gewildste op Schink Stories is.',
        41,
        10,
        true,
        false,
        '/branding/Storie_Hoekie_Logo_Banner.png',
        '/branding/Storie_Hoekie_Logo_Banner_Backdrop.png',
        'system',
        'popular-stories-this-week'
    ),
    (
        'most-popular-stories',
        'Most Popular Stories',
        'Stories wat oor tyd die gewildste op Schink Stories is.',
        42,
        10,
        true,
        false,
        '/branding/Storie_Hoekie_Logo_Banner.png',
        '/branding/Storie_Hoekie_Logo_Banner_Backdrop.png',
        'system',
        'most-popular-stories'
    )
on conflict (slug) do update
set
    title = excluded.title,
    description = excluded.description,
    sort_order = excluded.sort_order,
    max_items = excluded.max_items,
    is_enabled = excluded.is_enabled,
    show_on_home = excluded.show_on_home,
    logo_image_path = excluded.logo_image_path,
    backdrop_image_path = excluded.backdrop_image_path,
    playlist_type = excluded.playlist_type,
    system_key = excluded.system_key,
    updated_at = now();

create trigger trg_story_playlists_protect_system_updates
before update on public.story_playlists
for each row
execute function public.protect_system_playlist_updates();

create or replace function public.refresh_story_popularity_playlist(
    p_system_key text,
    p_cutoff timestamptz default '-infinity'::timestamptz
)
returns integer
language plpgsql
set search_path = pg_catalog, public
as $$
declare
    v_playlist_id uuid;
    v_max_items integer;
    v_rows_inserted integer := 0;
    v_normalized_system_key text := lower(btrim(coalesce(p_system_key, '')));
    v_effective_cutoff timestamptz := coalesce(p_cutoff, '-infinity'::timestamptz);
begin
    if v_normalized_system_key = '' then
        raise exception 'System playlist key is required.'
            using errcode = '22023';
    end if;

    select
        playlist_id,
        max_items
    into
        v_playlist_id,
        v_max_items
    from public.story_playlists
    where
        system_key = v_normalized_system_key
        and playlist_type = 'system'
    limit 1;

    if v_playlist_id is null then
        raise exception 'Story playlist with system key % was not found.', v_normalized_system_key
            using errcode = 'P0002';
    end if;

    delete from public.story_playlist_items
    where playlist_id = v_playlist_id;

    with eligible_stories as (
        select
            s.story_id,
            s.slug,
            s.title,
            s.sort_order,
            s.published_at
        from public.stories as s
        where
            s.status = 'published'
            and s.published_at <= now()
            and s.access_level in ('free', 'subscriber')
            and coalesce(nullif(btrim(s.audio_object_key), ''), '') <> ''
            and not (
                (s.duration_seconds is not null and s.duration_seconds <= 60)
                or coalesce(s.audio_object_key, '') ilike 'imported/soundbites/%'
                or coalesce(s.audio_object_key, '') ilike 'imported/non-story-audio/%'
                or coalesce(s.metadata::text, '') ilike '%soundbite%'
            )
    ),
    view_stats as (
        select
            v.story_slug,
            count(*)::integer as total_views,
            count(distinct v.subscriber_id)::integer as unique_viewers
        from public.story_views as v
        where v.viewed_at >= v_effective_cutoff
        group by v.story_slug
    ),
    listen_stats as (
        select
            l.story_slug,
            count(*)::integer as total_listen_events,
            count(distinct l.subscriber_id)::integer as unique_listeners,
            coalesce(sum(l.listened_seconds), 0)::numeric(12, 3) as listened_seconds,
            count(*) filter (
                where
                    l.event_type = 'ended'
                    or case
                        when jsonb_typeof(l.metadata -> 'is_completed') = 'boolean'
                            then (l.metadata ->> 'is_completed')::boolean
                        else false
                    end
            )::integer as completion_count
        from public.story_listen_events as l
        where l.occurred_at >= v_effective_cutoff
        group by l.story_slug
    ),
    ranked as (
        select
            e.story_id,
            row_number() over (
                order by
                    (
                        coalesce(v.unique_viewers, 0)::numeric +
                        (coalesce(l.unique_listeners, 0)::numeric * 2) +
                        (coalesce(l.completion_count, 0)::numeric * 3) +
                        (coalesce(l.listened_seconds, 0) / 60.0)
                    ) desc,
                    coalesce(l.listened_seconds, 0) desc,
                    coalesce(l.completion_count, 0) desc,
                    coalesce(l.unique_listeners, 0) desc,
                    coalesce(v.unique_viewers, 0) desc,
                    e.published_at desc nulls last,
                    e.sort_order asc,
                    e.title asc
            ) as calculated_sort_order
        from eligible_stories as e
        left join view_stats as v
            on v.story_slug = e.slug
        left join listen_stats as l
            on l.story_slug = e.slug
        where
            coalesce(v.total_views, 0) > 0
            or coalesce(l.total_listen_events, 0) > 0
    )
    insert into public.story_playlist_items (playlist_id, story_id, sort_order)
    select
        v_playlist_id,
        ranked.story_id,
        ranked.calculated_sort_order
    from ranked
    where v_max_items is null or ranked.calculated_sort_order <= v_max_items
    order by ranked.calculated_sort_order;

    get diagnostics v_rows_inserted = row_count;
    return v_rows_inserted;
end;
$$;

create or replace function public.refresh_popular_stories_this_week_playlist()
returns integer
language sql
set search_path = pg_catalog, public
as $$
    select public.refresh_story_popularity_playlist(
        p_system_key := 'popular-stories-this-week',
        p_cutoff := now() - interval '7 days'
    );
$$;

create or replace function public.refresh_most_popular_stories_playlist()
returns integer
language sql
set search_path = pg_catalog, public
as $$
    select public.refresh_story_popularity_playlist(
        p_system_key := 'most-popular-stories',
        p_cutoff := '-infinity'::timestamptz
    );
$$;

-- Supabase Cron uses the database timezone, which is UTC by default.
-- 22:10 UTC = 00:10 Africa/Johannesburg every night.
select cron.schedule(
    'refresh-popular-stories-this-week-playlist',
    '10 22 * * *',
    $$select public.refresh_popular_stories_this_week_playlist();$$
);

-- 21:30 UTC Sunday = 23:30 Africa/Johannesburg Sunday night.
select cron.schedule(
    'refresh-most-popular-stories-playlist',
    '30 21 * * 0',
    $$select public.refresh_most_popular_stories_playlist();$$
);

select public.refresh_popular_stories_this_week_playlist();
select public.refresh_most_popular_stories_playlist();
