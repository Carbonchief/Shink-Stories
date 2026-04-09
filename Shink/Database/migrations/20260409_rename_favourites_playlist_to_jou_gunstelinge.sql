-- Rename the system Favourites playlist to "Jou Gunstelinge".
-- The system-playlist update trigger blocks title edits by default, so we
-- temporarily remove it for this migration and restore it immediately after.
drop trigger if exists trg_story_playlists_protect_system_updates on public.story_playlists;

update public.story_playlists
set title = 'Jou Gunstelinge'
where system_key = 'favourites'
  and coalesce(nullif(btrim(title), ''), '') <> 'Jou Gunstelinge';

create trigger trg_story_playlists_protect_system_updates
before update on public.story_playlists
for each row
execute function public.protect_system_playlist_updates();
