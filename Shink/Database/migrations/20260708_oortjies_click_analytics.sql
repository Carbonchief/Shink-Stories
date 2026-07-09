create table if not exists public.oortjies_click_events (
    oortjies_click_event_id uuid primary key default gen_random_uuid(),
    subscriber_id uuid references public.subscribers(subscriber_id) on delete set null,
    page_path text not null default '/luister',
    side text,
    clicked_at timestamptz not null default now(),
    constraint oortjies_click_events_page_path_not_blank check (btrim(page_path) <> ''),
    constraint oortjies_click_events_side_valid check (side is null or side in ('top', 'right', 'bottom', 'left'))
);

create index if not exists idx_oortjies_click_events_clicked_at
    on public.oortjies_click_events(clicked_at desc);

create index if not exists idx_oortjies_click_events_subscriber_clicked_at
    on public.oortjies_click_events(subscriber_id, clicked_at desc);

alter table public.oortjies_click_events enable row level security;

drop policy if exists oortjies_click_events_service_role_all on public.oortjies_click_events;
create policy oortjies_click_events_service_role_all
on public.oortjies_click_events
for all
to service_role
using (true)
with check (true);

grant select, insert, update, delete on table public.oortjies_click_events to service_role;

do $$
begin
    if not exists (
        select 1
        from pg_proc p
        join pg_namespace n on n.oid = p.pronamespace
        where n.nspname = 'public'
          and p.proname = 'get_admin_analytics_snapshot_before_oortjies'
          and pg_get_function_identity_arguments(p.oid) = ''
    ) then
        alter function public.get_admin_analytics_snapshot() rename to get_admin_analytics_snapshot_before_oortjies;
    end if;
end;
$$;

create or replace function public.get_admin_analytics_snapshot()
returns jsonb
language sql
stable
set search_path = pg_catalog, public
as $$
with base as (
    select public.get_admin_analytics_snapshot_before_oortjies() as snapshot
),
oortjies_click_summary as (
    select
        count(*)::integer as total_clicks,
        count(*) filter (where clicked_at >= current_date)::integer as clicks_today,
        count(*) filter (where clicked_at >= current_date - interval '29 days')::integer as clicks_last_30_days,
        count(distinct subscriber_id) filter (where subscriber_id is not null)::integer as unique_subscribers,
        max(clicked_at) as last_clicked_at
    from public.oortjies_click_events
)
select jsonb_set(
    base.snapshot,
    '{award_summary}',
    jsonb_build_object(
        'oortjies_clicked', coalesce(oortjies_click_summary.total_clicks, 0),
        'oortjies_clicked_today', coalesce(oortjies_click_summary.clicks_today, 0),
        'oortjies_clicked_last_30_days', coalesce(oortjies_click_summary.clicks_last_30_days, 0),
        'unique_subscribers', coalesce(oortjies_click_summary.unique_subscribers, 0),
        'last_oortjies_clicked_at', oortjies_click_summary.last_clicked_at
    ),
    true
)
from base
cross join oortjies_click_summary;
$$;

revoke all on function public.get_admin_analytics_snapshot() from public, anon, authenticated;
grant execute on function public.get_admin_analytics_snapshot() to service_role;

revoke all on function public.get_admin_analytics_snapshot_before_oortjies() from public, anon, authenticated;
grant execute on function public.get_admin_analytics_snapshot_before_oortjies() to service_role;
