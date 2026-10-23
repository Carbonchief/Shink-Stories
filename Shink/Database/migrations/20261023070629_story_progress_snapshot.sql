-- Keep raw listening events for analytics; transfer only per-story progress to
-- the application. This is read-only and uses the caller's existing RLS rights.
create or replace function public.get_story_progress_snapshot(p_subscriber_id uuid)
returns jsonb
language sql
stable
security invoker
set search_path = ''
as $function$
with whitespace as (
    -- Match String.Trim/IsNullOrWhiteSpace for legacy data as well as API slugs.
    select U&'\0009\000A\000B\000C\000D\0020\0085\00A0\1680\2000\2001\2002\2003\2004\2005\2006\2007\2008\2009\200A\2028\2029\202F\205F\3000' as chars
), events as materialized (
    select e.*,
           lower(btrim(e.story_slug, whitespace.chars)) as story_key,
           (lower(e.event_type) = 'ended' or
            (jsonb_typeof(e.metadata -> 'is_completed') in ('boolean', 'string') and
             lower(btrim(e.metadata ->> 'is_completed', whitespace.chars)) = 'true'))
               is true as completed_from_event
    from public.story_listen_events e
    cross join whitespace
    where e.subscriber_id = p_subscriber_id
      and nullif(btrim(e.story_slug, whitespace.chars), '') is not null
), latest as (
    select distinct on (story_key)
           story_key, story_path, position_seconds, occurred_at, metadata
    from events
    order by story_key, occurred_at desc, story_listen_event_id desc
), story_totals as (
    select story_key,
           round(sum(greatest(listened_seconds, 0)), 3) as total_listened_seconds,
           bool_or(completed_from_event) as completed_from_event,
           (array_agg(duration_seconds order by occurred_at desc, story_listen_event_id desc)
               filter (where duration_seconds > 0))[1] as duration_seconds
    from events
    group by story_key
), sessions as (
    select story_key, session_id,
           bool_or(listened_seconds > 0) as has_listened,
           bool_or(completed_from_event) as completed_from_event,
           (array_agg(position_seconds order by occurred_at desc, story_listen_event_id desc))[1]
               as position_seconds,
           (array_agg(duration_seconds order by occurred_at desc, story_listen_event_id desc)
               filter (where duration_seconds > 0))[1] as duration_seconds
    from events
    group by story_key, session_id
), session_counts as (
    select story_key,
           count(*) filter (where has_listened) as listen_count,
           count(*) filter (where completed_from_event or
               (position_seconds > 0 and duration_seconds > 0 and
                position_seconds >= duration_seconds * 0.95)) as completed_listen_count
    from sessions
    where session_id is not null
      and session_id <> '00000000-0000-0000-0000-000000000000'::uuid
    group by story_key
)
select coalesce(jsonb_agg(jsonb_build_object(
    'story_slug', latest.story_key,
    'story_path', latest.story_path,
    'last_listened_at', latest.occurred_at,
    'total_listened_seconds', totals.total_listened_seconds,
    'listen_count', coalesce(counts.listen_count, 0),
    'completed_listen_count', coalesce(counts.completed_listen_count, 0),
    'position_seconds', latest.position_seconds,
    'duration_seconds', totals.duration_seconds,
    'is_completed', totals.completed_from_event or
        coalesce(latest.position_seconds > 0 and totals.duration_seconds > 0 and
                 latest.position_seconds >= totals.duration_seconds * 0.95, false),
    'metadata', jsonb_build_object('source', latest.metadata -> 'source')
) order by latest.occurred_at desc, latest.story_key), '[]'::jsonb)
from latest
join story_totals totals using (story_key)
left join session_counts counts using (story_key);
$function$;

-- The application resolves subscriber identity from its authenticated session.
-- Public clients must never be able to query arbitrary subscriber UUIDs.
revoke all on function public.get_story_progress_snapshot(uuid) from public, anon, authenticated;
grant execute on function public.get_story_progress_snapshot(uuid) to service_role;

comment on function public.get_story_progress_snapshot(uuid) is
    'Server-only listening progress, one row per story without exporting raw event history.';

notify pgrst, 'reload schema';
