alter table public.store_orders
    add column if not exists analytics_visitor_id uuid,
    add column if not exists analytics_reported_at timestamptz;

create index if not exists idx_store_orders_unreported_paid_analytics
    on public.store_orders (paid_at, order_id)
    where payment_status = 'paid'
      and paid_at is not null
      and analytics_visitor_id is not null
      and analytics_reported_at is null;

comment on column public.store_orders.analytics_visitor_id is 'Pseudonymous Winkel browser ID for purchase funnel attribution.';
comment on column public.store_orders.analytics_reported_at is 'When PostHog accepted the verified purchase event.';
notify pgrst, 'reload schema';
