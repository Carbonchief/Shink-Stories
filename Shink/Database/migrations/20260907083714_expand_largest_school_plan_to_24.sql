-- Deploy alongside the updated catalog. Keep existing tier and payment identifiers
-- so subscriptions and teacher entitlements continue to resolve. Price is unchanged.
begin;

update public.subscription_tiers
set display_name = 'Skool 24',
    description = 'Jaarlikse skooltoegang vir 24 klaskamers.'
where tier_code = 'school_20_yearly';

update public.school_accounts
set plan_name = 'Skool 24',
    slot_limit = 24
where plan_tier_code = 'school_20_yearly';

commit;

-- Include a non-personal read-back in the deployment log.
select tier_code, display_name, description, price_zar
from public.subscription_tiers
where tier_code = 'school_20_yearly';

select plan_name, slot_limit, count(*) as school_count
from public.school_accounts
where plan_tier_code = 'school_20_yearly'
group by plan_name, slot_limit;
