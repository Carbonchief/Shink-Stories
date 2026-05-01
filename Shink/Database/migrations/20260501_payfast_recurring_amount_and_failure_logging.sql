alter table public.subscriptions
    add column if not exists billing_amount_zar numeric(10, 2),
    add column if not exists billing_period_months integer,
    add column if not exists billing_amount_source text;

do $$
begin
    if not exists (
        select 1
        from pg_constraint
        where conname = 'subscriptions_billing_amount_zar_non_negative'
          and conrelid = 'public.subscriptions'::regclass
    ) then
        alter table public.subscriptions
            add constraint subscriptions_billing_amount_zar_non_negative
                check (billing_amount_zar is null or billing_amount_zar >= 0) not valid;
    end if;

    if not exists (
        select 1
        from pg_constraint
        where conname = 'subscriptions_billing_period_months_positive'
          and conrelid = 'public.subscriptions'::regclass
    ) then
        alter table public.subscriptions
            add constraint subscriptions_billing_period_months_positive
                check (billing_period_months is null or billing_period_months > 0) not valid;
    end if;

    if not exists (
        select 1
        from pg_constraint
        where conname = 'subscriptions_billing_amount_source_known'
          and conrelid = 'public.subscriptions'::regclass
    ) then
        alter table public.subscriptions
            add constraint subscriptions_billing_amount_source_known
                check (
                    billing_amount_source is null or
                    billing_amount_source in ('checkout', 'payfast_itn', 'paystack_payload', 'wordpress_import', 'manual')
                ) not valid;
    end if;
end $$;

comment on column public.subscriptions.billing_amount_zar is
    'Provider recurring charge amount captured at checkout or first successful webhook. Used to validate future recurring webhooks without rejecting grandfathered prices.';

comment on column public.subscriptions.billing_period_months is
    'Recurring billing period in months for the captured provider amount.';

comment on column public.subscriptions.billing_amount_source is
    'Source for billing_amount_zar, such as checkout or payfast_itn.';

with latest_payfast_amount as (
    select distinct on (provider_payment_id)
        provider_payment_id,
        nullif(payload ->> 'amount_gross', '')::numeric(10, 2) as amount_gross
    from public.subscription_events
    where provider = 'payfast'
      and event_type = 'payfast_itn'
      and event_status = 'COMPLETE'
      and provider_payment_id is not null
      and payload ? 'amount_gross'
      and nullif(payload ->> 'amount_gross', '') is not null
    order by provider_payment_id, received_at desc
)
update public.subscriptions as subscription
set billing_amount_zar = latest_payfast_amount.amount_gross,
    billing_period_months = coalesce(
        subscription.billing_period_months,
        (
            select tier.billing_period_months
            from public.subscription_tiers as tier
            where tier.tier_code = subscription.tier_code
            limit 1
        )
    ),
    billing_amount_source = coalesce(subscription.billing_amount_source, 'payfast_itn')
from latest_payfast_amount
where subscription.provider = 'payfast'
  and subscription.provider_payment_id = latest_payfast_amount.provider_payment_id
  and subscription.billing_amount_zar is null;
