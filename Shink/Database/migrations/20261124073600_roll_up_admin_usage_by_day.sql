-- Put the UTC reporting day in the existing listening summary. Each event still
-- updates a single counter row. All-time reports combine sessions across days.
do $migration$
begin
    lock table public.story_listen_events in share row exclusive mode;
    truncate private.admin_listen_totals;
    alter table private.admin_listen_totals add column if not exists activity_day_utc date not null;
    alter table private.admin_listen_totals drop constraint if exists admin_listen_totals_story_slug_session_id_subscriber_id_key;
    if not exists (
        select from pg_constraint where conrelid = 'private.admin_listen_totals'::regclass
        and conname = 'admin_listen_totals_daily_key'
    ) then
        alter table private.admin_listen_totals add constraint admin_listen_totals_daily_key
            unique nulls not distinct (activity_day_utc, story_slug, session_id, subscriber_id);
    end if;

    create or replace function private.maintain_admin_listen_totals()
    returns trigger language plpgsql security definer set search_path = pg_catalog, pg_temp
    as $trigger$
    begin
        if tg_op = 'TRUNCATE' then
            truncate private.admin_listen_totals;
            return null;
        end if;
        if tg_op in ('DELETE', 'UPDATE') then
            merge into private.admin_listen_totals r
            using (
                select (occurred_at at time zone 'UTC')::date as activity_day_utc, story_slug, session_id, subscriber_id,
                    count(*) as events, sum(listened_seconds) as seconds,
                    count(*) filter (where private.admin_listen_completed(event_type, metadata, position_seconds, duration_seconds)) as completed_events
                from old_events group by activity_day_utc, story_slug, session_id, subscriber_id
            ) d on r.activity_day_utc = d.activity_day_utc and r.story_slug = d.story_slug and r.session_id is not distinct from d.session_id
                and r.subscriber_id is not distinct from d.subscriber_id
            when matched and r.events = d.events then delete
            when matched then update set events = r.events - d.events, seconds = r.seconds - d.seconds,
                completed_events = r.completed_events - d.completed_events;
        end if;
        if tg_op in ('INSERT', 'UPDATE') then
            insert into private.admin_listen_totals as r (activity_day_utc, story_slug, session_id, subscriber_id, events, seconds, completed_events)
            select (occurred_at at time zone 'UTC')::date as activity_day_utc, story_slug, session_id, subscriber_id, count(*), sum(listened_seconds),
                count(*) filter (where private.admin_listen_completed(event_type, metadata, position_seconds, duration_seconds))
            from new_events group by activity_day_utc, story_slug, session_id, subscriber_id
            order by activity_day_utc, story_slug, session_id, subscriber_id
            on conflict (activity_day_utc, story_slug, session_id, subscriber_id) do update
            set events = r.events + excluded.events, seconds = r.seconds + excluded.seconds,
                completed_events = r.completed_events + excluded.completed_events;
        end if;
        return null;
    end;
    $trigger$;

    insert into private.admin_listen_totals (activity_day_utc, story_slug, session_id, subscriber_id, events, seconds, completed_events)
    select (occurred_at at time zone 'UTC')::date as activity_day_utc, story_slug, session_id, subscriber_id,
        count(*), sum(listened_seconds),
        count(*) filter (where private.admin_listen_completed(event_type, metadata, position_seconds, duration_seconds))
    from public.story_listen_events group by activity_day_utc, story_slug, session_id, subscriber_id;
end;
$migration$;
analyze private.admin_listen_totals;

create or replace function public.get_admin_usage_analytics()
returns jsonb
language plpgsql stable security invoker
set search_path = pg_catalog, public
set work_mem = '16MB'
as $$
begin
    return (
        with sessions as materialized (
            select story_slug, session_id, sum(seconds) as seconds, bool_or(completed_events > 0) as is_completed
            from private.admin_listen_totals group by story_slug, session_id
        ), listens as (
            select coalesce(sum(events), 0) as total_listen_events, count(distinct subscriber_id) as unique_listeners,
                count(distinct story_slug) as unique_listened_stories, coalesce(round(sum(seconds), 1), 0) as total_listened_seconds,
                (select max(occurred_at) from public.story_listen_events) as last_listen_at
            from private.admin_listen_totals
        ), session_summary as (
            select count(*) as total_listen_sessions, coalesce(round(avg(seconds), 1), 0) as average_listened_seconds_per_session
            from (select session_id, sum(seconds) as seconds from sessions group by session_id) global_sessions
        ), views as (
            select count(*) as total_views, count(distinct subscriber_id) as unique_viewers,
                count(distinct story_slug) as unique_viewed_stories, max(viewed_at) as last_view_at
            from public.story_views
        ), favorite_events as materialized (
            select story_slug, subscriber_id, coalesce(updated_at, created_at) as favorited_at from public.story_favorites
            union all
            select story_slug, subscriber_id, created_at from public.story_favourites
        ), favorites as (
            select count(*) as total_favorites, count(distinct subscriber_id) as unique_favoriters,
                max(favorited_at) as last_favorite_at
            from favorite_events
        ), daily_listens as (
            -- The Data API reports in UTC. Read exact day/session totals there.
            -- A caller with another timezone needs the original event boundaries.
            select activity_day_utc as activity_date, count(distinct session_id) as sessions, round(sum(seconds), 1) as seconds
            from private.admin_listen_totals
            where current_setting('TimeZone') in ('UTC', 'Etc/UTC', 'GMT', 'Etc/GMT')
              and activity_day_utc between current_date - 29 and current_date
            group by activity_day_utc
            union all
            select occurred_at::date as activity_date, count(distinct session_id) as sessions, round(sum(listened_seconds), 1) as seconds
            from public.story_listen_events
            where current_setting('TimeZone') not in ('UTC', 'Etc/UTC', 'GMT', 'Etc/GMT')
              and occurred_at >= current_date - 29 and occurred_at < current_date + 1
            group by 1
        ), daily_views as (
            select viewed_at::date as activity_date, count(*) as views
            from public.story_views where viewed_at >= current_date - 29 group by 1
        ), daily_favorites as (
            select favorited_at::date as activity_date, count(*) as favorites
            from favorite_events where favorited_at >= current_date - 29 group by 1
        ), daily_activity as (
            select activity_date, coalesce(views, 0) as total_views, coalesce(sessions, 0) as total_listen_sessions,
                coalesce(seconds, 0) as total_listened_seconds, coalesce(favorites, 0) as total_favorites
            from (select current_date - n as activity_date from generate_series(0, 29) n) days
            left join daily_listens using (activity_date)
            left join daily_views using (activity_date)
            left join daily_favorites using (activity_date)
        ), story_listens as (
            select story_slug, count(session_id) as total_listen_sessions,
                count(session_id) filter (where is_completed) as completed_listen_sessions,
                coalesce(round(sum(seconds), 1), 0) as total_listened_seconds
            from sessions group by story_slug having count(session_id) > 0
        ), top_completion as (
            select l.story_slug, s.title, l.total_listen_sessions, l.completed_listen_sessions,
                round(l.completed_listen_sessions * 100.0 / l.total_listen_sessions, 1) as completion_rate_percent,
                l.total_listened_seconds, nullif(greatest(
                    (select max(e.occurred_at) from public.story_listen_events e where e.story_slug = l.story_slug),
                    (select max(v.viewed_at) from public.story_views v where v.story_slug = l.story_slug),
                    f.last_favorite_at), '-infinity'::timestamptz) as last_activity_at
            from story_listens l join public.stories s on s.slug = l.story_slug
            left join (select story_slug, max(favorited_at) as last_favorite_at from favorite_events
                where btrim(story_slug) <> '' group by story_slug) f using (story_slug)
            order by completion_rate_percent desc, total_listen_sessions desc, total_listened_seconds desc, last_activity_at desc, title
            limit 10
        ), character_summary as (
            select count(*) as total_audio_plays, count(distinct subscriber_id) as unique_subscribers,
                count(distinct character_slug) as unique_characters, max(occurred_at) as last_audio_play_at
            from public.character_audio_plays
        ), characters as (
            select c.slug as character_slug, coalesce(nullif(c.display_name, ''), initcap(replace(c.slug, '-', ' '))) as display_name,
                coalesce(p.total_audio_plays, 0) as total_audio_plays, coalesce(p.unique_subscribers, 0) as unique_subscribers,
                p.last_activity_at
            from public.story_characters c left join (
                select character_slug, count(*) as total_audio_plays, count(distinct subscriber_id) as unique_subscribers,
                    max(occurred_at) as last_activity_at from public.character_audio_plays group by character_slug
            ) p on p.character_slug = c.slug
        )
        select jsonb_build_object(
            'generated_at', now(),
            'story_summary', to_jsonb(views) || to_jsonb(listens) || to_jsonb(session_summary) || to_jsonb(favorites),
            'daily_activity', (select jsonb_agg(d order by activity_date desc) from daily_activity d),
            'top_completion_stories', (select coalesce(jsonb_agg(t order by completion_rate_percent desc, total_listen_sessions desc,
                total_listened_seconds desc, last_activity_at desc, title), '[]') from top_completion t),
            'character_summary', to_jsonb(character_summary),
            'top_characters', (select coalesce(jsonb_agg(c order by total_audio_plays desc, last_activity_at desc, display_name), '[]') from characters c)
        ) from views, listens, session_summary, favorites, character_summary
    );
end;
$$;

revoke all on function public.get_admin_usage_analytics() from public, anon, authenticated;
grant execute on function public.get_admin_usage_analytics() to service_role;
