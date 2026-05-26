alter table if exists public.story_playlists
    add column if not exists accent_color_hex text;

alter table if exists public.story_playlists
    drop constraint if exists story_playlists_accent_color_hex_check;

alter table if exists public.story_playlists
    add constraint story_playlists_accent_color_hex_check
    check (
        accent_color_hex is null
        or accent_color_hex ~ '^#[0-9A-Fa-f]{6}$'
    );
