update public.subscription_tiers
set paystack_plan_code = case tier_code
    when 'story_corner_monthly' then 'PLN_7nopwqt07y34dy7'
    when 'all_stories_monthly' then 'PLN_8qo97ge1vs631n9'
    when 'all_stories_yearly' then 'PLN_hc4wkuh10y6xz30'
    else paystack_plan_code
end
where tier_code in (
    'story_corner_monthly',
    'all_stories_monthly',
    'all_stories_yearly'
);
