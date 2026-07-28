insert into public.subscription_tiers (
    tier_code,
    display_name,
    description,
    billing_period_months,
    price_zar,
    payfast_plan_slug,
    is_active
)
values (
    'school_20_yearly',
    'Skool 20',
    'Jaarlikse skooltoegang vir 20 klaskamers.',
    12,
    28800.00,
    'skool-20-jaarliks',
    true
)
on conflict (tier_code) do update
set
    display_name = excluded.display_name,
    description = excluded.description,
    billing_period_months = excluded.billing_period_months,
    price_zar = excluded.price_zar,
    payfast_plan_slug = excluded.payfast_plan_slug,
    is_active = excluded.is_active;
