alter table public.story_playlist_items
add column if not exists is_showcase boolean not null default false;

comment on column public.story_playlist_items.is_showcase is
    'Marks the single showcase story for a manual playlist.';

update public.story_playlist_items
set is_showcase = false;

with ranked_manual_items as (
    select
        items.playlist_id,
        items.story_id,
        row_number() over (
            partition by items.playlist_id
            order by items.sort_order asc, items.created_at asc, items.story_id asc
        ) as story_position
    from public.story_playlist_items as items
    join public.story_playlists as playlists
        on playlists.playlist_id = items.playlist_id
    where playlists.playlist_type = 'manual'
)
update public.story_playlist_items as items
set is_showcase = true
from ranked_manual_items as ranked
where
    items.playlist_id = ranked.playlist_id
    and items.story_id = ranked.story_id
    and ranked.story_position = 1;

create unique index if not exists ux_story_playlist_items_showcase_per_playlist
    on public.story_playlist_items(playlist_id)
    where is_showcase;

create or replace function public.validate_story_playlist_item_showcase()
returns trigger
language plpgsql
set search_path = pg_catalog, public
as $$
declare
    v_playlist_type text;
begin
    if not new.is_showcase then
        return new;
    end if;

    select playlist_type
    into v_playlist_type
    from public.story_playlists
    where playlist_id = new.playlist_id
    limit 1;

    if v_playlist_type = 'system' then
        raise exception 'System playlists cannot have a showcase story.'
            using errcode = '42501';
    end if;

    return new;
end;
$$;

drop trigger if exists trg_story_playlist_items_validate_showcase on public.story_playlist_items;
create trigger trg_story_playlist_items_validate_showcase
before insert or update on public.story_playlist_items
for each row execute function public.validate_story_playlist_item_showcase();
