alter table public.store_products
    add column if not exists waives_delivery_fee boolean not null default false;

comment on column public.store_products.waives_delivery_fee is
    'When true, an order containing this product does not include the standard PUDO delivery fee.';

alter table public.store_orders
    add column if not exists delivery_fee_waived boolean not null default false;

comment on column public.store_orders.delivery_fee_waived is
    'Whether this order waived the standard PUDO delivery fee at checkout time.';
