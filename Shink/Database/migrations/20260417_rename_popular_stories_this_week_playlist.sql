-- Rename the weekly popularity system playlist to the shorter Afrikaans title.
-- System playlist metadata is protected, so temporarily remove the trigger.
drop trigger if exists trg_story_playlists_protect_system_updates on public.story_playlists;

update public.story_playlists
set title = 'Gewild dié week'
where system_key = 'popular-stories-this-week'
  and coalesce(nullif(btrim(title), ''), '') <> 'Gewild dié week';

create trigger trg_story_playlists_protect_system_updates
before update on public.story_playlists
for each row
execute function public.protect_system_playlist_updates();
