create or replace function public.get_admin_analytics_snapshot()
returns jsonb
language sql
stable
set search_path = pg_catalog, public
as $$
with favorite_events as (
    select
        f.subscriber_id,
        f.story_slug,
        coalesce(f.updated_at, f.created_at) as favorited_at
    from public.story_favorites as f

    union all

    select
        f.subscriber_id,
        f.story_slug,
        f.created_at as favorited_at
    from public.story_favourites as f
),
story_view_summary as (
    select
        count(*)::integer as total_views,
        count(distinct subscriber_id)::integer as unique_viewers,
        count(distinct story_slug)::integer as unique_viewed_stories,
        max(viewed_at) as last_view_at
    from public.story_views
),
story_listen_summary as (
    select
        count(*)::integer as total_listen_events,
        count(distinct subscriber_id)::integer as unique_listeners,
        count(distinct story_slug)::integer as unique_listened_stories,
        coalesce(round(sum(listened_seconds)::numeric, 1), 0)::numeric as total_listened_seconds,
        max(occurred_at) as last_listen_at
    from public.story_listen_events
),
story_listen_session_totals as (
    select
        session_id,
        sum(listened_seconds) as listened_seconds
    from public.story_listen_events
    group by session_id
),
story_listen_session_summary as (
    select
        count(*)::integer as total_listen_sessions,
        coalesce(round(avg(listened_seconds)::numeric, 1), 0)::numeric as average_listened_seconds_per_session
    from story_listen_session_totals
),
story_favorite_summary as (
    select
        count(*)::integer as total_favorites,
        count(distinct subscriber_id)::integer as unique_favoriters,
        max(favorited_at) as last_favorite_at
    from favorite_events
),
character_audio_summary as (
    select
        count(*)::integer as total_audio_plays,
        count(distinct subscriber_id)::integer as unique_subscribers,
        count(distinct character_slug)::integer as unique_characters,
        max(occurred_at) as last_audio_play_at
    from public.character_audio_plays
),
day_series as (
    select generate_series(current_date - interval '29 days', current_date, interval '1 day')::date as activity_date
),
daily_view_counts as (
    select
        date_trunc('day', viewed_at)::date as activity_date,
        count(*)::integer as total_views
    from public.story_views
    where viewed_at >= current_date - interval '29 days'
    group by 1
),
daily_listen_counts as (
    select
        date_trunc('day', occurred_at)::date as activity_date,
        count(distinct session_id)::integer as total_listen_sessions,
        coalesce(round(sum(listened_seconds)::numeric, 1), 0)::numeric as total_listened_seconds
    from public.story_listen_events
    where occurred_at >= current_date - interval '29 days'
    group by 1
),
daily_favorite_counts as (
    select
        date_trunc('day', favorited_at)::date as activity_date,
        count(*)::integer as total_favorites
    from favorite_events
    where favorited_at >= current_date - interval '29 days'
    group by 1
),
daily_activity as (
    select
        day_series.activity_date,
        coalesce(daily_view_counts.total_views, 0)::integer as total_views,
        coalesce(daily_listen_counts.total_listen_sessions, 0)::integer as total_listen_sessions,
        coalesce(daily_listen_counts.total_listened_seconds, 0)::numeric as total_listened_seconds,
        coalesce(daily_favorite_counts.total_favorites, 0)::integer as total_favorites
    from day_series
    left join daily_view_counts
        on daily_view_counts.activity_date = day_series.activity_date
    left join daily_listen_counts
        on daily_listen_counts.activity_date = day_series.activity_date
    left join daily_favorite_counts
        on daily_favorite_counts.activity_date = day_series.activity_date
),
story_view_stats as (
    select
        story_slug,
        count(*)::integer as total_views,
        count(distinct subscriber_id)::integer as unique_viewers,
        max(viewed_at) as last_viewed_at
    from public.story_views
    group by story_slug
),
story_listen_stats as (
    select
        story_slug,
        count(*)::integer as total_listen_events,
        count(distinct subscriber_id)::integer as unique_listeners,
        max(occurred_at) as last_listened_at
    from public.story_listen_events
    group by story_slug
),
story_listen_session_stats as (
    select
        story_slug,
        count(*)::integer as total_listen_sessions,
        coalesce(round(sum(session_listened_seconds)::numeric, 1), 0)::numeric as total_listened_seconds,
        coalesce(round(avg(session_listened_seconds)::numeric, 1), 0)::numeric as average_listened_seconds_per_session
    from (
        select
            story_slug,
            session_id,
            sum(listened_seconds) as session_listened_seconds
        from public.story_listen_events
        group by story_slug, session_id
    ) as session_totals
    group by story_slug
),
story_favorite_stats as (
    select
        story_slug,
        count(distinct subscriber_id)::integer as total_favorites,
        count(distinct subscriber_id)::integer as unique_favoriters,
        max(favorited_at) as last_favorite_at
    from favorite_events
    where coalesce(nullif(btrim(story_slug), ''), '') <> ''
    group by story_slug
),
story_analytics as (
    select
        s.slug as story_slug,
        s.title,
        coalesce(story_view_stats.total_views, 0)::integer as total_views,
        coalesce(story_view_stats.unique_viewers, 0)::integer as unique_viewers,
        story_view_stats.last_viewed_at as last_view_at,
        coalesce(story_listen_stats.total_listen_events, 0)::integer as total_listen_events,
        coalesce(story_listen_stats.unique_listeners, 0)::integer as unique_listeners,
        coalesce(story_listen_session_stats.total_listen_sessions, 0)::integer as total_listen_sessions,
        coalesce(story_listen_session_stats.total_listened_seconds, 0)::numeric as total_listened_seconds,
        coalesce(story_listen_session_stats.average_listened_seconds_per_session, 0)::numeric as average_listened_seconds_per_session,
        story_listen_stats.last_listened_at as last_listen_at,
        coalesce(story_favorite_stats.total_favorites, 0)::integer as total_favorites,
        coalesce(story_favorite_stats.unique_favoriters, 0)::integer as unique_favoriters,
        story_favorite_stats.last_favorite_at,
        nullif(greatest(
            coalesce(story_view_stats.last_viewed_at, '-infinity'::timestamptz),
            coalesce(story_listen_stats.last_listened_at, '-infinity'::timestamptz),
            coalesce(story_favorite_stats.last_favorite_at, '-infinity'::timestamptz)
        ), '-infinity'::timestamptz) as last_activity_at
    from public.stories as s
    left join story_view_stats
        on story_view_stats.story_slug = s.slug
    left join story_listen_stats
        on story_listen_stats.story_slug = s.slug
    left join story_listen_session_stats
        on story_listen_session_stats.story_slug = s.slug
    left join story_favorite_stats
        on story_favorite_stats.story_slug = s.slug
),
top_stories as (
    select
        story_slug,
        title,
        total_views,
        unique_viewers,
        total_listen_sessions,
        total_listened_seconds,
        total_favorites,
        last_activity_at
    from story_analytics
    where
        total_views > 0
        or total_listen_sessions > 0
        or total_favorites > 0
    order by
        total_listened_seconds desc,
        total_views desc,
        total_favorites desc,
        last_activity_at desc,
        title asc
    limit 10
),
character_play_stats as (
    select
        p.character_slug,
        count(*)::integer as total_audio_plays,
        count(distinct p.subscriber_id)::integer as unique_subscribers,
        max(p.occurred_at) as last_activity_at
    from public.character_audio_plays as p
    group by p.character_slug
),
top_characters as (
    select
        c.slug as character_slug,
        coalesce(
            nullif(c.display_name, ''),
            initcap(replace(c.slug, '-', ' '))
        ) as display_name,
        coalesce(character_play_stats.total_audio_plays, 0)::integer as total_audio_plays,
        coalesce(character_play_stats.unique_subscribers, 0)::integer as unique_subscribers,
        character_play_stats.last_activity_at
    from public.story_characters as c
    left join character_play_stats
        on character_play_stats.character_slug = c.slug
    order by
        total_audio_plays desc,
        last_activity_at desc,
        display_name asc
)
select jsonb_build_object(
    'generated_at', now(),
    'story_summary', jsonb_build_object(
        'total_views', coalesce(story_view_summary.total_views, 0),
        'unique_viewers', coalesce(story_view_summary.unique_viewers, 0),
        'unique_viewed_stories', coalesce(story_view_summary.unique_viewed_stories, 0),
        'last_view_at', story_view_summary.last_view_at,
        'total_listen_events', coalesce(story_listen_summary.total_listen_events, 0),
        'unique_listeners', coalesce(story_listen_summary.unique_listeners, 0),
        'unique_listened_stories', coalesce(story_listen_summary.unique_listened_stories, 0),
        'total_listen_sessions', coalesce(story_listen_session_summary.total_listen_sessions, 0),
        'total_listened_seconds', coalesce(story_listen_summary.total_listened_seconds, 0),
        'average_listened_seconds_per_session', coalesce(story_listen_session_summary.average_listened_seconds_per_session, 0),
        'last_listen_at', story_listen_summary.last_listen_at,
        'total_favorites', coalesce(story_favorite_summary.total_favorites, 0),
        'unique_favoriters', coalesce(story_favorite_summary.unique_favoriters, 0),
        'last_favorite_at', story_favorite_summary.last_favorite_at
    ),
    'daily_activity', coalesce((
        select jsonb_agg(jsonb_build_object(
            'activity_date', daily_activity.activity_date,
            'total_views', daily_activity.total_views,
            'total_listen_sessions', daily_activity.total_listen_sessions,
            'total_listened_seconds', daily_activity.total_listened_seconds,
            'total_favorites', daily_activity.total_favorites
        ) order by daily_activity.activity_date desc)
        from daily_activity
    ), '[]'::jsonb),
    'top_stories', coalesce((
        select jsonb_agg(jsonb_build_object(
            'story_slug', top_stories.story_slug,
            'title', top_stories.title,
            'total_views', top_stories.total_views,
            'unique_viewers', top_stories.unique_viewers,
            'total_listen_sessions', top_stories.total_listen_sessions,
            'total_listened_seconds', top_stories.total_listened_seconds,
            'total_favorites', top_stories.total_favorites,
            'last_activity_at', top_stories.last_activity_at
        ) order by
            top_stories.total_listened_seconds desc,
            top_stories.total_views desc,
            top_stories.total_favorites desc,
            top_stories.last_activity_at desc)
        from top_stories
    ), '[]'::jsonb),
    'story_analytics', coalesce((
        select jsonb_agg(jsonb_build_object(
            'story_slug', story_analytics.story_slug,
            'title', story_analytics.title,
            'total_views', story_analytics.total_views,
            'unique_viewers', story_analytics.unique_viewers,
            'last_view_at', story_analytics.last_view_at,
            'total_listen_events', story_analytics.total_listen_events,
            'unique_listeners', story_analytics.unique_listeners,
            'total_listen_sessions', story_analytics.total_listen_sessions,
            'total_listened_seconds', story_analytics.total_listened_seconds,
            'average_listened_seconds_per_session', story_analytics.average_listened_seconds_per_session,
            'last_listen_at', story_analytics.last_listen_at,
            'total_favorites', story_analytics.total_favorites,
            'unique_favoriters', story_analytics.unique_favoriters,
            'last_favorite_at', story_analytics.last_favorite_at,
            'last_activity_at', story_analytics.last_activity_at
        ) order by story_analytics.title asc)
        from story_analytics
    ), '[]'::jsonb),
    'character_summary', jsonb_build_object(
        'total_audio_plays', coalesce(character_audio_summary.total_audio_plays, 0),
        'unique_subscribers', coalesce(character_audio_summary.unique_subscribers, 0),
        'unique_characters', coalesce(character_audio_summary.unique_characters, 0),
        'last_audio_play_at', character_audio_summary.last_audio_play_at
    ),
    'top_characters', coalesce((
        select jsonb_agg(jsonb_build_object(
            'character_slug', top_characters.character_slug,
            'display_name', top_characters.display_name,
            'total_audio_plays', top_characters.total_audio_plays,
            'unique_subscribers', top_characters.unique_subscribers,
            'last_activity_at', top_characters.last_activity_at
        ) order by
            top_characters.total_audio_plays desc,
            top_characters.last_activity_at desc,
            top_characters.display_name asc)
        from top_characters
    ), '[]'::jsonb)
)
from story_view_summary
cross join story_listen_summary
cross join story_listen_session_summary
cross join story_favorite_summary
cross join character_audio_summary;
$$;

