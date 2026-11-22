-- Admin-only reads. Every report is built from one database snapshot.
create or replace function public.get_admin_engagement_periods(p_today timestamptz, p_week timestamptz, p_month timestamptz, p_year timestamptz, p_now timestamptz)
returns jsonb
language sql stable security invoker
set search_path = pg_catalog, public
set work_mem = '16MB'
as $$
with session_starts as (
    select min(occurred_at) as started_at
    from public.story_listen_events
    where session_id <> '00000000-0000-0000-0000-000000000000'::uuid
    group by session_id
),
visits as (
    select array[
        count(*) filter (where started_at between p_today and p_now),
        count(*) filter (where started_at between p_week and p_now),
        count(*) filter (where started_at between p_month and p_now),
        count(*) filter (where started_at between p_year and p_now),
        count(started_at)
    ] as totals
    from session_starts
),
views as (
    select array[
        count(*) filter (where viewed_at between p_today and p_now),
        count(*) filter (where viewed_at between p_week and p_now),
        count(*) filter (where viewed_at between p_month and p_now),
        count(*) filter (where viewed_at between p_year and p_now),
        count(viewed_at)
    ] as totals
    from public.story_views
),
logins as (
    select array[
        count(*) filter (where created_at between p_today and p_now),
        count(*) filter (where created_at between p_week and p_now),
        count(*) filter (where created_at between p_month and p_now),
        count(*) filter (where created_at between p_year and p_now),
        count(created_at)
    ] as totals
    from public.auth_sessions
)
select jsonb_build_object('periods', jsonb_agg(jsonb_build_object('periodKey', period, 'visits', visits.totals[i],
    'views', views.totals[i], 'logins', logins.totals[i]) order by i))
from visits, views, logins,
    unnest(array['today','this_week','this_month','year_to_date','all_time']) with ordinality as periods(period, i);
$$;
revoke all on function public.get_admin_engagement_periods(timestamptz,timestamptz,timestamptz,timestamptz,timestamptz) from public, anon, authenticated;
grant execute on function public.get_admin_engagement_periods(timestamptz,timestamptz,timestamptz,timestamptz,timestamptz) to service_role;

create or replace function public.get_admin_resource_analytics()
returns jsonb
language sql stable security invoker
set search_path = pg_catalog, public
as $$
with resource_download_items as (
    select
        e.resource_document_id,
        coalesce(nullif(d.slug, ''), e.resource_document_id::text) as resource_slug,
        coalesce(nullif(d.title, ''), nullif(d.file_name, ''), e.download_path, e.resource_document_id::text) as title,
        count(*)::integer as total_downloads,
        count(*) filter (where e.downloaded_at >= current_date - interval '29 days')::integer as downloads_last_30_days,
        max(e.downloaded_at) as last_download_at
    from public.resource_document_download_events as e
    left join public.resource_documents as d
        on d.resource_document_id = e.resource_document_id
    group by
        e.resource_document_id,
        d.slug,
        d.title,
        d.file_name,
        e.download_path
)
select jsonb_build_object(
    'total_downloads', coalesce(sum(total_downloads), 0),
    'downloads_last_30_days', coalesce(sum(downloads_last_30_days), 0),
    'last_download_at', max(last_download_at),
    'items', coalesce(jsonb_agg(to_jsonb(i) order by total_downloads desc, downloads_last_30_days desc, last_download_at desc, title), '[]'))
from resource_download_items i;
$$;
revoke all on function public.get_admin_resource_analytics() from public, anon, authenticated;
grant execute on function public.get_admin_resource_analytics() to service_role;

create or replace function public.get_admin_blog_analytics()
returns jsonb
language sql stable security invoker
set search_path = pg_catalog, public
as $$
with blog_visit_pages as (
    select
        coalesce(nullif(e.visit_path, ''), '/blog') as page_path,
        coalesce(
            nullif(p.title, ''),
            nullif(e.post_slug, ''),
            nullif(e.visit_path, ''),
            '/blog'
        ) as page_title,
        count(*)::integer as total_visits,
        count(*) filter (where e.post_id is not null) as post_visits,
        count(*) filter (where e.visited_at >= current_date - interval '29 days')::integer as visits_last_30_days,
        max(e.visited_at) as last_visit_at
    from public.blog_visit_events as e
    left join public.blog_posts as p
        on p.post_id = e.post_id
    group by
        page_path,
        page_title
)
select jsonb_build_object(
    'total_visits', coalesce(sum(total_visits), 0), 'post_visits', coalesce(sum(post_visits), 0),
    'visits_last_30_days', coalesce(sum(visits_last_30_days), 0), 'last_visit_at', max(last_visit_at),
    'pages', coalesce(jsonb_agg(to_jsonb(p) - 'post_visits' order by total_visits desc, visits_last_30_days desc, last_visit_at desc, page_path), '[]'))
from blog_visit_pages p;
$$;
revoke all on function public.get_admin_blog_analytics() from public, anon, authenticated;
grant execute on function public.get_admin_blog_analytics() to service_role;

create or replace function public.get_admin_award_analytics()
returns jsonb
language sql stable security invoker
set search_path = pg_catalog, public
as $$
select jsonb_build_object(
    'oortjies_clicked', count(*),
    'oortjies_clicked_today', count(*) filter (where clicked_at >= current_date),
    'oortjies_clicked_last_30_days', count(*) filter (where clicked_at >= current_date - interval '29 days'),
    'unique_subscribers', count(distinct subscriber_id),
    'last_oortjies_clicked_at', max(clicked_at))
from public.oortjies_click_events;
$$;
revoke all on function public.get_admin_award_analytics() from public, anon, authenticated;
grant execute on function public.get_admin_award_analytics() to service_role;

-- Keep the common aggregate on narrow index pages. Only completed listens need
-- the position/metadata predicate, and the Stories panel does not need it at all.
create index if not exists idx_story_listen_admin_totals
    on public.story_listen_events (story_slug, session_id, subscriber_id)
    include (occurred_at, listened_seconds);

create index if not exists idx_story_listen_admin_completed_sessions
    on public.story_listen_events (story_slug, session_id)
    where session_id is not null and (
        event_type = 'ended' or metadata -> 'is_completed' = 'true'::jsonb
        or (duration_seconds > 0 and position_seconds >= duration_seconds * 0.95)
    );

create or replace function public.get_admin_listening_analytics(p_stories boolean)
returns jsonb
language sql stable security invoker
set search_path = pg_catalog, public
set work_mem = '16MB'
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
listen_session_listener_totals as materialized (
    select
        e.story_slug,
        e.session_id,
        e.subscriber_id,
        case when not p_stories and e.occurred_at >= current_date - interval '29 days'
            then date_trunc('day', e.occurred_at)::date
        end as activity_date,
        count(*)::integer as total_listen_events,
        sum(e.listened_seconds) as listened_seconds,
        max(e.occurred_at) as last_listen_at
    from public.story_listen_events as e
    group by e.story_slug, e.session_id, e.subscriber_id, activity_date
),
story_session_totals as materialized (
    select
        story_slug,
        session_id,
        sum(listened_seconds) as listened_seconds
    from listen_session_listener_totals
    group by story_slug, session_id
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
        coalesce(sum(total_listen_events), 0)::integer as total_listen_events,
        count(distinct subscriber_id)::integer as unique_listeners,
        count(distinct story_slug)::integer as unique_listened_stories,
        coalesce(round(sum(listened_seconds)::numeric, 1), 0)::numeric as total_listened_seconds,
        max(last_listen_at) as last_listen_at
    from listen_session_listener_totals
),
story_listen_session_totals as (
    select session_id, sum(listened_seconds) as listened_seconds
    from story_session_totals
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
    select current_date - day_offset as activity_date
    from generate_series(0, 29) as day_offset
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
        activity_date,
        count(distinct session_id)::integer as total_listen_sessions,
        coalesce(round(sum(listened_seconds)::numeric, 1), 0)::numeric as total_listened_seconds
    from listen_session_listener_totals
    where activity_date is not null
    group by activity_date
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
        sum(total_listen_events)::integer as total_listen_events,
        count(distinct subscriber_id)::integer as unique_listeners,
        max(last_listen_at) as last_listened_at
    from listen_session_listener_totals
    group by story_slug
),
story_listen_session_stats as (
    select
        story_slug,
        count(*)::integer as total_listen_sessions,
        coalesce(round(sum(listened_seconds)::numeric, 1), 0)::numeric as total_listened_seconds,
        coalesce(round(avg(listened_seconds)::numeric, 1), 0)::numeric as average_listened_seconds_per_session
    from story_session_totals
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
completed_sessions as (
    select distinct story_slug, session_id from public.story_listen_events
    where session_id is not null and (
        event_type = 'ended' or metadata -> 'is_completed' = 'true'::jsonb
        or (duration_seconds > 0 and position_seconds >= duration_seconds * 0.95)
    )
),
story_completion_session_stats as (
    select t.story_slug, t.session_id, c.session_id is not null as is_completed
    from story_session_totals t
    left join completed_sessions c using (story_slug, session_id)
    where t.session_id is not null
),
story_completion_stats as (
    select
        story_slug,
        count(*)::integer as total_listen_sessions,
        count(*) filter (where is_completed)::integer as completed_listen_sessions,
        coalesce(round((count(*) filter (where is_completed))::numeric * 100.0 / nullif(count(*)::numeric, 0), 1), 0)::numeric as completion_rate_percent
    from story_completion_session_stats
    group by story_slug
),
top_completion_stories as (
    select
        sa.story_slug,
        sa.title,
        sc.total_listen_sessions,
        sc.completed_listen_sessions,
        sc.completion_rate_percent,
        sa.total_listened_seconds,
        sa.last_activity_at
    from story_analytics as sa
    inner join story_completion_stats as sc
        on sc.story_slug = sa.story_slug
    where sc.total_listen_sessions > 0
    order by
        sc.completion_rate_percent desc,
        sc.total_listen_sessions desc,
        sa.total_listened_seconds desc,
        sa.last_activity_at desc,
        sa.title asc
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
select case when p_stories then jsonb_build_object(
    'generated_at', now(),
    'story_analytics', (select coalesce(jsonb_agg(s order by title, story_slug), '[]') from story_analytics s))
else (select jsonb_build_object(
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
    'character_summary', jsonb_build_object(
        'total_audio_plays', coalesce(character_audio_summary.total_audio_plays, 0),
        'unique_subscribers', coalesce(character_audio_summary.unique_subscribers, 0),
        'unique_characters', coalesce(character_audio_summary.unique_characters, 0),
        'last_audio_play_at', character_audio_summary.last_audio_play_at
    ),
    'daily_activity', (select coalesce(jsonb_agg(d order by activity_date desc), '[]') from daily_activity d),
    'top_completion_stories', (select coalesce(jsonb_agg(s order by completion_rate_percent desc, total_listen_sessions desc,
        total_listened_seconds desc, last_activity_at desc, title), '[]') from top_completion_stories s),
    'top_characters', (select coalesce(jsonb_agg(c order by total_audio_plays desc, last_activity_at desc, display_name), '[]') from top_characters c))
    from story_view_summary, story_listen_summary, story_listen_session_summary, story_favorite_summary, character_audio_summary)
end;
$$;
revoke all on function public.get_admin_listening_analytics(boolean) from public, anon, authenticated;
grant execute on function public.get_admin_listening_analytics(boolean) to service_role;

-- Return a scalar JSON document, so PostgREST's row limit cannot truncate a report.
-- Project only payment fields used to reconcile signups, sales and recoveries.
create or replace function public.get_admin_report_inputs(p_panel text)
returns json
language sql stable security invoker
set search_path = pg_catalog, public
as $$
select json_build_object(
    'subscribers', (
        select coalesce(json_agg(r), '[]'::json) from (
            select subscriber_id,email,first_name,last_name,display_name,created_at
            from public.subscribers
            where p_panel <> 'cancellations' or exists (select 1 from public.subscription_cancellation_feedback f where f.subscriber_id = subscribers.subscriber_id)
            order by updated_at desc, subscriber_id
        ) r),
    'subscriptions', case when p_panel <> 'cancellations' then (
        select coalesce(json_agg(r), '[]'::json) from (
            select subscription_id,subscriber_id,tier_code,provider,source_system,status,subscribed_at,next_renewal_at,cancelled_at,billing_amount_zar,billing_period_months,provider_payment_id,provider_transaction_id,provider_token
            from public.subscriptions
            order by subscribed_at desc, subscription_id
        ) r) else '[]'::json end,
    'events', case when p_panel in ('all', 'membership', 'revenue') then (
        select coalesce(json_agg(r), '[]'::json) from (
            select provider,event_type,event_status,received_at,provider_payment_id,provider_transaction_id,
            jsonb_strip_nulls(jsonb_build_object(
                'amount', payload #> '{amount}',
                'amount_gross', payload #> '{amount_gross}',
                'created_at', payload #> '{created_at}',
                'custom_str1', payload #> '{custom_str1}',
                'customer', jsonb_build_object(
                    'customer_code', payload #> '{customer,customer_code}',
                    'email', payload #> '{customer,email}',
                    'id', payload #> '{customer,id}'),
                'data', jsonb_build_object(
                    'amount', payload #> '{data,amount}',
                    'authorization', jsonb_build_object(
                        'authorization_code', payload #> '{data,authorization,authorization_code}'),
                    'customer', jsonb_build_object(
                        'customer_code', payload #> '{data,customer,customer_code}',
                        'email', payload #> '{data,customer,email}',
                        'id', payload #> '{data,customer,id}'),
                    'id', payload #> '{data,id}',
                    'metadata', jsonb_build_object(
                        'planSlug', payload #> '{data,metadata,planSlug}',
                        'plan_slug', payload #> '{data,metadata,plan_slug}',
                        'tier_code', payload #> '{data,metadata,tier_code}'),
                    'paidAt', payload #> '{data,paidAt}',
                    'paid_at', payload #> '{data,paid_at}',
                    'plan', jsonb_build_object(
                        'amount', payload #> '{data,plan,amount}',
                        'name', payload #> '{data,plan,name}',
                        'plan_code', payload #> '{data,plan,plan_code}'),
                    'reference', payload #> '{data,reference}',
                    'subscription', jsonb_build_object(
                        'subscription_code', payload #> '{data,subscription,subscription_code}'),
                    'subscription_code', payload #> '{data,subscription_code}',
                    'transaction', jsonb_build_object(
                        'authorization', jsonb_build_object(
                            'authorization_code', payload #> '{data,transaction,authorization,authorization_code}')),
                    'transaction_date', payload #> '{data,transaction_date}'),
                'email', payload #> '{email}',
                'item_name', payload #> '{item_name}',
                'm_payment_id', payload #> '{m_payment_id}',
                'paid_at', payload #> '{paid_at}',
                'pf_payment_id', payload #> '{pf_payment_id}',
                'planSlug', payload #> '{planSlug}',
                'plan_slug', payload #> '{plan_slug}',
                'reference', payload #> '{reference}',
                'tier_code', payload #> '{tier_code}',
                'token', payload #> '{token}')) as payload
            from public.subscription_events
            where provider in ('paystack','payfast') and event_type in ('charge.success','subscription.create','subscription.disable','payfast_itn')
            order by received_at desc, event_id
        ) r) else '[]'::json end,
    'orders', case when p_panel in ('all','membership','revenue') then (
        select coalesce(json_agg(r), '[]'::json) from (
            select order_id,order_reference,product_name,total_price_zar,payment_status,provider,provider_transaction_id,created_at,paid_at
            from public.store_orders
            where payment_status = 'paid'
            order by paid_at desc nulls last, created_at desc, order_id
        ) r) else '[]'::json end,
    'tiers', (
        select coalesce(json_agg(r), '[]'::json) from (
            select tier_code,display_name,price_zar,is_active
            from public.subscription_tiers
            order by display_name, tier_code
        ) r),
    'recoveries', case when p_panel <> 'cancellations' then (
        select coalesce(json_agg(r), '[]'::json) from (
            select recovery_id,subscription_id,provider,provider_payment_id,first_failed_at,grace_ends_at,created_at,resolved_at,resolution
            from public.subscription_payment_recoveries
            order by created_at desc, recovery_id
        ) r) else '[]'::json end,
    'carts', case when p_panel in ('all','revenue','recoveries') then (
        select coalesce(json_agg(r), '[]'::json) from (
            select recovery_id,source_type,source_key,checkout_reference,provider,customer_email,customer_name,item_name,cart_total_zar,created_at,resolved_at,resolution
            from public.abandoned_cart_recoveries
            order by created_at desc, recovery_id
        ) r) else '[]'::json end,
    'feedback', case when p_panel in ('all','cancellations') then (
        select coalesce(json_agg(r), '[]'::json) from (
            select feedback_id,subscriber_id,subscription_id,tier_code,provider,feedback_status,reason_code,note,cancelled_subscription_count,created_at
            from public.subscription_cancellation_feedback
            order by created_at desc, feedback_id
        ) r) else '[]'::json end
);
$$;
revoke all on function public.get_admin_report_inputs(text) from public, anon, authenticated;
grant execute on function public.get_admin_report_inputs(text) to service_role;
