-- Local-only fixture. Never run against a hosted database.
-- psql -X -v ON_ERROR_STOP=1 -d shink_story_progress_regression -f Shink/Database/tests/story_progress_snapshot.sql
\set ON_ERROR_STOP on
begin;
set local timezone = 'UTC';
set local statement_timeout = '30s';
do $$
begin
    if current_database() <> 'shink_story_progress_regression' or
       to_regclass('public.story_listen_events') is not null then
        raise exception 'Use an empty local shink_story_progress_regression database';
    end if;
    if not exists (select from pg_roles where rolname = 'anon') then create role anon; end if;
    if not exists (select from pg_roles where rolname = 'authenticated') then create role authenticated; end if;
    if not exists (select from pg_roles where rolname = 'service_role') then create role service_role; end if;
end;
$$;

create table public.story_listen_events (
    story_listen_event_id bigint generated always as identity primary key,
    subscriber_id uuid, story_slug text, story_path text, session_id uuid,
    event_type text, listened_seconds numeric(10,3), position_seconds numeric(10,3),
    duration_seconds numeric(10,3), occurred_at timestamptz, metadata jsonb
);
create index on public.story_listen_events (subscriber_id, story_slug, occurred_at desc);
alter table public.story_listen_events enable row level security;
create policy test_server_reads on public.story_listen_events for select to service_role using (true);
grant select on public.story_listen_events to service_role;

\ir ../migrations/20261023070629_story_progress_snapshot.sql

-- Two sessions of alpha: the older one crossed 95%, then the listener restarted.
-- A story can have completed sessions while its latest position is incomplete.
-- Beta has an ended event with zero seconds, gamma a string completion flag.
-- Other cases cover missing duration, null position, empty sessions, a second
-- subscriber, equal timestamps, invalid metadata types, and late/offline writes.
insert into public.story_listen_events
    (subscriber_id, story_slug, story_path, session_id, event_type,
     listened_seconds, position_seconds, duration_seconds, occurred_at, metadata)
select ('00000000-0000-0000-0000-' || lpad(subscriber::text, 12, '0'))::uuid,
       slug, path,
       case when session is null then null else ('10000000-0000-0000-0000-' || lpad(session::text, 12, '0'))::uuid end,
       event_type, seconds, position, duration, '2026-10-01T00:00:00Z'::timestamptz + age, metadata
from (values
    (1, 'alpha', '/luister/alpha', 1, 'progress', 40.111, 40, 100, interval '1 second', '{"source":"luister"}'::jsonb),
    (1, 'alpha', '/luister/alpha', 1, 'pause', 54.889, 95, null, interval '2 seconds', '{}'),
    (1, 'alpha', '/luister/alpha', 2, 'progress', 10.123, 10.123, null, interval '3 seconds', '{"source":"luister"}'),
    (1, 'beta', '/gratis/beta', 3, 'ended', 0, 0, 100, interval '4 seconds', '{"is_completed":false}'),
    (1, 'gamma', '//bad', 4, 'progress', 12, null, 0, interval '5 seconds', '{"source":" GRATIS ","is_completed":" TrUe "}'),
    (1, 'invalid', '/luister/invalid', 5, 'progress', 5, null, null, interval '6 seconds', '{"source":1,"is_completed":1}'),
    (1, 'invalid', '/luister/invalid', 5, 'progress', 0, 0, null, interval '7 seconds', '{"is_completed":"yes"}'),
    (1, 'invalid', '/luister/invalid', 5, 'progress', 0, null, null, interval '7 seconds', null),
    (1, 'delta', '/luister/delta', null, 'ended', 1, 1, 100, interval '8 seconds', '{"is_completed":true}'),
    (1, 'epsilon', '/luister/epsilon', 6, 'progress', 12, 90, 100, interval '10 seconds', '{}'),
    (1, 'epsilon', '/luister/epsilon', 6, 'pause', 6, 96, null, interval '11 seconds', '{}'),
    (1, 'zeta', '/luister/zeta', 7, 'progress', 12, 96, 100, interval '12 seconds', '{}'),
    (1, 'zeta', '/luister/zeta', 7, 'pause', 1, 10, null, interval '13 seconds', '{}'),
    (1, 'alpha', '/luister/alpha', 1, 'progress', 1, 50, 100, interval '1.5 seconds', '{}'),
    (2, 'alpha', '/luister/alpha', 1, 'ended', 999, 999, 999, interval '14 seconds', '{}'),
    (1, '   ', '/luister/blank', 8, 'progress', 999, 999, 999, interval '15 seconds', '{}')
) as v(subscriber, slug, path, session, event_type, seconds, position, duration, age, metadata);

do $$
declare
    snapshot jsonb := public.get_story_progress_snapshot('00000000-0000-0000-0000-000000000001');
    progress jsonb;
begin
    if jsonb_array_length(snapshot) <> 7 then raise exception 'Expected seven stories: %', snapshot; end if;
    select x into progress from jsonb_array_elements(snapshot) x where x->>'story_slug' = 'alpha';
    if progress->>'total_listened_seconds' <> '106.123' or progress->>'listen_count' <> '2' or
       progress->>'completed_listen_count' <> '1' or progress->>'is_completed' <> 'false' or
       progress->>'position_seconds' <> '10.123' or progress->>'duration_seconds' <> '100.000' then
        raise exception 'Repeated listens, latest position or fallback duration changed: %', progress;
    end if;
    select x into progress from jsonb_array_elements(snapshot) x where x->>'story_slug' = 'beta';
    if progress->>'listen_count' <> '0' or progress->>'completed_listen_count' <> '1' or
       progress->>'is_completed' <> 'true' then raise exception 'Zero-second ended event lost: %', progress; end if;
    select x into progress from jsonb_array_elements(snapshot) x where x->>'story_slug' = 'gamma';
    if progress->>'completed_listen_count' <> '1' or progress->>'is_completed' <> 'true' then
        raise exception 'String completion flag changed: %', progress; end if;
    select x into progress from jsonb_array_elements(snapshot) x where x->>'story_slug' = 'invalid';
    if progress->>'is_completed' <> 'false' or progress->>'position_seconds' is not null then
        raise exception 'Invalid flags or equal-time latest position changed: %', progress; end if;
    select x into progress from jsonb_array_elements(snapshot) x where x->>'story_slug' = 'delta';
    if progress->>'listen_count' <> '0' or progress->>'completed_listen_count' <> '0' or
       progress->>'is_completed' <> 'true' then raise exception 'Null session semantics changed: %', progress; end if;
    select x into progress from jsonb_array_elements(snapshot) x where x->>'story_slug' = 'epsilon';
    if progress->>'is_completed' <> 'true' or progress->>'completed_listen_count' <> '1' then
        raise exception '95%% fallback duration completion changed: %', progress; end if;
    select x into progress from jsonb_array_elements(snapshot) x where x->>'story_slug' = 'zeta';
    if progress->>'is_completed' <> 'false' or progress->>'completed_listen_count' <> '0' then
        raise exception 'Latest session position changed: %', progress; end if;
    if public.get_story_progress_snapshot('00000000-0000-0000-0000-000000000003') <> '[]'::jsonb then
        raise exception 'An empty subscriber must receive an empty array'; end if;
    if has_function_privilege('anon','public.get_story_progress_snapshot(uuid)','execute') or
       has_function_privilege('authenticated','public.get_story_progress_snapshot(uuid)','execute') or
       not has_function_privilege('service_role','public.get_story_progress_snapshot(uuid)','execute') or
       (select prosecdef from pg_proc where oid='public.get_story_progress_snapshot(uuid)'::regprocedure) then
        raise exception 'Snapshot must be service-role-only and security invoker'; end if;
end;
$$;

-- Optional large, deliberately messy history for SQL/.NET parity and volume.
-- Enable with psql -v scale=1. Keep this out of the checked-in JSON fixture.
\if :{?scale}
insert into public.story_listen_events
    (subscriber_id, story_slug, story_path, session_id, event_type,
     listened_seconds, position_seconds, duration_seconds, occurred_at, metadata)
select '00000000-0000-0000-0000-000000000004'::uuid,
       case when i%5=0 then E' \tSTORY-' || (i%160) || U&'\00A0' else 'story-' || (i%160) end,
       case when i%7=0 then '//invalid' when i%9=0 then '/gratis/story-' || (i%160) else '/luister/story-' || (i%160) end,
       case when i%17=0 then null when i%19=0 then '00000000-0000-0000-0000-000000000000'::uuid
            else ('10000000-0000-0000-0000-' || lpad((i/97)::text,12,'0'))::uuid end,
       case when i%41=0 then 'ended' else 'progress' end,
       case when i%31=0 then 0 else (i%12345)/1000.0 end,
       case when i%3=0 then null else (i%301)::numeric end,
       case when i%7=0 then null else 300 end,
       '2026-10-01T00:00:00Z'::timestamptz + (i%1234)*interval '1 second',
       case when i%13=0 then null when i%29=0 then '{"is_completed":1}'::jsonb
            when i%23=0 then '{"is_completed":" TrUe ","source":"gratis"}'::jsonb
            else jsonb_build_object('source','luister','is_completed',i%37=0) end
from generate_series(1,30000) i;
do $$
declare
    snapshot jsonb := public.get_story_progress_snapshot('00000000-0000-0000-0000-000000000004');
    raw_bytes bigint;
begin
    select sum(octet_length(to_jsonb(e)::text)) into raw_bytes
    from public.story_listen_events e where subscriber_id='00000000-0000-0000-0000-000000000004';
    if jsonb_array_length(snapshot) <> 160 or octet_length(snapshot::text) >= raw_bytes * 0.02 then
        raise exception 'Progress must remain complete and compact for a large history'; end if;
end;
$$;
\set parity_subscriber '00000000-0000-0000-0000-000000000004'
\else
\set parity_subscriber '00000000-0000-0000-0000-000000000001'
\endif

-- Return both contracts to the optional .NET parity test, in one transaction.
-- Run with -qAt to capture the single JSON result into SHINK_PROGRESS_PARITY_JSON.
select jsonb_build_object(
    'events', (select jsonb_agg(to_jsonb(e) order by occurred_at desc, story_listen_event_id desc)
               from public.story_listen_events e where subscriber_id=:'parity_subscriber'),
    'snapshot', public.get_story_progress_snapshot(:'parity_subscriber'));

set local role service_role;
do $$
begin
    if jsonb_array_length(public.get_story_progress_snapshot('00000000-0000-0000-0000-000000000001')) <> 7 then
        raise exception 'The server role cannot read its progress snapshot'; end if;
end;
$$;
reset role;
rollback;
