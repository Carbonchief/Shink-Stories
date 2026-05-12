create temporary table tmp_paystack_signup_duplicate_pairs
on commit drop
as
with paystack_active as (
    select
        s.subscription_id,
        s.subscriber_id,
        s.tier_code,
        s.provider_payment_id,
        s.provider_token,
        case
            when s.provider_payment_id like 'SUB_%' then true
            else false
        end as is_canonical
    from public.subscriptions s
    where s.provider = 'paystack'
      and s.source_system = 'shink_app'
      and s.status = 'active'
      and s.cancelled_at is null
      and s.tier_code <> 'gratis'
      and nullif(trim(s.provider_token), '') is not null
),
grouped as (
    select
        subscriber_id,
        tier_code,
        provider_token,
        count(*) as row_count,
        count(*) filter (where is_canonical) as canonical_count,
        count(*) filter (where not is_canonical) as non_canonical_count
    from paystack_active
    group by subscriber_id, tier_code, provider_token
)
select
    duplicate.subscription_id as duplicate_subscription_id,
    canonical.subscription_id as canonical_subscription_id,
    canonical.provider_payment_id as canonical_provider_payment_id
from grouped g
join paystack_active canonical
  on canonical.subscriber_id = g.subscriber_id
 and canonical.tier_code = g.tier_code
 and canonical.provider_token = g.provider_token
 and canonical.is_canonical
join paystack_active duplicate
  on duplicate.subscriber_id = g.subscriber_id
 and duplicate.tier_code = g.tier_code
 and duplicate.provider_token = g.provider_token
 and not duplicate.is_canonical
where g.canonical_count = 1
  and g.non_canonical_count >= 1;

update public.subscriptions canonical
set provider_token = coalesce(canonical.provider_token, duplicate.provider_token),
    provider_email_token = coalesce(canonical.provider_email_token, duplicate.provider_email_token),
    next_renewal_at = case
        when canonical.next_renewal_at is null then duplicate.next_renewal_at
        when duplicate.next_renewal_at is null then canonical.next_renewal_at
        when duplicate.next_renewal_at > canonical.next_renewal_at then duplicate.next_renewal_at
        else canonical.next_renewal_at
    end,
    billing_amount_zar = coalesce(canonical.billing_amount_zar, duplicate.billing_amount_zar),
    billing_period_months = coalesce(canonical.billing_period_months, duplicate.billing_period_months),
    billing_amount_source = coalesce(canonical.billing_amount_source, duplicate.billing_amount_source),
    cancelled_at = coalesce(canonical.cancelled_at, duplicate.cancelled_at)
from tmp_paystack_signup_duplicate_pairs pairs
join public.subscriptions duplicate
  on duplicate.subscription_id = pairs.duplicate_subscription_id
where canonical.subscription_id = pairs.canonical_subscription_id;

update public.subscription_events event_rows
set subscription_id = pairs.canonical_subscription_id
from tmp_paystack_signup_duplicate_pairs pairs
where event_rows.subscription_id = pairs.duplicate_subscription_id;

update public.subscription_payment_recoveries recovery_rows
set subscription_id = pairs.canonical_subscription_id,
    provider_payment_id = pairs.canonical_provider_payment_id
from tmp_paystack_signup_duplicate_pairs pairs
where recovery_rows.subscription_id = pairs.duplicate_subscription_id;

update public.subscription_discount_code_redemptions redemption_rows
set granted_subscription_id = pairs.canonical_subscription_id
from tmp_paystack_signup_duplicate_pairs pairs
where redemption_rows.granted_subscription_id = pairs.duplicate_subscription_id;

delete from public.subscriptions duplicate
using tmp_paystack_signup_duplicate_pairs pairs
where duplicate.subscription_id = pairs.duplicate_subscription_id;
