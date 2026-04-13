alter table public.subscriber_notifications
    add column if not exists cleared_at timestamptz;

create index if not exists idx_subscriber_notifications_visible
    on public.subscriber_notifications(subscriber_id, cleared_at, created_at desc);

comment on column public.subscriber_notifications.cleared_at is 'Timestamp when a subscriber cleared this notification from their notification center.';
