alter table if exists public.story_playlists
    add column if not exists font_color_hex text;

alter table if exists public.story_playlists
    drop constraint if exists story_playlists_font_color_hex_check;

alter table if exists public.story_playlists
    add constraint story_playlists_font_color_hex_check
    check (
        font_color_hex is null
        or font_color_hex ~ '^#[0-9A-Fa-f]{6}$'
    );

comment on column public.story_playlists.font_color_hex is
    'Optional hex font color used on playlist showcase pages.';
