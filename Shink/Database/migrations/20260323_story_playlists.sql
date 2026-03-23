create extension if not exists pgcrypto;

create table if not exists public.story_playlists (
    playlist_id uuid primary key default gen_random_uuid(),
    slug text not null unique,
    title text not null,
    description text,
    sort_order integer not null default 0,
    max_items integer check (max_items is null or max_items > 0),
    is_enabled boolean not null default true,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint story_playlists_slug_format check (slug ~ '^[a-z0-9]+(?:-[a-z0-9]+)*$'),
    constraint story_playlists_slug_not_blank check (btrim(slug) <> ''),
    constraint story_playlists_title_not_blank check (btrim(title) <> '')
);

comment on table public.story_playlists is 'Configurable story playlists shown on /luister.';
comment on column public.story_playlists.sort_order is 'Lower values appear first on /luister.';
comment on column public.story_playlists.max_items is 'Optional item cap for a playlist; null means unlimited.';

create table if not exists public.story_playlist_items (
    playlist_id uuid not null references public.story_playlists(playlist_id) on delete cascade,
    story_id uuid not null references public.stories(story_id) on delete cascade,
    sort_order integer not null default 0,
    created_at timestamptz not null default now(),
    primary key (playlist_id, story_id)
);

comment on table public.story_playlist_items is 'Ordered story membership for playlists.';

create index if not exists idx_story_playlists_enabled_sort
    on public.story_playlists(is_enabled, sort_order, title);

create index if not exists idx_story_playlist_items_playlist_sort
    on public.story_playlist_items(playlist_id, sort_order, story_id);

create index if not exists idx_story_playlist_items_story
    on public.story_playlist_items(story_id);

alter table public.story_playlists enable row level security;
alter table public.story_playlist_items enable row level security;

drop policy if exists story_playlists_read_enabled on public.story_playlists;
create policy story_playlists_read_enabled
on public.story_playlists
for select
to anon, authenticated
using (is_enabled = true);

drop policy if exists story_playlist_items_read_enabled on public.story_playlist_items;
create policy story_playlist_items_read_enabled
on public.story_playlist_items
for select
to anon, authenticated
using (
    exists (
        select 1
        from public.story_playlists playlist
        where
            playlist.playlist_id = story_playlist_items.playlist_id
            and playlist.is_enabled = true
    )
);

drop trigger if exists trg_story_playlists_set_updated_at on public.story_playlists;
create trigger trg_story_playlists_set_updated_at
before update on public.story_playlists
for each row execute function public.set_updated_at();

insert into public.story_playlists (
    slug,
    title,
    description,
    sort_order,
    max_items,
    is_enabled
)
values
    ('gratis-stories', 'Gratis stories', 'Dis op die huis!', 10, null, true),
    ('top-10-nuutste-stories', 'Top 10 nuutste stories', 'Kry ''n voorsmakie van ons nuutste uitgawes.', 20, 10, true),
    ('stories-vir-kleuters', 'Stories vir Kleuters', 'Verken stories spesiaal vir kleuters.', 30, null, true),
    ('bybelstories', 'Bybelstories', 'Luister na ons Bybelstories vir kinders.', 40, 10, true),
    ('all-stories', 'Alle stories', 'Stories wat nie in ander playlists is nie.', 50, null, true)
on conflict (slug) do update
set
    title = excluded.title,
    description = excluded.description,
    sort_order = excluded.sort_order,
    max_items = excluded.max_items,
    is_enabled = excluded.is_enabled,
    updated_at = now();

with target_playlists as (
    select playlist_id
    from public.story_playlists
    where slug in (
        'gratis-stories',
        'top-10-nuutste-stories',
        'stories-vir-kleuters',
        'bybelstories'
    )
)
delete from public.story_playlist_items as items
using target_playlists
where items.playlist_id = target_playlists.playlist_id;

with playlist as (
    select playlist_id
    from public.story_playlists
    where slug = 'gratis-stories'
),
eligible as (
    select s.story_id, s.sort_order, s.title
    from public.stories as s
    where
        s.status = 'published'
        and s.published_at <= now()
        and s.access_level = 'free'
        and not (
            (s.duration_seconds is not null and s.duration_seconds <= 60)
            or coalesce(s.audio_object_key, '') ilike 'imported/soundbites/%'
            or coalesce(s.audio_object_key, '') ilike 'imported/non-story-audio/%'
            or coalesce(s.metadata::text, '') ilike '%soundbite%'
        )
)
insert into public.story_playlist_items (playlist_id, story_id, sort_order)
select
    playlist.playlist_id,
    eligible.story_id,
    row_number() over (order by eligible.sort_order asc, eligible.title asc)
from playlist
cross join eligible
order by sort_order;

with playlist as (
    select playlist_id
    from public.story_playlists
    where slug = 'top-10-nuutste-stories'
),
eligible as (
    select s.story_id, s.sort_order, s.title, s.published_at, s.is_featured
    from public.stories as s
    where
        s.status = 'published'
        and s.published_at <= now()
        and s.access_level = 'subscriber'
        and not (
            (s.duration_seconds is not null and s.duration_seconds <= 60)
            or coalesce(s.audio_object_key, '') ilike 'imported/soundbites/%'
            or coalesce(s.audio_object_key, '') ilike 'imported/non-story-audio/%'
            or coalesce(s.metadata::text, '') ilike '%soundbite%'
        )
),
featured as (
    select
        e.story_id,
        row_number() over (
            order by e.sort_order asc, e.published_at desc nulls last, e.title asc
        ) as featured_position
    from eligible as e
    where e.is_featured = true
),
featured_limited as (
    select story_id, featured_position
    from featured
    where featured_position <= 10
),
featured_count as (
    select count(*)::int as total_featured
    from featured_limited
),
remainder as (
    select
        e.story_id,
        row_number() over (
            order by e.published_at desc nulls last, e.sort_order asc, e.title asc
        ) as remainder_position
    from eligible as e
    where not exists (
        select 1
        from featured_limited as f
        where f.story_id = e.story_id
    )
),
selected as (
    select
        story_id,
        featured_position as sort_order
    from featured_limited
    union all
    select
        r.story_id,
        fc.total_featured + r.remainder_position as sort_order
    from remainder as r
    cross join featured_count as fc
    where r.remainder_position <= greatest(0, 10 - fc.total_featured)
)
insert into public.story_playlist_items (playlist_id, story_id, sort_order)
select
    playlist.playlist_id,
    selected.story_id,
    selected.sort_order
from playlist
cross join selected
order by selected.sort_order;

with playlist as (
    select playlist_id
    from public.story_playlists
    where slug = 'stories-vir-kleuters'
),
kleuter_targets(target_order, title) as (
    values
        (1, 'Tiekie Tik Tik Tok'),
        (2, 'Die Kwaaibok se Klip'),
        (3, 'Rammetjie Uitnek'),
        (4, 'Die Kwaai Grommel'),
        (5, 'Hailey Hasie se Groentetuin'),
        (6, 'Maniere wys jou spiere'),
        (7, 'Seekoei Sluit sy mond toe'),
        (8, 'Koalabeertjie Klou'),
        (9, 'Robot doen reg'),
        (10, 'Babbelbessie'),
        (11, 'Dankie en die mislike skree'),
        (12, 'Dankie en die lelike praat'),
        (13, 'Fantjie Leer Skryf')
),
eligible as (
    select s.story_id, s.title, s.published_at, s.sort_order
    from public.stories as s
    where
        s.status = 'published'
        and s.published_at <= now()
        and s.access_level in ('free', 'subscriber')
        and not (
            (s.duration_seconds is not null and s.duration_seconds <= 60)
            or coalesce(s.audio_object_key, '') ilike 'imported/soundbites/%'
            or coalesce(s.audio_object_key, '') ilike 'imported/non-story-audio/%'
            or coalesce(s.metadata::text, '') ilike '%soundbite%'
        )
)
insert into public.story_playlist_items (playlist_id, story_id, sort_order)
select
    playlist.playlist_id,
    matched.story_id,
    kleuter_targets.target_order
from playlist
join kleuter_targets on true
join lateral (
    select e.story_id
    from eligible as e
    where lower(btrim(e.title)) = lower(btrim(kleuter_targets.title))
    order by e.published_at desc nulls last, e.sort_order asc, e.title asc
    limit 1
) as matched on true
order by kleuter_targets.target_order;

with playlist as (
    select playlist_id
    from public.story_playlists
    where slug = 'bybelstories'
),
eligible as (
    select s.story_id, s.title, s.sort_order, s.published_at
    from public.stories as s
    where
        s.status = 'published'
        and s.published_at <= now()
        and s.access_level = 'subscriber'
        and not (
            (s.duration_seconds is not null and s.duration_seconds <= 60)
            or coalesce(s.audio_object_key, '') ilike 'imported/soundbites/%'
            or coalesce(s.audio_object_key, '') ilike 'imported/non-story-audio/%'
            or coalesce(s.metadata::text, '') ilike '%soundbite%'
        )
        and (
            coalesce(s.audio_object_key, '') ilike '%bybel%'
            or coalesce(s.title, '') ilike '%bybel%'
            or coalesce(s.slug, '') ilike '%bybel%'
            or coalesce(s.title, '') ilike '%byble%'
            or coalesce(s.slug, '') ilike '%byble%'
            or exists (
                select 1
                from unnest(coalesce(s.tags, '{}'::text[])) as tag
                where lower(tag) in ('bybel', 'byble')
            )
            or coalesce(s.metadata::text, '') ilike '%bybel%'
            or coalesce(s.metadata::text, '') ilike '%byble%'
        )
),
ordered as (
    select
        e.story_id,
        row_number() over (
            order by e.published_at desc nulls last, e.sort_order asc, e.title asc
        ) as sort_order
    from eligible as e
)
insert into public.story_playlist_items (playlist_id, story_id, sort_order)
select
    playlist.playlist_id,
    ordered.story_id,
    ordered.sort_order
from playlist
cross join ordered
where ordered.sort_order <= 10
order by ordered.sort_order;
