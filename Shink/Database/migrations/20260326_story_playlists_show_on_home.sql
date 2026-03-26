alter table public.story_playlists
add column if not exists show_on_home boolean not null default false;

comment on column public.story_playlists.show_on_home is 'When true, the playlist is shown on the home page carousel section.';

create index if not exists idx_story_playlists_home_sort
    on public.story_playlists(show_on_home, is_enabled, sort_order, title);

update public.story_playlists
set
    show_on_home = case
        when slug = 'gratis-stories' then true
        else false
    end,
    is_enabled = case
        when slug = 'gratis-stories' then true
        else is_enabled
    end;
