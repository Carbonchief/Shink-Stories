alter table public.subscriptions
    drop constraint if exists subscriptions_provider_check;

alter table public.subscriptions
    add constraint subscriptions_provider_check
    check (provider in ('payfast', 'paystack', 'free', 'apple', 'google_play'));

alter table public.subscriptions
    drop constraint if exists subscriptions_billing_amount_source_known;

alter table public.subscriptions
    add constraint subscriptions_billing_amount_source_known
    check (
        billing_amount_source is null or
        billing_amount_source in (
            'checkout',
            'payfast_itn',
            'paystack_payload',
            'wordpress_import',
            'manual',
            'plan_change',
            'app_store',
            'google_play'
        )
    ) not valid;

alter table public.subscriptions
    validate constraint subscriptions_billing_amount_source_known;
