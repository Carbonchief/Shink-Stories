-- Add video as a persisted story type and keep it out of the same
-- automatically populated story playlist that excludes music.

alter table public.stories
    drop constraint if exists stories_story_type_check_video;

alter table public.stories
    add constraint stories_story_type_check_video
    check (story_type in ('story', 'music', 'video')) not valid;

alter table public.stories
    validate constraint stories_story_type_check_video;

alter table public.stories
    drop constraint if exists stories_story_type_check;

alter table public.stories
    rename constraint stories_story_type_check_video to stories_story_type_check;

comment on column public.stories.story_type is
    'Content type for Luister entries: story, music, or video.';

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
            and coalesce(s.story_type, 'story') not in ('music', 'video')
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

select public.refresh_nuwe_stories_playlist();
