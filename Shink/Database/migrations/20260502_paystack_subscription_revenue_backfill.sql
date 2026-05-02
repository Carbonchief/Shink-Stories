with paystack_event_amounts as (
    select distinct on (subscription.subscription_id)
        subscription.subscription_id,
        round(amounts.amount_in_cents / 100.0, 2) as billing_amount_zar,
        tier.billing_period_months
    from public.subscriptions as subscription
    left join public.subscription_tiers as tier
        on tier.tier_code = subscription.tier_code
    join public.subscription_events as event
        on event.provider = 'paystack'
       and (
            event.subscription_id = subscription.subscription_id
            or (
                event.subscription_id is null
                and event.provider_payment_id is not null
                and event.provider_payment_id = subscription.provider_payment_id
            )
            or (
                event.subscription_id is null
                and event.provider_transaction_id is not null
                and event.provider_transaction_id = subscription.provider_transaction_id
            )
       )
    cross join lateral (
        select coalesce(
            nullif(event.payload #>> '{data,amount}', '')::numeric,
            nullif(event.payload #>> '{data,plan,amount}', '')::numeric,
            nullif(event.payload ->> 'amount', '')::numeric
        ) as amount_in_cents
    ) as amounts
    where subscription.provider = 'paystack'
      and coalesce(subscription.billing_amount_zar, 0) = 0
      and amounts.amount_in_cents is not null
      and amounts.amount_in_cents > 0
      and (
            event.event_type in ('charge.success', 'subscription.create')
            or event.event_status in ('success', 'successful', 'paid')
      )
    order by subscription.subscription_id, event.received_at desc
)
update public.subscriptions as subscription
set billing_amount_zar = paystack_event_amounts.billing_amount_zar,
    billing_period_months = coalesce(subscription.billing_period_months, paystack_event_amounts.billing_period_months),
    billing_amount_source = 'paystack_payload'
from paystack_event_amounts
where subscription.subscription_id = paystack_event_amounts.subscription_id
  and subscription.provider = 'paystack'
  and coalesce(subscription.billing_amount_zar, 0) = 0;
