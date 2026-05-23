-- Add the order-only system row that controls where the Speellyste block appears on /luister.

drop trigger if exists trg_story_playlists_protect_system_updates on public.story_playlists;

insert into public.story_playlists (
    slug,
    title,
    playlist_type,
    system_key,
    description,
    sort_order,
    max_items,
    is_enabled,
    show_on_home,
    include_in_speellyste_carousel,
    show_showcase_image_on_luister_page
)
values (
    'speellyste',
    'Speellyste',
    'system',
    'speellyste',
    'Die Speellyste blok op die Luister blad.',
    25,
    null,
    true,
    false,
    false,
    false
)
on conflict (system_key)
where system_key is not null
do update set
    slug = excluded.slug,
    title = excluded.title,
    playlist_type = excluded.playlist_type,
    description = excluded.description,
    sort_order = excluded.sort_order,
    max_items = excluded.max_items,
    is_enabled = excluded.is_enabled,
    show_on_home = excluded.show_on_home,
    include_in_speellyste_carousel = excluded.include_in_speellyste_carousel,
    show_showcase_image_on_luister_page = excluded.show_showcase_image_on_luister_page;

create or replace function public.protect_system_playlist_updates()
returns trigger
language plpgsql
set search_path = pg_catalog, public
as $$
begin
    if old.playlist_type = 'system'
       and coalesce(old.system_key, '') = 'speellyste'
       and (
           new.slug is distinct from old.slug or
           new.title is distinct from old.title or
           new.description is distinct from old.description or
           new.max_items is distinct from old.max_items or
           new.logo_image_path is distinct from old.logo_image_path or
           new.backdrop_image_path is distinct from old.backdrop_image_path or
           new.showcase_image_path is distinct from old.showcase_image_path or
           new.playlist_type is distinct from old.playlist_type or
           coalesce(new.system_key, '') is distinct from coalesce(old.system_key, '') or
           new.is_enabled is distinct from old.is_enabled or
           new.show_on_home is distinct from old.show_on_home or
           new.include_in_speellyste_carousel is distinct from old.include_in_speellyste_carousel or
           new.show_showcase_image_on_luister_page is distinct from old.show_showcase_image_on_luister_page
       ) then
        raise exception 'Speellyste system playlist can only change sort order.'
            using errcode = '42501';
    end if;

    if old.playlist_type = 'system'
       and coalesce(old.system_key, '') <> 'speellyste'
       and (
           new.slug is distinct from old.slug or
           new.title is distinct from old.title or
           new.description is distinct from old.description or
           new.max_items is distinct from old.max_items or
           new.logo_image_path is distinct from old.logo_image_path or
           new.backdrop_image_path is distinct from old.backdrop_image_path or
           new.playlist_type is distinct from old.playlist_type or
           coalesce(new.system_key, '') is distinct from coalesce(old.system_key, '')
       ) then
        raise exception 'System playlists can only change active/home/order flags.'
            using errcode = '42501';
    end if;

    return new;
end;
$$;

create trigger trg_story_playlists_protect_system_updates
before update on public.story_playlists
for each row execute function public.protect_system_playlist_updates();
