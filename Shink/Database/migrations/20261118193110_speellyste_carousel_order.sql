alter table public.story_playlists
    add column if not exists speellyste_carousel_order uuid[] not null default '{}'
        constraint story_playlists_speellyste_carousel_order_check
        check (
            cardinality(speellyste_carousel_order) = 0
            or (playlist_type = 'system' and system_key is not distinct from 'speellyste')
        ),
    add column if not exists speellyste_square_playlist_ids uuid[] not null default '{}'
        constraint story_playlists_speellyste_square_playlist_ids_check
        check (
            cardinality(speellyste_square_playlist_ids) = 0
            or (playlist_type = 'system' and system_key is not distinct from 'speellyste')
        );

comment on column public.story_playlists.speellyste_carousel_order is
    'Playlist IDs in display order inside the Speellyste carousel. An empty array follows the standard playlist order.';

comment on column public.story_playlists.speellyste_square_playlist_ids is
    'Playlists with square cover crops in Speellyste on /luister. Other playlists display the full cover.';
