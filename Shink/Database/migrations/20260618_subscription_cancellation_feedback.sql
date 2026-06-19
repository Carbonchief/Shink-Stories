create table if not exists public.subscription_cancellation_feedback (
    feedback_id uuid primary key default gen_random_uuid(),
    subscriber_id uuid not null references public.subscribers(subscriber_id) on delete cascade,
    subscription_id uuid references public.subscriptions(subscription_id) on delete set null,
    tier_code text,
    provider text,
    feedback_status text not null check (feedback_status in ('submitted', 'skipped')),
    reason_code text,
    note text,
    cancelled_subscription_count integer not null default 1 check (cancelled_subscription_count >= 1),
    created_at timestamptz not null default timezone('utc', now())
);

create index if not exists idx_subscription_cancellation_feedback_created_at
    on public.subscription_cancellation_feedback (created_at desc);

create index if not exists idx_subscription_cancellation_feedback_subscriber_created_at
    on public.subscription_cancellation_feedback (subscriber_id, created_at desc);

alter table public.subscription_cancellation_feedback enable row level security;

grant select, insert, update, delete on table
    public.subscription_cancellation_feedback
to service_role;
