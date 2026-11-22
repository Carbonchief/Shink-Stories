-- Included by admin_usage_daily_rollup.sql inside its transaction.
alter table public.subscribers
    add email text,
    add first_name text,
    add last_name text,
    add display_name text,
    add created_at timestamp with time zone,
    add updated_at timestamp with time zone;
create table public.subscriptions (
    subscription_id uuid,
    subscriber_id uuid,
    tier_code text,
    provider text,
    source_system text,
    status text,
    subscribed_at timestamp with time zone,
    next_renewal_at timestamp with time zone,
    cancelled_at timestamp with time zone,
    billing_amount_zar numeric(10,2),
    billing_period_months integer,
    provider_payment_id text,
    provider_transaction_id text,
    provider_token text
);
create table public.subscription_events (
    event_id bigint,
    provider text,
    event_type text,
    event_status text,
    received_at timestamp with time zone,
    provider_payment_id text,
    provider_transaction_id text,
    payload jsonb
);
create table public.store_orders (
    order_id uuid,
    order_reference text,
    product_name text,
    total_price_zar numeric(10,2),
    payment_status text,
    provider text,
    provider_transaction_id text,
    created_at timestamp with time zone,
    paid_at timestamp with time zone
);
create table public.subscription_tiers (
    tier_code text,
    display_name text,
    price_zar numeric(10,2),
    is_active boolean
);
create table public.subscription_payment_recoveries (
    recovery_id uuid,
    subscription_id uuid,
    provider text,
    provider_payment_id text,
    first_failed_at timestamp with time zone,
    grace_ends_at timestamp with time zone,
    created_at timestamp with time zone,
    resolved_at timestamp with time zone,
    resolution text
);
create table public.abandoned_cart_recoveries (
    recovery_id uuid,
    source_type text,
    source_key text,
    checkout_reference text,
    provider text,
    customer_email text,
    customer_name text,
    item_name text,
    cart_total_zar numeric(10,2),
    created_at timestamp with time zone,
    resolved_at timestamp with time zone,
    resolution text
);
create table public.subscription_cancellation_feedback (
    feedback_id uuid,
    subscriber_id uuid,
    subscription_id uuid,
    tier_code text,
    provider text,
    feedback_status text,
    reason_code text,
    note text,
    cancelled_subscription_count integer,
    created_at timestamp with time zone
);
create table public.auth_sessions (
    created_at timestamp with time zone
);

\ir ../migrations/20261121130000_admin_panel_analytics.sql

insert into public.store_orders(payment_status,total_price_zar) values ('paid',149), ('pending',149);
insert into public.auth_sessions select now() - n * interval '1 minute' from generate_series(1,1501) n;
insert into public.subscription_events(event_id, provider, event_type, received_at, payload)
select n, 'paystack', 'charge.success', now(), '{"data":{"amount":7900,"metadata":{"tier_code":"monthly"}}}'::jsonb
from generate_series(1,1501) n;

do $$
declare
    baseline jsonb := public.get_admin_analytics_snapshot();
    usage jsonb := public.get_admin_listening_analytics(false);
    stories jsonb := public.get_admin_listening_analytics(true);
    engagement jsonb := public.get_admin_engagement_periods(current_date, date_trunc('week',now()),
        date_trunc('month',now()), date_trunc('year',now()), now());
    inputs json := public.get_admin_report_inputs('revenue');
    all_time jsonb := engagement#>'{periods,4}';
    key text;
begin
    foreach key in array array['story_summary','daily_activity','top_completion_stories','character_summary','top_characters'] loop
        if usage->key is distinct from baseline->key then raise exception 'Usage parity failed for %', key; end if;
    end loop;
    if stories->'story_analytics' is distinct from baseline->'story_analytics'
       or public.get_admin_resource_analytics() is distinct from baseline->'resource_download_summary'
       or public.get_admin_blog_analytics() is distinct from baseline->'blog_visit_summary'
       or public.get_admin_award_analytics() is distinct from baseline->'award_summary' then
        raise exception 'Panel output differs from full analytics';
    end if;
    if jsonb_array_length(engagement->'periods') <> 5 or (all_time->>'logins')::int <> 1501
       or (all_time->>'views')::int <> (select count(*) from public.story_views)
       or (all_time->>'visits')::int <> (select count(distinct session_id) from public.story_listen_events
           where session_id <> '00000000-0000-0000-0000-000000000000')
       or (engagement#>>'{periods,0,visits}')::int <> (select count(*) from (
           select session_id from public.story_listen_events where session_id <> '00000000-0000-0000-0000-000000000000'
           group by session_id having min(occurred_at) between current_date and now()) sessions) then
        raise exception 'Engagement totals differ from full history';
    end if;
    if json_array_length(public.get_admin_report_inputs('membership')->'orders') <> 1
       or json_array_length(inputs->'events') <> 1501 or inputs#>>'{events,1500,payload,data,amount}' <> '7900'
       or json_array_length(public.get_admin_report_inputs('cancellations')->'events') <> 0 then
        raise exception 'Incomplete or unscoped subscriber report';
    end if;
    if exists (
        select 1 from pg_proc where pronamespace='public'::regnamespace
        and proname in ('get_admin_listening_analytics','get_admin_award_analytics','get_admin_resource_analytics',
            'get_admin_blog_analytics','get_admin_engagement_periods','get_admin_report_inputs')
        and (prosecdef or has_function_privilege('anon', oid, 'execute') or has_function_privilege('authenticated', oid, 'execute')
            or not has_function_privilege('service_role', oid, 'execute'))
    ) then raise exception 'Unexpected admin report privileges'; end if;
end;
$$;
