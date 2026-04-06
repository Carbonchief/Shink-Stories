-- Add system-playlist support and the personalized Favourites system playlist.

alter table public.story_playlists
add column if not exists playlist_type text;

alter table public.story_playlists
add column if not exists system_key text;

update public.story_playlists
set playlist_type = 'manual'
where coalesce(nullif(btrim(playlist_type), ''), '') = '';

update public.story_playlists
set system_key = null
where playlist_type = 'manual'
  and coalesce(nullif(btrim(system_key), ''), '') <> '';

alter table public.story_playlists
alter column playlist_type set default 'manual';

alter table public.story_playlists
alter column playlist_type set not null;

do $$
begin
    if not exists (
        select 1
        from pg_constraint
        where conname = 'story_playlists_playlist_type_check'
    ) then
        alter table public.story_playlists
        add constraint story_playlists_playlist_type_check
            check (playlist_type in ('manual', 'system'));
    end if;
end
$$;

do $$
begin
    if not exists (
        select 1
        from pg_constraint
        where conname = 'story_playlists_system_key_required_check'
    ) then
        alter table public.story_playlists
        add constraint story_playlists_system_key_required_check
            check (
                (playlist_type = 'manual' and system_key is null) or
                (
                    playlist_type = 'system' and
                    system_key is not null and
                    btrim(system_key) <> '' and
                    system_key ~ '^[a-z0-9]+(?:-[a-z0-9]+)*$'
                )
            );
    end if;
end
$$;

comment on column public.story_playlists.playlist_type is 'manual for admin-managed playlists, system for generated playlists.';
comment on column public.story_playlists.system_key is 'Stable identifier for system playlists (for example favourites).';

insert into public.story_playlists (
    slug,
    title,
    description,
    sort_order,
    max_items,
    is_enabled,
    show_on_home,
    logo_image_path,
    backdrop_image_path,
    playlist_type,
    system_key
)
values (
    'favourites',
    'Favourites',
    'Stories wat jy as gunsteling gemerk het.',
    5,
    null,
    true,
    false,
    '/branding/Storie_Hoekie_Logo_Banner.png',
    '/branding/Storie_Hoekie_Logo_Banner_Backdrop.png',
    'system',
    'favourites'
)
on conflict (slug) do update
set
    playlist_type = 'system',
    system_key = 'favourites',
    updated_at = now();

create unique index if not exists ux_story_playlists_system_key
    on public.story_playlists(system_key)
    where system_key is not null;

create or replace function public.prevent_system_playlist_delete()
returns trigger
language plpgsql
set search_path = pg_catalog, public
as $$
begin
    if old.playlist_type = 'system' then
        raise exception 'System playlists cannot be deleted.'
            using errcode = '42501';
    end if;

    return old;
end;
$$;

drop trigger if exists trg_story_playlists_prevent_system_delete on public.story_playlists;
create trigger trg_story_playlists_prevent_system_delete
before delete on public.story_playlists
for each row execute function public.prevent_system_playlist_delete();

create or replace function public.protect_system_playlist_updates()
returns trigger
language plpgsql
set search_path = pg_catalog, public
as $$
begin
    if old.playlist_type = 'system' and (
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

drop trigger if exists trg_story_playlists_protect_system_updates on public.story_playlists;
create trigger trg_story_playlists_protect_system_updates
before update on public.story_playlists
for each row execute function public.protect_system_playlist_updates();

create table if not exists public.story_favourites (
    subscriber_id uuid not null references public.subscribers(subscriber_id) on delete cascade,
    story_slug text not null references public.stories(slug) on delete cascade,
    created_at timestamptz not null default now(),
    primary key (subscriber_id, story_slug)
);

comment on table public.story_favourites is 'Stories explicitly favourited by a subscriber.';
comment on column public.story_favourites.story_slug is 'Slug of the favourited story.';

create index if not exists idx_story_favourites_subscriber_created_at
    on public.story_favourites(subscriber_id, created_at desc);

create index if not exists idx_story_favourites_story_slug
    on public.story_favourites(story_slug);

alter table public.story_favourites enable row level security;
