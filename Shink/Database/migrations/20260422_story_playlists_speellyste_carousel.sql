alter table public.story_playlists
add column if not exists showcase_image_path text;

alter table public.story_playlists
add column if not exists include_in_speellyste_carousel boolean not null default false;

comment on column public.story_playlists.showcase_image_path is
    'Optional card image shown for the playlist in the Speellyste carousel on /luister.';

comment on column public.story_playlists.include_in_speellyste_carousel is
    'When true, the playlist appears in the Speellyste carousel on /luister.';

create index if not exists idx_story_playlists_speellyste_carousel
    on public.story_playlists(include_in_speellyste_carousel, is_enabled, sort_order, title);
