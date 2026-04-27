create or replace function public.get_wordpress_subscriber_report_snapshot()
returns jsonb
language sql
security definer
set search_path = public, private
as $$
with dataset_state as (
    select exists(select 1 from private.wordpress_membership_periods)
        or exists(select 1 from private.wordpress_membership_orders)
        or exists(select 1 from private.wordpress_subscriptions) as has_wordpress_data
),
bounds as (
    select
        timezone('Africa/Johannesburg', now())::date as today,
        date_trunc('month', timezone('Africa/Johannesburg', now()))::date as month_start,
        date_trunc('year', timezone('Africa/Johannesburg', now()))::date as year_start
),
membership_periods as (
    select
        wp_user_id,
        coalesce(nullif(tier_code, ''), 'gratis') as tier_code,
        status,
        startdate,
        coalesce(modified_at, enddate, startdate, last_synced_at) as status_changed_at
    from private.wordpress_membership_periods
),
membership_orders as (
    select
        nullif(tier_code, '') as tier_code,
        status,
        order_timestamp,
        coalesce(nullif(regexp_replace(total, '[^0-9\.-]', '', 'g'), '')::numeric, 0) as total_amount
    from private.wordpress_membership_orders
),
membership_period_specs as (
    select *
    from (values
        ('today'::text, 1),
        ('this_month'::text, 2),
        ('this_year'::text, 3),
        ('all_time'::text, 4)
    ) as spec(period_key, sort_order)
),
membership_metrics as (
    select
        spec.period_key,
        spec.sort_order,
        (
            select count(distinct period_row.wp_user_id)::integer
            from membership_periods period_row
            cross join bounds
            where period_row.startdate is not null
              and case spec.period_key
                  when 'today' then timezone('Africa/Johannesburg', period_row.startdate)::date = bounds.today
                  when 'this_month' then timezone('Africa/Johannesburg', period_row.startdate)::date >= bounds.month_start
                  when 'this_year' then timezone('Africa/Johannesburg', period_row.startdate)::date >= bounds.year_start
                  else true
              end
        ) as signups,
        (
            select count(distinct period_row.wp_user_id)::integer
            from membership_periods period_row
            cross join bounds
            where period_row.status in ('cancelled', 'expired', 'admin_cancelled', 'inactive')
              and period_row.status_changed_at is not null
              and case spec.period_key
                  when 'today' then timezone('Africa/Johannesburg', period_row.status_changed_at)::date = bounds.today
                  when 'this_month' then timezone('Africa/Johannesburg', period_row.status_changed_at)::date >= bounds.month_start
                  when 'this_year' then timezone('Africa/Johannesburg', period_row.status_changed_at)::date >= bounds.year_start
                  else true
              end
        ) as cancellations
    from membership_period_specs spec
),
active_members_per_level as (
    select
        tier_code,
        count(distinct wp_user_id)::integer as active_members
    from membership_periods
    where status = 'active'
      and tier_code is not null
      and tier_code <> ''
    group by tier_code
),
sales_specs as (
    select *
    from (values
        ('today'::text, 1),
        ('this_month'::text, 2),
        ('this_year'::text, 3),
        ('all_time'::text, 4)
    ) as spec(period_key, sort_order)
),
sales_metrics as (
    select
        spec.period_key,
        spec.sort_order,
        (
            select count(*)::integer
            from membership_orders order_row
            cross join bounds
            where order_row.status = 'success'
              and coalesce(order_row.tier_code, '') <> 'gratis'
              and order_row.total_amount > 0
              and case spec.period_key
                  when 'all_time' then true
                  when 'today' then timezone('Africa/Johannesburg', order_row.order_timestamp)::date = bounds.today
                  when 'this_month' then timezone('Africa/Johannesburg', order_row.order_timestamp)::date >= bounds.month_start
                  when 'this_year' then timezone('Africa/Johannesburg', order_row.order_timestamp)::date >= bounds.year_start
                  else order_row.order_timestamp is not null
              end
        ) as sales,
        (
            select round(coalesce(sum(order_row.total_amount), 0), 2)
            from membership_orders order_row
            cross join bounds
            where order_row.status = 'success'
              and coalesce(order_row.tier_code, '') <> 'gratis'
              and case spec.period_key
                  when 'all_time' then true
                  when 'today' then timezone('Africa/Johannesburg', order_row.order_timestamp)::date = bounds.today
                  when 'this_month' then timezone('Africa/Johannesburg', order_row.order_timestamp)::date >= bounds.month_start
                  when 'this_year' then timezone('Africa/Johannesburg', order_row.order_timestamp)::date >= bounds.year_start
                  else order_row.order_timestamp is not null
              end
        ) as revenue
    from sales_specs spec
)
select jsonb_build_object(
    'has_wordpress_data',
    coalesce((select has_wordpress_data from dataset_state), false),
    'membership_stats',
    coalesce(
        (
            select jsonb_agg(
                jsonb_build_object(
                    'period_key', period_key,
                    'signups', signups,
                    'cancellations', cancellations
                )
                order by sort_order
            )
            from membership_metrics
        ),
        '[]'::jsonb
    ),
    'active_members_per_level',
    coalesce(
        (
            select jsonb_agg(
                jsonb_build_object(
                    'tier_code', tier_code,
                    'active_members', active_members
                )
                order by active_members desc, tier_code asc
            )
            from active_members_per_level
        ),
        '[]'::jsonb
    ),
    'sales_and_revenue',
    coalesce(
        (
            select jsonb_agg(
                jsonb_build_object(
                    'period_key', period_key,
                    'sales', sales,
                    'revenue', revenue
                )
                order by sort_order
            )
            from sales_metrics
        ),
        '[]'::jsonb
    )
);
$$;

revoke all on function public.get_wordpress_subscriber_report_snapshot() from public, anon, authenticated;
grant execute on function public.get_wordpress_subscriber_report_snapshot() to service_role;
