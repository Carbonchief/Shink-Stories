-- Add the "Most Favourited" system playlist and keep it refreshed from favourites data.

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
values (
    'most-favourited-stories',
    'Stories met die meeste gunstelinge',
    'Stories wat die meeste as gunsteling gemerk is op Schink Stories.',
    43,
    10,
    true,
    false,
    '/branding/Storie_Hoekie_Logo_Banner.png',
    '/branding/Storie_Hoekie_Logo_Banner_Backdrop.png',
    'system',
    'most-favourited-stories'
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

create or replace function public.refresh_most_favourited_stories_playlist()
returns integer
language plpgsql
set search_path = pg_catalog, public
as $$
declare
    v_playlist_id uuid;
    v_max_items integer;
    v_rows_inserted integer := 0;
begin
    select
        playlist_id,
        max_items
    into
        v_playlist_id,
        v_max_items
    from public.story_playlists
    where
        system_key = 'most-favourited-stories'
        and playlist_type = 'system'
    limit 1;

    if v_playlist_id is null then
        raise exception 'Story playlist with system key % was not found.', 'most-favourited-stories'
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
    favourite_events as (
        select
            f.subscriber_id,
            f.story_slug,
            coalesce(f.updated_at, f.created_at) as favourited_at
        from public.story_favorites as f

        union all

        select
            f.subscriber_id,
            f.story_slug,
            f.created_at as favourited_at
        from public.story_favourites as f
    ),
    favourite_stats as (
        select
            f.story_slug,
            count(distinct f.subscriber_id)::integer as total_favourites,
            max(f.favourited_at) as latest_favourited_at
        from favourite_events as f
        where coalesce(nullif(btrim(f.story_slug), ''), '') <> ''
        group by f.story_slug
    ),
    ranked as (
        select
            e.story_id,
            row_number() over (
                order by
                    favourite_stats.total_favourites desc,
                    favourite_stats.latest_favourited_at desc nulls last,
                    e.published_at desc nulls last,
                    e.sort_order asc,
                    e.title asc
            ) as calculated_sort_order
        from eligible_stories as e
        join favourite_stats
            on favourite_stats.story_slug = e.slug
        where favourite_stats.total_favourites > 0
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

-- Refresh every hour to keep the favourites ranking reasonably current.
select cron.schedule(
    'refresh-most-favourited-stories-playlist',
    '15 * * * *',
    $$select public.refresh_most_favourited_stories_playlist();$$
);

select public.refresh_most_favourited_stories_playlist();
