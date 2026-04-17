create table if not exists public.store_orders (
    order_id uuid primary key default gen_random_uuid(),
    order_reference text not null unique,
    product_slug text not null,
    product_name text not null,
    quantity integer not null check (quantity between 1 and 10),
    unit_price_zar numeric(10, 2) not null check (unit_price_zar >= 0),
    total_price_zar numeric(10, 2) not null check (total_price_zar = unit_price_zar * quantity),
    customer_name text not null,
    customer_email text not null,
    customer_phone text not null,
    delivery_address_line_1 text not null,
    delivery_address_line_2 text,
    delivery_suburb text,
    delivery_city text not null,
    delivery_postal_code text not null,
    notes text,
    payment_status text not null default 'pending' check (payment_status in ('pending', 'paid', 'failed', 'cancelled')),
    provider text not null default 'paystack' check (provider in ('paystack')),
    currency text not null default 'ZAR' check (currency = 'ZAR'),
    status_reason text,
    provider_transaction_id text,
    paid_at timestamptz,
    raw_verify_response jsonb,
    raw_webhook_payload jsonb,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint store_orders_reference_format check (order_reference ~ '^[A-Za-z0-9.=\\-]+$'),
    constraint store_orders_product_slug_format check (product_slug ~ '^[a-z0-9-]+$'),
    constraint store_orders_customer_email_format check (position('@' in customer_email) > 1),
    constraint store_orders_customer_email_lowercase check (customer_email = lower(customer_email)),
    constraint store_orders_delivery_postal_code_length check (char_length(delivery_postal_code) between 3 and 20)
);

create unique index if not exists idx_store_orders_provider_transaction_id
    on public.store_orders(provider_transaction_id)
    where provider_transaction_id is not null;

create index if not exists idx_store_orders_payment_status_created_at
    on public.store_orders(payment_status, created_at desc);

create index if not exists idx_store_orders_product_slug_created_at
    on public.store_orders(product_slug, created_at desc);

alter table public.store_orders enable row level security;

drop policy if exists "store_orders_select_own" on public.store_orders;
create policy "store_orders_select_own"
    on public.store_orders
    for select
    to authenticated
    using (
        lower(customer_email) = (select private.current_jwt_email())
    );

drop trigger if exists trg_store_orders_set_updated_at on public.store_orders;
create trigger trg_store_orders_set_updated_at
before update on public.store_orders
for each row execute function public.set_updated_at();
