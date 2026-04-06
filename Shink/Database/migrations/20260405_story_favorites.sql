create table if not exists public.story_favorites (
    story_favorite_id bigint generated always as identity primary key,
    subscriber_id uuid not null references public.subscribers(subscriber_id) on delete cascade,
    story_slug text not null references public.stories(slug) on delete restrict,
    story_path text not null,
    source text not null default 'luister',
    metadata jsonb not null default '{}'::jsonb,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint story_favorites_story_path_not_blank check (btrim(story_path) <> ''),
    constraint story_favorites_source_check check (source in ('gratis', 'luister'))
);

create unique index if not exists ux_story_favorites_subscriber_story_source
    on public.story_favorites(subscriber_id, story_slug, source);

create index if not exists idx_story_favorites_subscriber_source_updated
    on public.story_favorites(subscriber_id, source, updated_at desc);

create index if not exists idx_story_favorites_story_source_updated
    on public.story_favorites(story_slug, source, updated_at desc);

alter table public.story_favorites enable row level security;

drop trigger if exists trg_story_favorites_set_updated_at on public.story_favorites;
create trigger trg_story_favorites_set_updated_at
before update on public.story_favorites
for each row execute function public.set_updated_at();
