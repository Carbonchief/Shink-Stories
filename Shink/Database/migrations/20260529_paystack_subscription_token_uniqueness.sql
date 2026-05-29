with ranked_active_paystack as (
    select
        subscription_id,
        row_number() over (
            partition by subscriber_id, tier_code, provider_token
            order by
                case when provider_payment_id like 'SUB\_%' escape '\' then 0 else 1 end,
                next_renewal_at desc nulls last,
                created_at asc,
                subscription_id asc
        ) as duplicate_rank
    from public.subscriptions
    where provider = 'paystack'
      and source_system = 'shink_app'
      and status = 'active'
      and cancelled_at is null
      and provider_token is not null
)
update public.subscriptions subscription
set
    status = 'cancelled',
    cancelled_at = coalesce(subscription.cancelled_at, now()),
    updated_at = now()
from ranked_active_paystack ranked
where subscription.subscription_id = ranked.subscription_id
  and ranked.duplicate_rank > 1;

create unique index if not exists uq_subscriptions_active_paystack_token_tier
    on public.subscriptions(provider, subscriber_id, tier_code, provider_token)
    where provider = 'paystack'
      and source_system = 'shink_app'
      and status = 'active'
      and cancelled_at is null
      and provider_token is not null;
