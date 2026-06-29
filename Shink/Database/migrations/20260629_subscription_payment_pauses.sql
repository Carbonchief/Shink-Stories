create table if not exists public.subscription_payment_pauses (
    pause_id uuid primary key default gen_random_uuid(),
    subscriber_id uuid not null references public.subscribers(subscriber_id) on delete cascade,
    paid_subscription_id uuid not null references public.subscriptions(subscription_id) on delete cascade,
    discount_code_id uuid references public.subscription_discount_codes(discount_code_id) on delete set null,
    redemption_id uuid references public.subscription_discount_code_redemptions(redemption_id) on delete set null,
    tier_code text not null references public.subscription_tiers(tier_code) on delete restrict,
    provider text not null default 'paystack',
    provider_payment_id text not null,
    provider_email_token text,
    status text not null default 'active',
    pause_started_at timestamptz not null default now(),
    pause_ends_at timestamptz not null,
    resume_grace_ends_at timestamptz,
    resumed_at timestamptz,
    last_resume_attempt_at timestamptz,
    resume_attempt_count integer not null default 0,
    last_error text,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint subscription_payment_pauses_provider_check check (provider = 'paystack'),
    constraint subscription_payment_pauses_status_check check (status in ('active', 'resume_pending', 'resumed', 'resume_failed')),
    constraint subscription_payment_pauses_attempt_count_check check (resume_attempt_count >= 0),
    constraint subscription_payment_pauses_window_check check (pause_ends_at > pause_started_at),
    constraint subscription_payment_pauses_resume_state_check check (
        (status = 'resumed' and resumed_at is not null)
        or (status <> 'resumed')
    )
);

alter table public.subscription_payment_pauses enable row level security;

create index if not exists ix_subscription_payment_pauses_due
    on public.subscription_payment_pauses (pause_ends_at)
    where status = 'active';

create index if not exists ix_subscription_payment_pauses_subscriber
    on public.subscription_payment_pauses (subscriber_id, pause_started_at desc);

create unique index if not exists uq_subscription_payment_pauses_open_paid_subscription
    on public.subscription_payment_pauses (paid_subscription_id)
    where status in ('active', 'resume_pending');

drop trigger if exists trg_subscription_payment_pauses_set_updated_at on public.subscription_payment_pauses;
create trigger trg_subscription_payment_pauses_set_updated_at
before update on public.subscription_payment_pauses
for each row execute function public.set_updated_at();
