create or replace function public.admin_subscribers_page(
    p_page_index integer default 0,
    p_page_size integer default 100,
    p_search text default null,
    p_sort_label text default null,
    p_sort_desc boolean default false
)
returns jsonb
language sql
security definer
set search_path = public
as $$
with normalized as (
    select
        greatest(coalesce(p_page_index, 0), 0) as page_index,
        least(greatest(coalesce(p_page_size, 100), 1), 500) as page_size,
        nullif(btrim(p_search), '') as search_term,
        case coalesce(nullif(lower(btrim(p_sort_label)), ''), 'subscriber')
            when 'subscriber' then 'subscriber'
            when 'mobile' then 'mobile'
            when 'tiers' then 'tiers'
            when 'source' then 'source'
            when 'provider' then 'provider'
            when 'status' then 'status'
            when 'subscribed_at' then 'subscribed_at'
            when 'next_payment' then 'next_payment'
            else 'subscriber'
        end as sort_label,
        coalesce(p_sort_desc, false) as sort_desc,
        now() as now_utc
),
base as (
    select
        s.subscriber_id,
        lower(trim(s.email)) as email,
        nullif(trim(s.first_name), '') as first_name,
        nullif(trim(s.last_name), '') as last_name,
        nullif(trim(s.display_name), '') as display_name,
        nullif(trim(s.mobile_number), '') as mobile_number,
        nullif(trim(s.profile_image_url), '') as profile_image_url,
        s.created_at,
        s.updated_at,
        coalesce(active.active_tier_codes, array[]::text[]) as active_tier_codes,
        summary.payment_provider,
        summary.subscription_source_system,
        summary.subscription_status,
        summary.subscribed_at,
        summary.next_payment_due_at,
        summary.cancelled_at,
        lower(
            coalesce(
                nullif(trim(concat_ws(' ', s.first_name, s.last_name)), ''),
                nullif(trim(s.display_name), ''),
                trim(s.email)
            )
        ) as subscriber_sort_key,
        lower(coalesce(s.mobile_number, '')) as mobile_sort_key,
        lower(array_to_string(coalesce(active.active_tier_codes, array[]::text[]), ', ')) as tier_sort_key,
        lower(coalesce(summary.subscription_source_system, '')) as source_sort_key,
        lower(coalesce(summary.payment_provider, '')) as provider_sort_key,
        lower(coalesce(summary.subscription_status, '')) as status_sort_key
    from public.subscribers s
    cross join normalized n
    left join lateral (
        select array_agg(distinct sub.tier_code order by sub.tier_code) as active_tier_codes
        from public.subscriptions sub
        where sub.subscriber_id = s.subscriber_id
          and sub.status = 'active'
          and (sub.cancelled_at is null or sub.cancelled_at > n.now_utc)
          and (sub.next_renewal_at is null or sub.next_renewal_at >= n.now_utc)
    ) active on true
    left join lateral (
        select
            sub.provider as payment_provider,
            sub.source_system as subscription_source_system,
            sub.status as subscription_status,
            sub.subscribed_at,
            sub.next_renewal_at as next_payment_due_at,
            sub.cancelled_at
        from public.subscriptions sub
        where sub.subscriber_id = s.subscriber_id
        order by
            case
                when sub.status = 'active'
                     and (sub.cancelled_at is null or sub.cancelled_at > n.now_utc)
                     and (sub.next_renewal_at is null or sub.next_renewal_at >= n.now_utc) then 3
                when sub.status = 'pending' then 2
                when sub.status = 'cancelled' then 1
                else 0
            end desc,
            coalesce(sub.subscribed_at, '-infinity'::timestamptz) desc,
            sub.created_at desc
        limit 1
    ) summary on true
    where n.search_term is null
       or s.email ilike ('%' || n.search_term || '%')
       or coalesce(s.first_name, '') ilike ('%' || n.search_term || '%')
       or coalesce(s.last_name, '') ilike ('%' || n.search_term || '%')
       or coalesce(s.display_name, '') ilike ('%' || n.search_term || '%')
       or coalesce(s.mobile_number, '') ilike ('%' || n.search_term || '%')
),
counted as (
    select count(*)::integer as total_count
    from base
),
ordered as (
    select
        b.*,
        row_number() over () as row_order
    from base b
    cross join normalized n
    order by
        case when n.sort_label = 'subscriber' and not n.sort_desc then b.subscriber_sort_key end asc nulls last,
        case when n.sort_label = 'subscriber' and n.sort_desc then b.subscriber_sort_key end desc nulls last,
        case when n.sort_label = 'mobile' and not n.sort_desc then b.mobile_sort_key end asc nulls last,
        case when n.sort_label = 'mobile' and n.sort_desc then b.mobile_sort_key end desc nulls last,
        case when n.sort_label = 'tiers' and not n.sort_desc then b.tier_sort_key end asc nulls last,
        case when n.sort_label = 'tiers' and n.sort_desc then b.tier_sort_key end desc nulls last,
        case when n.sort_label = 'source' and not n.sort_desc then b.source_sort_key end asc nulls last,
        case when n.sort_label = 'source' and n.sort_desc then b.source_sort_key end desc nulls last,
        case when n.sort_label = 'provider' and not n.sort_desc then b.provider_sort_key end asc nulls last,
        case when n.sort_label = 'provider' and n.sort_desc then b.provider_sort_key end desc nulls last,
        case when n.sort_label = 'status' and not n.sort_desc then b.status_sort_key end asc nulls last,
        case when n.sort_label = 'status' and n.sort_desc then b.status_sort_key end desc nulls last,
        case when n.sort_label = 'subscribed_at' and not n.sort_desc then b.subscribed_at end asc nulls last,
        case when n.sort_label = 'subscribed_at' and n.sort_desc then b.subscribed_at end desc nulls last,
        case when n.sort_label = 'next_payment' and not n.sort_desc then b.next_payment_due_at end asc nulls last,
        case when n.sort_label = 'next_payment' and n.sort_desc then b.next_payment_due_at end desc nulls last,
        b.updated_at desc,
        b.email asc
    limit (select page_size from normalized)
    offset (select page_index * page_size from normalized)
)
select jsonb_build_object(
    'total_count', coalesce((select total_count from counted), 0),
    'items',
    coalesce(
        (
            select jsonb_agg(
                jsonb_build_object(
                    'subscriber_id', o.subscriber_id,
                    'email', o.email,
                    'first_name', o.first_name,
                    'last_name', o.last_name,
                    'display_name', o.display_name,
                    'mobile_number', o.mobile_number,
                    'profile_image_url', o.profile_image_url,
                    'created_at', o.created_at,
                    'updated_at', o.updated_at,
                    'active_tier_codes', o.active_tier_codes,
                    'payment_provider', o.payment_provider,
                    'subscription_source_system', o.subscription_source_system,
                    'subscription_status', o.subscription_status,
                    'subscribed_at', o.subscribed_at,
                    'next_payment_due_at', o.next_payment_due_at,
                    'cancelled_at', o.cancelled_at
                )
                order by o.row_order
            )
            from ordered o
        ),
        '[]'::jsonb
    )
);
$$;

revoke all on function public.admin_subscribers_page(integer, integer, text, text, boolean) from public;
revoke all on function public.admin_subscribers_page(integer, integer, text, text, boolean) from anon;
revoke all on function public.admin_subscribers_page(integer, integer, text, text, boolean) from authenticated;
grant execute on function public.admin_subscribers_page(integer, integer, text, text, boolean) to service_role;
