-- Clear playlist showcase artwork for every playlist except Bybelstories and Storie Hoekie.
-- System playlists are protected by a trigger, so temporarily drop and recreate it
-- around the data update to keep the existing guard in place afterward.

drop trigger if exists trg_story_playlists_protect_system_updates on public.story_playlists;

update public.story_playlists
set
    logo_image_path = '',
    backdrop_image_path = ''
where lower(slug) not in ('bybelstories', 'storie-hoekie');

create trigger trg_story_playlists_protect_system_updates
before update on public.story_playlists
for each row
execute function public.protect_system_playlist_updates();
