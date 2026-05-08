do $$
begin
    if not exists (
        select 1
        from pg_proc p
        join pg_namespace n on n.oid = p.pronamespace
        where n.nspname = 'public'
          and p.proname = 'get_admin_analytics_snapshot_base'
          and pg_get_function_identity_arguments(p.oid) = ''
    ) then
        alter function public.get_admin_analytics_snapshot() rename to get_admin_analytics_snapshot_base;
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
    select public.get_admin_analytics_snapshot_base() as snapshot
),
resource_download_items as (
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
),
blog_visit_pages as (
    select
        coalesce(nullif(e.visit_path, ''), '/blog') as page_path,
        coalesce(
            nullif(p.title, ''),
            nullif(e.post_slug, ''),
            nullif(e.visit_path, ''),
            '/blog'
        ) as page_title,
        count(*)::integer as total_visits,
        count(*) filter (where e.visited_at >= current_date - interval '29 days')::integer as visits_last_30_days,
        max(e.visited_at) as last_visit_at
    from public.blog_visit_events as e
    left join public.blog_posts as p
        on p.post_id = e.post_id
    group by
        page_path,
        page_title
),
resource_items_json as (
    select coalesce(jsonb_agg(jsonb_build_object(
        'resource_document_id', resource_download_items.resource_document_id,
        'resource_slug', resource_download_items.resource_slug,
        'title', resource_download_items.title,
        'total_downloads', resource_download_items.total_downloads,
        'downloads_last_30_days', resource_download_items.downloads_last_30_days,
        'last_download_at', resource_download_items.last_download_at
    ) order by
        resource_download_items.total_downloads desc,
        resource_download_items.downloads_last_30_days desc,
        resource_download_items.last_download_at desc,
        resource_download_items.title asc), '[]'::jsonb) as items
    from resource_download_items
),
blog_pages_json as (
    select coalesce(jsonb_agg(jsonb_build_object(
        'page_path', blog_visit_pages.page_path,
        'page_title', blog_visit_pages.page_title,
        'total_visits', blog_visit_pages.total_visits,
        'visits_last_30_days', blog_visit_pages.visits_last_30_days,
        'last_visit_at', blog_visit_pages.last_visit_at
    ) order by
        blog_visit_pages.total_visits desc,
        blog_visit_pages.visits_last_30_days desc,
        blog_visit_pages.last_visit_at desc,
        blog_visit_pages.page_path asc), '[]'::jsonb) as pages
    from blog_visit_pages
)
select
    jsonb_set(
        jsonb_set(
            base.snapshot,
            '{resource_download_summary,items}',
            resource_items_json.items,
            true
        ),
        '{blog_visit_summary,pages}',
        blog_pages_json.pages,
        true
    )
from base
cross join resource_items_json
cross join blog_pages_json;
$$;
