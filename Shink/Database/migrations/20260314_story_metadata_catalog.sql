create extension if not exists pgcrypto;

create table if not exists public.stories (
    story_id uuid primary key default gen_random_uuid(),
    slug text not null unique,
    title text not null,
    subtitle text,
    summary text,
    description text,
    language_code text not null default 'af',
    age_min smallint check (age_min is null or age_min >= 0),
    age_max smallint check (age_max is null or (age_min is not null and age_max >= age_min)),
    narrator text,
    author_name text,
    illustrator_name text,
    duration_seconds integer check (duration_seconds is null or duration_seconds > 0),
    cover_image_path text,
    thumbnail_image_path text,
    audio_provider text not null default 'local',
    audio_bucket text,
    audio_object_key text,
    audio_content_type text,
    audio_file_size_bytes bigint check (audio_file_size_bytes is null or audio_file_size_bytes >= 0),
    audio_etag text,
    audio_sha256 text,
    audio_last_modified_at timestamptz,
    access_level text not null default 'subscriber',
    status text not null default 'draft',
    is_featured boolean not null default false,
    sort_order integer not null default 0,
    tags text[] not null default '{}'::text[],
    metadata jsonb not null default '{}'::jsonb,
    published_at timestamptz,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint stories_slug_format check (slug ~ '^[a-z0-9]+(?:-[a-z0-9]+)*$'),
    constraint stories_slug_not_blank check (btrim(slug) <> ''),
    constraint stories_title_not_blank check (btrim(title) <> ''),
    constraint stories_audio_provider_check check (audio_provider in ('local', 'r2')),
    constraint stories_access_level_check check (access_level in ('free', 'subscriber')),
    constraint stories_status_check check (status in ('draft', 'published', 'archived')),
    constraint stories_audio_bucket_not_blank check (audio_bucket is null or btrim(audio_bucket) <> ''),
    constraint stories_audio_object_key_not_blank check (audio_object_key is null or btrim(audio_object_key) <> ''),
    constraint stories_r2_fields_required check (audio_provider <> 'r2' or (audio_bucket is not null and audio_object_key is not null)),
    constraint stories_published_requires_audio check (status <> 'published' or audio_object_key is not null),
    constraint stories_published_requires_timestamp check (status <> 'published' or published_at is not null)
);

comment on table public.stories is 'Story metadata and playback references. Media bytes are not stored in the database.';
comment on column public.stories.audio_provider is 'Storage provider for the audio object. local=filesystem path, r2=Cloudflare R2 object.';
comment on column public.stories.audio_object_key is 'Relative file path for local provider or object key for R2 provider.';
comment on column public.stories.metadata is 'Flexible JSON metadata for future story attributes and import data.';

create index if not exists idx_stories_status_published_at
    on public.stories(status, published_at desc);

create index if not exists idx_stories_access_level_status
    on public.stories(access_level, status, published_at desc);

create index if not exists idx_stories_featured_sort
    on public.stories(is_featured, sort_order, published_at desc);

create index if not exists idx_stories_audio_lookup
    on public.stories(audio_provider, audio_bucket, audio_object_key);

create index if not exists idx_stories_tags_gin
    on public.stories using gin (tags);

create index if not exists idx_stories_metadata_gin
    on public.stories using gin (metadata jsonb_path_ops);

alter table public.stories enable row level security;

drop policy if exists stories_read_published on public.stories;
create policy stories_read_published
on public.stories
for select
to anon, authenticated
using (
    status = 'published'
    and published_at <= now()
);

create or replace function public.set_updated_at()
returns trigger
language plpgsql
set search_path = pg_catalog, public
as $$
begin
    new.updated_at = now();
    return new;
end;
$$;

drop trigger if exists trg_stories_set_updated_at on public.stories;
create trigger trg_stories_set_updated_at
before update on public.stories
for each row execute function public.set_updated_at();
