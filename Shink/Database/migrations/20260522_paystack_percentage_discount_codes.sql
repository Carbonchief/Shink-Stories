alter table public.subscription_discount_codes
    add column if not exists discount_kind text not null default 'free_access',
    add column if not exists discount_percent numeric(5,2),
    add column if not exists discount_duration text not null default 'lifetime',
    add column if not exists discount_payment_count integer;

update public.subscription_discount_codes
set discount_kind = 'free_access',
    discount_duration = 'lifetime'
where discount_kind is null
   or discount_kind = '';

alter table public.subscription_discount_codes
    drop constraint if exists subscription_discount_codes_discount_kind_check,
    add constraint subscription_discount_codes_discount_kind_check
        check (discount_kind in ('free_access', 'percentage'));

alter table public.subscription_discount_codes
    drop constraint if exists subscription_discount_codes_discount_duration_check,
    add constraint subscription_discount_codes_discount_duration_check
        check (discount_duration in ('lifetime', 'first_payments'));

alter table public.subscription_discount_codes
    drop constraint if exists subscription_discount_codes_discount_percent_check,
    add constraint subscription_discount_codes_discount_percent_check
        check (discount_percent is null or (discount_percent > 0 and discount_percent <= 100));

alter table public.subscription_discount_codes
    drop constraint if exists subscription_discount_codes_discount_payment_count_check,
    add constraint subscription_discount_codes_discount_payment_count_check
        check (discount_payment_count is null or discount_payment_count in (1, 2, 3));

alter table public.subscription_discount_codes
    drop constraint if exists subscription_discount_codes_percentage_terms_check,
    add constraint subscription_discount_codes_percentage_terms_check
        check (
            discount_kind <> 'percentage'
            or (
                discount_percent is not null
                and bypass_payment = false
                and (
                    discount_duration = 'lifetime'
                    or (discount_duration = 'first_payments' and discount_payment_count in (1, 2, 3))
                )
            )
        );

alter table public.subscriptions
    add column if not exists recurring_billing_mode text not null default 'provider_subscription',
    add column if not exists discount_code_id uuid references public.subscription_discount_codes(discount_code_id) on delete set null,
    add column if not exists discount_percent numeric(5,2),
    add column if not exists discount_duration text,
    add column if not exists discount_payment_count integer,
    add column if not exists discount_payments_used integer not null default 0,
    add column if not exists undiscounted_billing_amount_zar numeric(18,8),
    add column if not exists authorization_reusable boolean;

alter table public.subscriptions
    drop constraint if exists subscriptions_recurring_billing_mode_check,
    add constraint subscriptions_recurring_billing_mode_check
        check (recurring_billing_mode in ('provider_subscription', 'paystack_authorization_schedule'));

alter table public.subscriptions
    drop constraint if exists subscriptions_discount_terms_check,
    add constraint subscriptions_discount_terms_check
        check (
            recurring_billing_mode <> 'paystack_authorization_schedule'
            or (
                provider = 'paystack'
                and discount_code_id is not null
                and discount_percent is not null
                and discount_percent > 0
                and discount_percent <= 100
                and discount_duration in ('lifetime', 'first_payments')
                and discount_payments_used >= 0
                and undiscounted_billing_amount_zar is not null
                and undiscounted_billing_amount_zar > 0
                and authorization_reusable = true
                and provider_token is not null
                and (
                    discount_duration = 'lifetime'
                    or discount_payment_count in (1, 2, 3)
                )
            )
        );

create index if not exists ix_subscriptions_paystack_authorization_schedule_due
    on public.subscriptions (next_renewal_at)
    where recurring_billing_mode = 'paystack_authorization_schedule'
      and status = 'active'
      and cancelled_at is null;

create index if not exists ix_subscriptions_discount_code_id
    on public.subscriptions (discount_code_id)
    where discount_code_id is not null;

create table if not exists public.subscription_recurring_charge_attempts (
    attempt_id uuid primary key default gen_random_uuid(),
    subscription_id uuid not null references public.subscriptions(subscription_id) on delete cascade,
    provider text not null default 'paystack',
    reference text not null,
    due_at timestamptz not null,
    amount_zar numeric(18,8) not null,
    discount_applied boolean not null default false,
    status text not null default 'pending',
    provider_transaction_id text,
    error_message text,
    payload jsonb,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    unique (reference),
    constraint subscription_recurring_charge_attempts_provider_check check (provider in ('paystack')),
    constraint subscription_recurring_charge_attempts_status_check check (status in ('pending', 'success', 'failed', 'skipped')),
    constraint subscription_recurring_charge_attempts_amount_check check (amount_zar > 0)
);

alter table public.subscription_recurring_charge_attempts enable row level security;

create index if not exists ix_subscription_recurring_charge_attempts_subscription
    on public.subscription_recurring_charge_attempts (subscription_id, due_at desc);

drop trigger if exists set_subscription_recurring_charge_attempts_updated_at on public.subscription_recurring_charge_attempts;
create trigger set_subscription_recurring_charge_attempts_updated_at
before update on public.subscription_recurring_charge_attempts
for each row execute function public.set_updated_at();
