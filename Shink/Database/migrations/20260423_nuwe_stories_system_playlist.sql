-- Add the "Nuwe Stories" system playlist and keep it refreshed from newest published stories.

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
    'nuwe-stories',
    'Nuwe Stories',
    'Die nuutste stories op Schink Stories.',
    21,
    10,
    true,
    false,
    '/branding/Storie_Hoekie_Logo_Banner.png',
    '/branding/Storie_Hoekie_Logo_Banner_Backdrop.png',
    'system',
    'nuwe-stories'
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

create or replace function public.refresh_nuwe_stories_playlist()
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
        system_key = 'nuwe-stories'
        and playlist_type = 'system'
    limit 1;

    if v_playlist_id is null then
        raise exception 'Story playlist with system key % was not found.', 'nuwe-stories'
            using errcode = 'P0002';
    end if;

    delete from public.story_playlist_items
    where playlist_id = v_playlist_id;

    with eligible_stories as (
        select
            s.story_id,
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
    ranked as (
        select
            e.story_id,
            row_number() over (
                order by
                    e.published_at desc nulls last,
                    e.sort_order asc,
                    e.title asc
            ) as calculated_sort_order
        from eligible_stories as e
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

-- Refresh hourly so newly published stories show up quickly.
select cron.schedule(
    'refresh-nuwe-stories-playlist',
    '5 * * * *',
    $$select public.refresh_nuwe_stories_playlist();$$
);

select public.refresh_nuwe_stories_playlist();
