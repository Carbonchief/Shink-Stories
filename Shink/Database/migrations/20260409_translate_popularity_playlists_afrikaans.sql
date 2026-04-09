-- Translate the popularity system playlist titles to Afrikaans.
-- System-playlist metadata updates are protected, so temporarily remove the
-- update trigger while these title changes are applied.
drop trigger if exists trg_story_playlists_protect_system_updates on public.story_playlists;

update public.story_playlists
set title = 'Gewilde stories hierdie week'
where system_key = 'popular-stories-this-week'
  and coalesce(nullif(btrim(title), ''), '') <> 'Gewilde stories hierdie week';

update public.story_playlists
set title = 'Gewildste stories'
where system_key = 'most-popular-stories'
  and coalesce(nullif(btrim(title), ''), '') <> 'Gewildste stories';

create trigger trg_story_playlists_protect_system_updates
before update on public.story_playlists
for each row
execute function public.protect_system_playlist_updates();
