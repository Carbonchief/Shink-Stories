alter table public.story_playlists
    add column if not exists speellyste_featured_playlist_id uuid
        references public.story_playlists (playlist_id) on delete set null
        constraint story_playlists_speellyste_featured_playlist_check
        check (
            speellyste_featured_playlist_id is null
            or (
                playlist_type = 'system'
                and system_key is not distinct from 'speellyste'
                and speellyste_featured_playlist_id <> playlist_id
            )
        );

comment on column public.story_playlists.speellyste_featured_playlist_id is
    'Playlist featured above the Speellyste carousel on /luister when it is visible in the carousel.';
