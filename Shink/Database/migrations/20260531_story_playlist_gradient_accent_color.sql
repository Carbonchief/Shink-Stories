alter table if exists public.story_playlists
    add column if not exists accent_color_end_hex text;

alter table if exists public.story_playlists
    drop constraint if exists story_playlists_accent_color_end_hex_check;

alter table if exists public.story_playlists
    add constraint story_playlists_accent_color_end_hex_check
    check (
        accent_color_end_hex is null
        or accent_color_end_hex ~ '^#[0-9A-Fa-f]{6}$'
    );

comment on column public.story_playlists.accent_color_end_hex is
    'Optional second hex color for playlist showcase gradient backgrounds.';
