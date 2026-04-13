create extension if not exists pgcrypto;

create table if not exists public.subscriber_notifications (
    notification_id uuid primary key default gen_random_uuid(),
    subscriber_id uuid not null references public.subscribers(subscriber_id) on delete cascade,
    notification_type text not null,
    source_key text not null,
    title text not null,
    body text,
    image_path text,
    image_alt text,
    href text,
    metadata jsonb not null default '{}'::jsonb,
    created_at timestamptz not null default now(),
    read_at timestamptz,
    constraint subscriber_notifications_type_not_blank check (btrim(notification_type) <> ''),
    constraint subscriber_notifications_source_key_not_blank check (btrim(source_key) <> ''),
    constraint subscriber_notifications_title_not_blank check (btrim(title) <> ''),
    constraint subscriber_notifications_href_check check (href is null or btrim(href) <> '')
);

create unique index if not exists idx_subscriber_notifications_unique_source
    on public.subscriber_notifications(subscriber_id, source_key);

create index if not exists idx_subscriber_notifications_created_at
    on public.subscriber_notifications(subscriber_id, created_at desc);

create index if not exists idx_subscriber_notifications_unread
    on public.subscriber_notifications(subscriber_id, read_at, created_at desc);

comment on table public.subscriber_notifications is 'Per-subscriber app notifications such as Karakter unlocks and future product updates.';
comment on column public.subscriber_notifications.source_key is 'Stable dedupe key per subscriber notification source.';
comment on column public.subscriber_notifications.metadata is 'Optional JSON payload for future notification-specific details.';

alter table public.subscriber_notifications enable row level security;
