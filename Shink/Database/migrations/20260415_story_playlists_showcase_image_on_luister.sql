alter table public.story_playlists
add column if not exists show_showcase_image_on_luister_page boolean not null default false;

comment on column public.story_playlists.show_showcase_image_on_luister_page is
    'When true, the playlist showcases its selected story image above the carousel on /luister.';
