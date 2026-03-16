create table if not exists public.story_views (
    story_view_id bigint generated always as identity primary key,
    subscriber_id uuid not null references public.subscribers(subscriber_id) on delete cascade,
    story_slug text not null references public.stories(slug) on delete restrict,
    story_path text not null,
    viewed_at timestamptz not null default now(),
    metadata jsonb not null default '{}'::jsonb,
    constraint story_views_story_path_not_blank check (btrim(story_path) <> '')
);

create index if not exists idx_story_views_subscriber_viewed_at
    on public.story_views(subscriber_id, viewed_at desc);

create index if not exists idx_story_views_story_slug_viewed_at
    on public.story_views(story_slug, viewed_at desc);

create table if not exists public.story_listen_events (
    story_listen_event_id bigint generated always as identity primary key,
    subscriber_id uuid not null references public.subscribers(subscriber_id) on delete cascade,
    story_slug text not null references public.stories(slug) on delete restrict,
    story_path text not null,
    session_id uuid not null,
    event_type text not null default 'progress',
    listened_seconds numeric(10, 3) not null default 0,
    position_seconds numeric(10, 3),
    duration_seconds numeric(10, 3),
    occurred_at timestamptz not null default now(),
    metadata jsonb not null default '{}'::jsonb,
    constraint story_listen_events_story_path_not_blank check (btrim(story_path) <> ''),
    constraint story_listen_events_event_type_check check (event_type in ('progress', 'pause', 'ended', 'pagehide', 'visibilityhidden')),
    constraint story_listen_events_listened_seconds_check check (listened_seconds >= 0 and listened_seconds <= 3600),
    constraint story_listen_events_position_seconds_check check (position_seconds is null or position_seconds >= 0),
    constraint story_listen_events_duration_seconds_check check (duration_seconds is null or duration_seconds > 0)
);

create index if not exists idx_story_listen_events_subscriber_story_occurred
    on public.story_listen_events(subscriber_id, story_slug, occurred_at desc);

create index if not exists idx_story_listen_events_story_occurred
    on public.story_listen_events(story_slug, occurred_at desc);

create index if not exists idx_story_listen_events_session_occurred
    on public.story_listen_events(session_id, occurred_at desc);

alter table public.story_views enable row level security;
alter table public.story_listen_events enable row level security;
