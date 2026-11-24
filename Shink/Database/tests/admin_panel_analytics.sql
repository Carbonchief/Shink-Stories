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
-- The production schema already indexes these latest-activity lookups.
create index on public.story_listen_events (story_slug, occurred_at desc);
create index on public.story_views (story_slug, viewed_at desc);
\ir ../migrations/20261124070312_optimize_admin_usage_report.sql
\ir ../migrations/20261124073600_roll_up_admin_usage_by_day.sql

insert into public.store_orders(payment_status,total_price_zar) values ('paid',149), ('pending',149);
insert into public.auth_sessions select now() - n * interval '1 minute' from generate_series(1,1501) n;
insert into public.subscription_events(event_id, provider, event_type, received_at, payload)
select n, 'paystack', 'charge.success', now(), '{"data":{"amount":7900,"metadata":{"tier_code":"monthly"}}}'::jsonb
from generate_series(1,1501) n;

do $$
declare
    baseline jsonb := public.get_admin_analytics_snapshot();
    usage jsonb := public.get_admin_usage_analytics();
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
        and proname in ('get_admin_usage_analytics','get_admin_listening_analytics','get_admin_award_analytics','get_admin_resource_analytics',
            'get_admin_blog_analytics','get_admin_engagement_periods','get_admin_report_inputs')
        and (prosecdef or has_function_privilege('anon', oid, 'execute') or has_function_privilege('authenticated', oid, 'execute')
            or not has_function_privilege('service_role', oid, 'execute'))
    ) then raise exception 'Unexpected admin report privileges'; end if;
end;
$$;

-- Time the old and new Usage reads on identical data. Compare the complete
-- response as the application role, including its view of the private totals.
set local role service_role;
\timing on
create temporary table usage_reference as select public.get_admin_listening_analytics(false) as report;
set local statement_timeout = '8s';
do $$
begin
    if public.get_admin_usage_analytics() is distinct from (select report from usage_reference) then
        raise exception 'Service role Usage report differs from event history';
    end if;
end;
$$;
\timing off
reset role;
set local statement_timeout = '45s';

-- A warm local database can still finish a history scan inside eight seconds.
-- In UTC, raw listens should only be visited to look up latest timestamps.
do $$
declare query_text text; plan jsonb;
begin
    select prosrc into query_text from pg_proc where oid = 'public.get_admin_usage_analytics()'::regprocedure;
    query_text := regexp_replace(query_text, '^\s*begin\s*return\s*\((.*)\);\s*end;\s*$', '\1', 's');
    execute 'explain (analyze, timing off, format json) ' || query_text into plan;
    if jsonb_path_exists(plan, 'strict $.** ? (@."Relation Name" == "story_listen_events" && @."Actual Loops" > 0 && @."Actual Rows" > 1)') then
        raise exception 'UTC Usage report read raw listening history for its daily chart';
    end if;
end;
$$;

-- Compare every field after bulk writes, a key change and removal of the last
-- completed event in a session. This also exercises nullable legacy identities.
create function pg_temp.assert_usage_parity() returns void language plpgsql as $$
declare expected jsonb := public.get_admin_listening_analytics(false);
begin
    if public.get_admin_usage_analytics() is distinct from expected then
        raise exception 'Usage report differs from event history';
    end if;
end;
$$;

truncate public.story_listen_events;
select pg_temp.assert_usage_parity();
grant insert on public.story_listen_events to authenticated;
set local role authenticated;
insert into public.story_listen_events(story_slug, session_id, subscriber_id, occurred_at, event_type, listened_seconds, metadata)
select 'alpha', case when n < 3 then '10000000-0000-0000-0000-000000000001'::uuid end,
    null, current_date - interval '3 days' + n * interval '12 hours' - interval '30 minutes',
    case when n = 1 then 'ended' else 'progress' end, 3.12, '{}'::jsonb from generate_series(1,4) n;
reset role;
do $$
begin
    if has_table_privilege('authenticated', 'private.admin_listen_totals', 'select')
       or has_table_privilege('anon', 'private.admin_listen_totals', 'select')
       or has_table_privilege('service_role', 'private.admin_listen_totals', 'update') then
        raise exception 'Unexpected private listening summary privileges';
    end if;
end;
$$;
select pg_temp.assert_usage_parity();
update public.story_listen_events set story_slug = 'beta', metadata = '{"is_completed":true}', listened_seconds = 4.56
where event_type = 'progress';
select pg_temp.assert_usage_parity();
update public.story_listen_events set occurred_at = occurred_at - interval '3 days' where metadata -> 'is_completed' = 'true'::jsonb;
select pg_temp.assert_usage_parity();
delete from public.story_listen_events where event_type = 'ended';
select pg_temp.assert_usage_parity();
do $$
declare tz text;
begin
    foreach tz in array array['UTC', 'Africa/Johannesburg', 'America/New_York'] loop
        perform set_config('TimeZone', tz, true);
        perform pg_temp.assert_usage_parity();
    end loop;
end;
$$;
delete from public.story_listen_events;
select pg_temp.assert_usage_parity();
