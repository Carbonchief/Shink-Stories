-- Keep the "Nuwe Stories" system playlist in sync when stories change.

create or replace function public.refresh_nuwe_stories_playlist_on_story_change()
returns trigger
language plpgsql
set search_path = pg_catalog, public
as $$
begin
    perform public.refresh_nuwe_stories_playlist();
    return null;
end;
$$;

drop trigger if exists trg_refresh_nuwe_stories_playlist_after_insert on public.stories;
create trigger trg_refresh_nuwe_stories_playlist_after_insert
after insert on public.stories
for each statement
execute function public.refresh_nuwe_stories_playlist_on_story_change();

drop trigger if exists trg_refresh_nuwe_stories_playlist_after_delete on public.stories;
create trigger trg_refresh_nuwe_stories_playlist_after_delete
after delete on public.stories
for each statement
execute function public.refresh_nuwe_stories_playlist_on_story_change();

drop trigger if exists trg_refresh_nuwe_stories_playlist_after_update on public.stories;
create trigger trg_refresh_nuwe_stories_playlist_after_update
after update of
    status,
    published_at,
    access_level,
    audio_object_key,
    duration_seconds,
    metadata,
    sort_order,
    title
on public.stories
for each statement
execute function public.refresh_nuwe_stories_playlist_on_story_change();
