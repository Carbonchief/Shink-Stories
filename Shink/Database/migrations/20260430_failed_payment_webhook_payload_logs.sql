create table if not exists public.payment_webhook_failures (
    failure_id uuid primary key default gen_random_uuid(),
    provider text not null,
    event_type text not null,
    event_status text,
    provider_payment_id text,
    provider_transaction_id text,
    failure_stage text not null,
    error_message text not null,
    payload jsonb not null,
    received_at timestamptz not null default now(),
    created_at timestamptz not null default now(),
    constraint payment_webhook_failures_provider_not_blank check (btrim(provider) <> ''),
    constraint payment_webhook_failures_event_type_not_blank check (btrim(event_type) <> ''),
    constraint payment_webhook_failures_failure_stage_not_blank check (btrim(failure_stage) <> ''),
    constraint payment_webhook_failures_error_message_not_blank check (btrim(error_message) <> '')
);

create index if not exists idx_payment_webhook_failures_received_at
    on public.payment_webhook_failures(received_at desc);

create index if not exists idx_payment_webhook_failures_provider_event
    on public.payment_webhook_failures(provider, event_type, received_at desc);

create index if not exists idx_payment_webhook_failures_provider_payment_id
    on public.payment_webhook_failures(provider, provider_payment_id, received_at desc)
    where provider_payment_id is not null;

create index if not exists idx_payment_webhook_failures_provider_transaction_id
    on public.payment_webhook_failures(provider, provider_transaction_id, received_at desc)
    where provider_transaction_id is not null;

alter table public.payment_webhook_failures enable row level security;
