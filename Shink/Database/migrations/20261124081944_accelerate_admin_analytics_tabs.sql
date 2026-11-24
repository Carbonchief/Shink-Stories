-- Count a session only once across dates and listeners. The legacy GROUP BY
-- also counted a single session for a story with a NULL session identifier.
create or replace function public.get_admin_story_analytics()
returns jsonb language sql stable security invoker
set search_path = pg_catalog, public
set work_mem = '16MB'
as $$
with listeners as (
    select story_slug, sum(events)::integer as total_listen_events, count(distinct subscriber_id)::integer as unique_listeners,
        (count(distinct session_id) + bool_or(session_id is null)::integer)::integer as total_listen_sessions, sum(seconds) as seconds
    from private.admin_listen_totals group by story_slug
), views as (
    select story_slug, sum(views)::integer as total_views, count(subscriber_id)::integer as unique_viewers, max(last_view_at) as last_view_at
    from (
        select story_slug, subscriber_id, count(*) as views, max(viewed_at) as last_view_at
        from public.story_views group by story_slug, subscriber_id
    ) per_viewer group by story_slug
), favorites as (
    select story_slug, count(distinct subscriber_id)::integer as total_favorites, max(favorited_at) as last_favorite_at
    from (
        select story_slug, subscriber_id, coalesce(updated_at, created_at) as favorited_at from public.story_favorites
        union all
        select story_slug, subscriber_id, created_at from public.story_favourites
    ) favorite_events where btrim(story_slug) <> '' group by story_slug
), stories as (
    select s.slug as story_slug, s.title, coalesce(v.total_views, 0) as total_views,
        coalesce(v.unique_viewers, 0) as unique_viewers, v.last_view_at,
        coalesce(l.total_listen_events, 0) as total_listen_events, coalesce(l.unique_listeners, 0) as unique_listeners,
        coalesce(l.total_listen_sessions, 0) as total_listen_sessions, coalesce(round(l.seconds, 1), 0) as total_listened_seconds,
        coalesce(round(l.seconds / l.total_listen_sessions, 1), 0) as average_listened_seconds_per_session, latest.last_listen_at,
        coalesce(f.total_favorites, 0) as total_favorites, coalesce(f.total_favorites, 0) as unique_favoriters,
        f.last_favorite_at, nullif(greatest(v.last_view_at, latest.last_listen_at, f.last_favorite_at), '-infinity'::timestamptz) as last_activity_at
    from public.stories s
    left join views v on v.story_slug = s.slug
    left join listeners l on l.story_slug = s.slug
    left join favorites f on f.story_slug = s.slug
    left join lateral (
        select max(e.occurred_at) as last_listen_at from public.story_listen_events e where e.story_slug = s.slug
    ) latest on true
)
select jsonb_build_object('generated_at', now(),
    'story_analytics', coalesce(jsonb_agg(stories order by title, story_slug), '[]')) from stories;
$$;
revoke all on function public.get_admin_story_analytics() from public, anon, authenticated;
grant execute on function public.get_admin_story_analytics() to service_role;


-- Both reports use the same input projections. The old all/cancellations API
-- also calls this helper, so there is only one definition of each field.
create or replace function private.admin_report_inputs(p_panel text, p_skip text[])
returns json language sql stable security invoker set search_path = pg_catalog, public
as $$
select json_build_object(
    'subscribers', case when not ('subscribers' = any(p_skip)) then (
        select coalesce(json_agg(r), '[]'::json) from (
            select subscriber_id,email,first_name,last_name,display_name,created_at
            from public.subscribers
            where p_panel <> 'cancellations' or exists (select 1 from public.subscription_cancellation_feedback f where f.subscriber_id = subscribers.subscriber_id)
            order by updated_at desc, subscriber_id
        ) r) else '[]'::json end,
    'subscriptions', case when not ('subscriptions' = any(p_skip)) and p_panel <> 'cancellations' then (
        select coalesce(json_agg(r), '[]'::json) from (
            select subscription_id,subscriber_id,tier_code,provider,source_system,status,subscribed_at,next_renewal_at,cancelled_at,billing_amount_zar,billing_period_months,provider_payment_id,provider_transaction_id,provider_token
            from public.subscriptions
            order by subscribed_at desc, subscription_id
        ) r) else '[]'::json end,
    'events', case when not ('events' = any(p_skip)) and p_panel in ('all', 'membership', 'revenue') then (
        select coalesce(json_agg(r), '[]'::json) from (
            select provider,event_type,event_status,received_at,provider_payment_id,provider_transaction_id,
            jsonb_strip_nulls(jsonb_build_object(
                'amount', fields.amount,
                'amount_gross', fields.amount_gross,
                'created_at', fields.created_at,
                'custom_str1', fields.custom_str1,
                'customer', jsonb_build_object(
                    'customer_code', fields.customer #> '{customer_code}',
                    'email', fields.customer #> '{email}',
                    'id', fields.customer #> '{id}'),
                'data', jsonb_build_object(
                    'amount', fields.data #> '{amount}',
                    'authorization', jsonb_build_object(
                        'authorization_code', fields.data #> '{authorization,authorization_code}'),
                    'customer', jsonb_build_object(
                        'customer_code', fields.data #> '{customer,customer_code}',
                        'email', fields.data #> '{customer,email}',
                        'id', fields.data #> '{customer,id}'),
                    'id', fields.data #> '{id}',
                    'metadata', jsonb_build_object(
                        'planSlug', fields.data #> '{metadata,planSlug}',
                        'plan_slug', fields.data #> '{metadata,plan_slug}',
                        'tier_code', fields.data #> '{metadata,tier_code}'),
                    'paidAt', fields.data #> '{paidAt}',
                    'paid_at', fields.data #> '{paid_at}',
                    'plan', jsonb_build_object(
                        'amount', fields.data #> '{plan,amount}',
                        'name', fields.data #> '{plan,name}',
                        'plan_code', fields.data #> '{plan,plan_code}'),
                    'reference', fields.data #> '{reference}',
                    'subscription', jsonb_build_object(
                        'subscription_code', fields.data #> '{subscription,subscription_code}'),
                    'subscription_code', fields.data #> '{subscription_code}',
                    'transaction', jsonb_build_object(
                        'authorization', jsonb_build_object(
                            'authorization_code', fields.data #> '{transaction,authorization,authorization_code}')),
                    'transaction_date', fields.data #> '{transaction_date}'),
                'email', fields.email,
                'item_name', fields.item_name,
                'm_payment_id', fields.m_payment_id,
                'paid_at', fields.paid_at,
                'pf_payment_id', fields.pf_payment_id,
                'planSlug', fields."planSlug",
                'plan_slug', fields.plan_slug,
                'reference', fields.reference,
                'tier_code', fields.tier_code,
                'token', fields.token)) as payload
            from public.subscription_events
            cross join lateral jsonb_to_record(case when jsonb_typeof(payload) = 'object' then payload else '{}'::jsonb end)
                as fields(amount jsonb, amount_gross jsonb, created_at jsonb, custom_str1 jsonb, customer jsonb, data jsonb, email jsonb, item_name jsonb, m_payment_id jsonb, paid_at jsonb, pf_payment_id jsonb, "planSlug" jsonb, plan_slug jsonb, reference jsonb, tier_code jsonb, token jsonb)
            where provider in ('paystack','payfast') and event_type in ('charge.success','subscription.create','subscription.disable','payfast_itn')
            order by received_at desc, event_id
        ) r) else '[]'::json end,
    'orders', case when not ('orders' = any(p_skip)) and p_panel in ('all','membership','revenue') then (
        select coalesce(json_agg(r), '[]'::json) from (
            select order_id,order_reference,product_name,total_price_zar,payment_status,provider,provider_transaction_id,created_at,paid_at
            from public.store_orders
            where payment_status = 'paid'
            order by paid_at desc nulls last, created_at desc, order_id
        ) r) else '[]'::json end,
    'tiers', case when not ('tiers' = any(p_skip)) then (
        select coalesce(json_agg(r), '[]'::json) from (
            select tier_code,display_name,price_zar,is_active
            from public.subscription_tiers
            order by display_name, tier_code
        ) r) else '[]'::json end,
    'recoveries', case when not ('recoveries' = any(p_skip)) and p_panel <> 'cancellations' then (
        select coalesce(json_agg(r), '[]'::json) from (
            select recovery_id,subscription_id,provider,provider_payment_id,first_failed_at,grace_ends_at,created_at,resolved_at,resolution
            from public.subscription_payment_recoveries
            order by created_at desc, recovery_id
        ) r) else '[]'::json end,
    'carts', case when not ('carts' = any(p_skip)) and p_panel in ('all','revenue','recoveries') then (
        select coalesce(json_agg(r), '[]'::json) from (
            select recovery_id,source_type,source_key,checkout_reference,provider,customer_email,customer_name,item_name,cart_total_zar,created_at,resolved_at,resolution
            from public.abandoned_cart_recoveries
            order by created_at desc, recovery_id
        ) r) else '[]'::json end,
    'feedback', case when not ('feedback' = any(p_skip)) and p_panel in ('all','cancellations') then (
        select coalesce(json_agg(r), '[]'::json) from (
            select feedback_id,subscriber_id,subscription_id,tier_code,provider,feedback_status,reason_code,note,cancelled_subscription_count,created_at
            from public.subscription_cancellation_feedback
            order by created_at desc, feedback_id
        ) r) else '[]'::json end
);
$$;
revoke all on function private.admin_report_inputs(text,text[]) from public, anon, authenticated;
grant execute on function private.admin_report_inputs(text,text[]) to service_role;

create or replace function public.get_admin_report_inputs(p_panel text)
returns json language sql stable security invoker set search_path = pg_catalog, public
as $$ select private.admin_report_inputs(p_panel, '{}'::text[]); $$;

-- Row identifiers and their visible tuple versions detect inserts, updates and
-- deletes. A STABLE function reads the signatures and payloads from one MVCC
-- snapshot. No triggers, report writes or locks are added to checkout.
create or replace function public.get_admin_financial_report_inputs(p_known_versions jsonb)
returns json language plpgsql stable security invoker set search_path = pg_catalog, public
as $$
declare versions jsonb; unchanged text[];
begin
    select jsonb_build_object(
        'subscribers', (select md5(coalesce(string_agg(concat_ws('/', subscriber_id, xmin, ctid), ',' order by subscriber_id), '')) from public.subscribers),
        'subscriptions', (select md5(coalesce(string_agg(concat_ws('/', subscription_id, xmin, ctid), ',' order by subscription_id), '')) from public.subscriptions),
        'events', (select md5(coalesce(string_agg(concat_ws('/', event_id, xmin, ctid), ',' order by event_id), '')) from public.subscription_events where provider in ('paystack','payfast') and event_type in ('charge.success','subscription.create','subscription.disable','payfast_itn')),
        'tiers', (select md5(coalesce(string_agg(concat_ws('/', tier_code, xmin, ctid), ',' order by tier_code), '')) from public.subscription_tiers),
        'recoveries', (select md5(coalesce(string_agg(concat_ws('/', recovery_id, xmin, ctid), ',' order by recovery_id), '')) from public.subscription_payment_recoveries),
        'orders', (select md5(coalesce(string_agg(concat_ws('/', order_id, xmin, ctid), ',' order by order_id), '')) from public.store_orders where payment_status = 'paid'),
        'carts', (select md5(coalesce(string_agg(concat_ws('/', recovery_id, xmin, ctid), ',' order by recovery_id), '')) from public.abandoned_cart_recoveries)
    ) into versions;
    select coalesce(array_agg(key), '{}'::text[]) into unchanged
    from jsonb_each_text(versions)
    where value = p_known_versions ->> key;
    return json_build_object('versions', versions, 'inputs', private.admin_report_inputs('revenue', unchanged));
end;
$$;
revoke all on function public.get_admin_financial_report_inputs(jsonb) from public, anon, authenticated;
grant execute on function public.get_admin_financial_report_inputs(jsonb) to service_role;

-- Group unpriced memberships with identical eligibility at as_of. Keep paid
-- subscriptions in their original report order so decimal MRR sums and their
-- final cent rounding match the detailed report.
create or replace function public.get_admin_financial_overview()
returns jsonb language sql stable security invoker set search_path = pg_catalog, public
as $$
with active as (
    select coalesce(subscriber_id, '00000000-0000-0000-0000-000000000000'::uuid) as subscriber_id,
        subscription_id, subscribed_at, tier_code, provider, source_system, status, next_renewal_at, cancelled_at,
        billing_amount_zar, billing_period_months,
        count(*) over plan_group as quantity, row_number() over plan_group as position
    from public.subscriptions
    where lower(status) = 'active' and lower(coalesce(source_system, '')) not in ('wordpress_pmpro','admin_override')
        and (cancelled_at is null or cancelled_at > now())
        and (next_renewal_at is null or next_renewal_at >= now())
    window plan_group as (partition by case when billing_amount_zar > 0 then subscription_id end, tier_code, provider, source_system, billing_amount_zar, billing_period_months,
        (next_renewal_at is null), coalesce(subscriber_id <> '00000000-0000-0000-0000-000000000000'::uuid, false))
)
select jsonb_build_object('as_of', now(), 'groups', coalesce(jsonb_agg(jsonb_build_object(
    'quantity', quantity, 'sample', jsonb_strip_nulls(to_jsonb(active) - array['quantity','position','subscription_id','subscribed_at']))
    order by subscribed_at desc, subscription_id), '[]'))
from active where position = 1;
$$;
revoke all on function public.get_admin_financial_overview() from public, anon, authenticated;
grant execute on function public.get_admin_financial_overview() to service_role;
