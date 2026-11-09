alter table public.store_orders add column if not exists shipped_at timestamptz;

comment on column public.store_orders.shipped_at is 'When an administrator marked this paid order as shipped.';
notify pgrst, 'reload schema';
