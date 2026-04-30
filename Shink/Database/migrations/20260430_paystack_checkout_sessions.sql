create table if not exists public.paystack_checkout_sessions (
    session_id uuid primary key default gen_random_uuid(),
    provider text not null default 'paystack',
    checkout_kind text not null,
    customer_email text not null,
    plan_slug text not null,
    tier_code text not null,
    amount_in_cents bigint not null,
    currency text not null default 'ZAR',
    callback_url text not null,
    reference text not null,
    authorization_url text not null,
    status text not null default 'pending',
    expires_at timestamp with time zone not null,
    metadata jsonb not null default '{}'::jsonb,
    created_at timestamp with time zone not null default now(),
    updated_at timestamp with time zone not null default now(),
    constraint paystack_checkout_sessions_checkout_kind_check
        check (checkout_kind in ('subscription')),
    constraint paystack_checkout_sessions_status_check
        check (status in ('pending', 'paid', 'expired', 'cancelled', 'failed')),
    constraint paystack_checkout_sessions_amount_check
        check (amount_in_cents > 0),
    constraint paystack_checkout_sessions_email_lower_check
        check (customer_email = lower(customer_email))
);

alter table public.paystack_checkout_sessions enable row level security;

create unique index if not exists uq_paystack_checkout_sessions_reference
    on public.paystack_checkout_sessions(provider, reference);

create unique index if not exists uq_paystack_checkout_sessions_pending_subscription
    on public.paystack_checkout_sessions(
        provider,
        checkout_kind,
        customer_email,
        tier_code,
        amount_in_cents,
        currency,
        md5(callback_url)
    )
    where status = 'pending'
      and provider = 'paystack'
      and checkout_kind = 'subscription';

create index if not exists idx_paystack_checkout_sessions_reuse_lookup
    on public.paystack_checkout_sessions(
        provider,
        checkout_kind,
        status,
        customer_email,
        tier_code,
        amount_in_cents,
        currency,
        expires_at desc
    );

comment on table public.paystack_checkout_sessions is
    'Short-lived Paystack checkout URLs reused to avoid creating duplicate transaction attempts while a customer is still deciding.';
