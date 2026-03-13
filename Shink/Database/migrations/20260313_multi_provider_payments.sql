alter table public.subscription_tiers
    add column if not exists paystack_plan_code text;

create unique index if not exists uq_subscription_tiers_paystack_plan_code
    on public.subscription_tiers(paystack_plan_code)
    where paystack_plan_code is not null;

alter table public.subscriptions
    alter column provider set default 'paystack';

alter table public.subscription_events
    alter column provider set default 'paystack';

alter table public.subscriptions
    drop constraint if exists subscriptions_provider_payment_id_key;

alter table public.subscriptions
    drop constraint if exists subscriptions_provider_transaction_id_key;

create unique index if not exists uq_subscriptions_provider_payment
    on public.subscriptions(provider, provider_payment_id);

create unique index if not exists uq_subscriptions_provider_transaction
    on public.subscriptions(provider, provider_transaction_id)
    where provider_transaction_id is not null;

create index if not exists idx_subscriptions_provider_status
    on public.subscriptions(provider, status);

create index if not exists idx_subscription_events_provider_payment_received
    on public.subscription_events(provider, provider_payment_id, received_at desc);

do $$
begin
    if not exists (
        select 1
        from pg_constraint
        where conname = 'subscriptions_provider_check'
          and conrelid = 'public.subscriptions'::regclass
    ) then
        alter table public.subscriptions
            add constraint subscriptions_provider_check
            check (provider in ('payfast', 'paystack'));
    end if;
end
$$;

do $$
begin
    if not exists (
        select 1
        from pg_constraint
        where conname = 'subscription_events_provider_check'
          and conrelid = 'public.subscription_events'::regclass
    ) then
        alter table public.subscription_events
            add constraint subscription_events_provider_check
            check (provider in ('payfast', 'paystack'));
    end if;
end
$$;
