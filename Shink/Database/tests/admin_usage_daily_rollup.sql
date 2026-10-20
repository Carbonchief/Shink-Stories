-- Local-only regression fixture; never run this against a hosted database.
-- createdb shink_admin_analytics_regression
-- psql -X -v ON_ERROR_STOP=1 -d shink_admin_analytics_regression -f Shink/Database/tests/admin_usage_daily_rollup.sql
-- The minimal schema, role grants, functions and fixtures are rolled back.
\set ON_ERROR_STOP on
begin;
set local timezone = 'UTC';
set local statement_timeout = '30s';

do $$
begin
    if current_database() <> 'shink_admin_analytics_regression' then
        raise exception 'Use the empty local shink_admin_analytics_regression database';
    end if;
    if to_regclass('public.stories') is not null then
        raise exception 'This fixture requires an empty local database';
    end if;
    if not exists (select from pg_roles where rolname = 'anon') then create role anon; end if;
    if not exists (select from pg_roles where rolname = 'authenticated') then create role authenticated; end if;
    if not exists (select from pg_roles where rolname = 'service_role') then create role service_role; end if;
end;
$$;

create table public.subscribers (subscriber_id uuid primary key);
create table public.stories (slug text primary key, title text);
create table public.story_characters (slug text primary key, display_name text);
create table public.story_views (subscriber_id uuid, story_slug text, viewed_at timestamptz);
create table public.story_favorites (subscriber_id uuid, story_slug text, updated_at timestamptz, created_at timestamptz);
create table public.story_favourites (subscriber_id uuid, story_slug text, created_at timestamptz);
create table public.story_listen_events (
    subscriber_id uuid, story_slug text, session_id uuid, event_type text,
    listened_seconds numeric(10, 3), position_seconds numeric(10, 3),
    duration_seconds numeric(10, 3), occurred_at timestamptz, metadata jsonb, story_path text
);
create table public.character_audio_plays (subscriber_id uuid, character_slug text, occurred_at timestamptz);
create table public.resource_documents (resource_document_id uuid primary key, slug text, title text, file_name text);
create table public.resource_document_download_events (resource_document_id uuid, download_path text, downloaded_at timestamptz);
create table public.blog_posts (post_id uuid primary key, title text);
create table public.blog_visit_events (post_id uuid, post_slug text, visit_path text, visited_at timestamptz);

\ir ../migrations/20261014101341_optimize_admin_usage_analytics.sql
\ir ../migrations/20261014195150_admin_usage_memory_budget.sql
\ir ../migrations/20260506_admin_analytics_item_page_breakdowns.sql
\ir ../migrations/20260708_oortjies_click_analytics.sql
grant select on all tables in schema public to service_role;

create temporary table expected (phase text, timezone text, snapshot jsonb);
insert into expected select 'empty', 'UTC', public.get_admin_analytics_snapshot();

\ir ../migrations/20261020122224_admin_usage_daily_rollup.sql
do $$
declare actual jsonb := public.get_admin_analytics_snapshot();
begin
    if actual is distinct from (select snapshot from expected where phase = 'empty')
       or jsonb_array_length(actual->'daily_activity') <> 30
       or actual#>>'{story_summary,total_listen_events}' <> '0'
       or actual#>>'{story_summary,average_listened_seconds_per_session}' <> '0' then
        raise exception 'Empty analytics changed or omitted zero-valued dates';
    end if;
end;
$$;

insert into public.stories values ('alpha', 'Alpha'), ('beta', 'Beta'), ('empty-story', 'Empty story');
insert into public.story_characters values ('panda', 'Panda'), ('fallback-name', '');
insert into public.subscribers values ('00000000-0000-0000-0000-000000000001'), ('00000000-0000-0000-0000-000000000002');

-- One session spans the 30-day boundary and midnight, two subscribers and two
-- stories. Global session counts must not be summed from per-day/story counts.
-- Completion covers ended, JSON boolean, 95% progress, invalid JSON types, and
-- metadata that is NULL/missing. Nullable IDs test the previous SQL semantics.
insert into public.story_listen_events
select ('00000000-0000-0000-0000-' || lpad(subscriber::text, 12, '0'))::uuid,
       slug, ('10000000-0000-0000-0000-' || lpad(session::text, 12, '0'))::uuid,
       event_type, seconds, position, duration, current_date + age, metadata
from (values
    (1, 'alpha', 1, 'progress', 0.101, 1, 100, interval '-35 days', '{}'::jsonb),
    (1, 'alpha', 1, 'progress', 0.202, 1, 100, interval '-29 days -1 microsecond', '{}'),
    (1, 'alpha', 1, 'progress', 0.303, 1, 100, interval '-29 days', '{}'),
    (1, 'alpha', 1, 'progress', 0.404, 1, 100, interval '-1 microsecond', '{}'),
    (1, 'alpha', 1, 'progress', 0.505, 1, 100, interval '0 seconds', '{}'),
    (2, 'alpha', 1, 'progress', 0.606, 1, 100, interval '1 second', '{"is_completed":true}'),
    (1, 'beta', 1, 'ended', 0.707, null, null, interval '2 seconds', '{}'),
    (1, 'alpha', 2, 'progress', 0.808, 95, 100, interval '-1 day', '{}'),
    (1, 'beta', 3, 'progress', 0.909, 94.9, 100, interval '-1 day', '{"is_completed":"true"}'),
    (2, 'beta', 4, 'progress', 1.010, null, null, interval '-1 day', '{"is_completed":false}'),
    (2, 'beta', 5, 'progress', 1.111, null, null, interval '-2 days', null),
    (2, 'alpha', 6, 'progress', 1.212, null, null, interval '2 days', '{"is_completed":1}')
) as v(subscriber, slug, session, event_type, seconds, position, duration, age, metadata);
insert into public.story_listen_events values
    (null, 'alpha', null, 'progress', 0.123, null, null, current_date, '{}');

insert into public.story_views values
    ('00000000-0000-0000-0000-000000000001', 'alpha', current_date),
    ('00000000-0000-0000-0000-000000000001', 'alpha', current_date - interval '40 days');
insert into public.story_favorites values
    ('00000000-0000-0000-0000-000000000001', 'alpha', current_date, current_date - interval '40 days');
insert into public.story_favourites values
    ('00000000-0000-0000-0000-000000000001', 'alpha', current_date),
    ('00000000-0000-0000-0000-000000000002', '', current_date);
insert into public.character_audio_plays values
    ('00000000-0000-0000-0000-000000000001', 'panda', current_date);
insert into public.resource_documents values ('20000000-0000-0000-0000-000000000001', 'resource', 'Resource', 'resource.pdf');
insert into public.resource_document_download_events values ('20000000-0000-0000-0000-000000000001', '/resource', current_date);
insert into public.blog_posts values ('30000000-0000-0000-0000-000000000001', 'Post');
insert into public.blog_visit_events values ('30000000-0000-0000-0000-000000000001', 'post', '/blog/post', current_date);
insert into public.oortjies_click_events (subscriber_id, clicked_at) values ('00000000-0000-0000-0000-000000000001', current_date);

\ir ../migrations/20261014101341_optimize_admin_usage_analytics.sql
\ir ../migrations/20261014195150_admin_usage_memory_budget.sql
do $$
declare tz text;
begin
    foreach tz in array array['UTC', 'Africa/Johannesburg', 'America/New_York'] loop
        perform set_config('TimeZone', tz, true);
        insert into expected select 'boundaries', tz, public.get_admin_analytics_snapshot();
    end loop;
end;
$$;

\ir ../migrations/20261020122224_admin_usage_daily_rollup.sql
do $$
declare tz text; actual jsonb;
begin
    foreach tz in array array['UTC', 'Africa/Johannesburg', 'America/New_York'] loop
        perform set_config('TimeZone', tz, true);
        actual := public.get_admin_analytics_snapshot();
        if actual is distinct from (select snapshot from expected where phase = 'boundaries' and timezone = tz) then
            raise exception 'Snapshot contract changed in timezone %', tz;
        end if;
    end loop;
    perform set_config('TimeZone', 'UTC', true);
    actual := public.get_admin_analytics_snapshot();
    if actual#>>'{story_summary,total_listen_events}' <> '13'
       or actual#>>'{story_summary,total_listen_sessions}' <> '7'
       or actual#>>'{story_summary,unique_listeners}' <> '2'
       or actual#>>'{story_summary,total_listened_seconds}' <> '8.0'
       or actual#>>'{story_summary,average_listened_seconds_per_session}' <> '1.1'
       or actual#>>'{daily_activity,0,total_listen_sessions}' <> '1'
       or actual#>>'{daily_activity,0,total_listened_seconds}' <> '1.9'
       or actual#>>'{daily_activity,1,total_listen_sessions}' <> '4'
       or actual#>>'{daily_activity,1,total_listened_seconds}' <> '3.1'
       or actual#>>'{daily_activity,29,total_listened_seconds}' <> '0.3' then
        raise exception 'Session, boundary or numeric rounding metrics changed: %', actual->'story_summary';
    end if;
    if actual#>>'{top_completion_stories,0,story_slug}' <> 'alpha'
       or actual#>>'{top_completion_stories,0,total_listen_sessions}' <> '3'
       or actual#>>'{top_completion_stories,0,completed_listen_sessions}' <> '2'
       or actual#>>'{top_completion_stories,0,completion_rate_percent}' <> '66.7'
       or actual#>>'{top_completion_stories,1,completed_listen_sessions}' <> '1'
       or actual#>>'{top_completion_stories,1,completion_rate_percent}' <> '25.0'
       or actual#>>'{story_summary,total_favorites}' <> '3'
       or actual#>>'{story_analytics,0,total_favorites}' <> '1'
       or actual#>>'{story_analytics,0,total_listen_sessions}' <> '4' then
        raise exception 'Completion, session de-duplication or legacy favorites changed';
    end if;
    if actual#>>'{resource_download_summary,items,0,total_downloads}' <> '1'
       or actual#>>'{blog_visit_summary,pages,0,total_visits}' <> '1'
       or actual#>>'{award_summary,oortjies_clicked}' <> '1' then
        raise exception 'Wrapper analytics disappeared';
    end if;
    if (select prosecdef from pg_proc where oid = 'public.get_admin_analytics_snapshot_base()'::regprocedure)
       or (select proparallel <> 'u' from pg_proc where oid = 'public.get_admin_analytics_snapshot_base()'::regprocedure)
       or has_function_privilege('anon', 'public.get_admin_analytics_snapshot_base()', 'EXECUTE')
       or has_function_privilege('authenticated', 'public.get_admin_analytics_snapshot_base()', 'EXECUTE')
       or not has_function_privilege('service_role', 'public.get_admin_analytics_snapshot_base()', 'EXECUTE')
       or not (select proconfig @> array['work_mem=16MB', 'search_path=pg_catalog, public']
               from pg_proc where oid = 'public.get_admin_analytics_snapshot_base()'::regprocedure) then
        raise exception 'Admin analytics invoker security or server-only grants changed';
    end if;
    set local role service_role;
    perform public.get_admin_analytics_snapshot();
    reset role;
end;
$$;

-- Four times the current production event count, with representative row width,
-- many events per session and sessions spanning dates. Capture the entire RPC
-- JSON, including the untouched wrappers. Call through the server's role and
-- keep its eight-second query budget for the optimized function.
insert into public.story_listen_events
select ('00000000-0000-0000-0000-' || lpad((1 + n % 2)::text, 12, '0'))::uuid,
       case when n % 3 = 0 then 'alpha' else 'beta' end,
       ('40000000-0000-0000-0000-' || lpad((n % 51000)::text, 12, '0'))::uuid,
       case when n % 19 = 0 then 'ended' else 'progress' end,
       0.123, n % 100, 100, current_date - (n % 120) * interval '1 day' + (n % 86400) * interval '1 second',
       jsonb_build_object('source', 'mobile', 'platform', 'Android', 'app_version', '1.0', 'os_version', '15', 'is_completed', n % 39 = 0),
       '/luister/a-representative-story-path'
from generate_series(1, 3080000) n;
insert into public.story_views
select ('00000000-0000-0000-0000-' || lpad((1 + n % 2)::text, 12, '0'))::uuid,
       case when n % 3 = 0 then 'alpha' else 'beta' end,
       current_date - (n % 120) * interval '1 day'
from generate_series(1, 93307) n;
analyze public.story_listen_events;
analyze public.story_views;

\ir ../migrations/20261014101341_optimize_admin_usage_analytics.sql
\ir ../migrations/20261014195150_admin_usage_memory_budget.sql
\timing on
insert into expected select 'large', 'UTC', public.get_admin_analytics_snapshot();
\ir ../migrations/20261020122224_admin_usage_daily_rollup.sql
set local statement_timeout = '8s';
do $$
declare query_plan jsonb; query_text text; actual jsonb;
begin
    set local role service_role;
    actual := public.get_admin_analytics_snapshot();
    reset role;
    if actual is distinct from (select snapshot from expected where phase = 'large') then
        raise exception 'Production-sized snapshot differs from the previous RPC';
    end if;
    select prosrc into query_text from pg_proc
    where oid = 'public.get_admin_analytics_snapshot_base()'::regprocedure;
    -- EXPLAIN the RETURN expression because EXPLAIN SELECT function() does not
    -- expose its nested plan. The migration keeps this one read-only expression.
    query_text := regexp_replace(query_text,
        '^\s*begin\s*return\s*\((.*)\);\s*end;\s*$', '\1', 's');
    execute 'explain (format json) ' || query_text into query_plan;
    if jsonb_array_length(jsonb_path_query_array(query_plan,
        'strict $.** ? (@."Relation Name" == "story_listen_events")')) <> 1 then
        raise exception 'Usage analytics must scan listening history only once';
    end if;
end;
$$;
\timing off
rollback;
select 'Empty, boundary, completion, rounding, timezone, wrapper, permission and 3.08M-row parity checks passed; all fixtures rolled back.' as verification;
