-- Add playlist branding visuals for showcase splash screens.
-- Defaults point to the Storie Hoekie assets in /wwwroot/branding.

alter table public.story_playlists
add column if not exists logo_image_path text;

alter table public.story_playlists
add column if not exists backdrop_image_path text;

alter table public.story_playlists
alter column logo_image_path set default '/branding/Storie_Hoekie_Logo_Banner.png';

alter table public.story_playlists
alter column backdrop_image_path set default '/branding/Storie_Hoekie_Logo_Banner_Backdrop.png';

update public.story_playlists
set
    logo_image_path = coalesce(nullif(btrim(logo_image_path), ''), '/branding/Storie_Hoekie_Logo_Banner.png'),
    backdrop_image_path = coalesce(nullif(btrim(backdrop_image_path), ''), '/branding/Storie_Hoekie_Logo_Banner_Backdrop.png')
where coalesce(nullif(btrim(logo_image_path), ''), '') = ''
   or coalesce(nullif(btrim(backdrop_image_path), ''), '') = '';

alter table public.story_playlists
alter column logo_image_path set not null;

alter table public.story_playlists
alter column backdrop_image_path set not null;

comment on column public.story_playlists.logo_image_path is 'Logo image shown in the playlist showcase splash screen.';
comment on column public.story_playlists.backdrop_image_path is 'Backdrop image shown behind the logo in the playlist showcase splash screen.';
