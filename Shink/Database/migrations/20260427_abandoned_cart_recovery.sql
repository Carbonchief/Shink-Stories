create table if not exists public.abandoned_cart_recoveries (
    recovery_id uuid primary key default gen_random_uuid(),
    source_type text not null check (source_type in ('subscription', 'store_order')),
    source_key text not null,
    checkout_reference text not null,
    provider text not null,
    customer_email text not null,
    customer_name text,
    item_name text not null,
    item_summary text not null,
    cart_total_zar numeric(10, 2),
    checkout_url text not null,
    opt_out_token text not null,
    first_scheduled_at timestamptz not null,
    second_scheduled_at timestamptz not null,
    final_scheduled_at timestamptz not null,
    first_email_id text,
    second_email_id text,
    final_email_id text,
    resolved_at timestamptz,
    resolution text check (resolution in ('paid', 'cancelled', 'opted_out', 'expired')),
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint abandoned_cart_recoveries_customer_email_format check (position('@' in customer_email) > 1),
    constraint abandoned_cart_recoveries_customer_email_lowercase check (customer_email = lower(customer_email)),
    constraint abandoned_cart_recoveries_source_checkout_unique unique (source_type, checkout_reference)
);

create index if not exists idx_abandoned_cart_recoveries_customer_source
    on public.abandoned_cart_recoveries(source_type, source_key, customer_email)
    where resolved_at is null;

create index if not exists idx_abandoned_cart_recoveries_token
    on public.abandoned_cart_recoveries(recovery_id, opt_out_token)
    where resolved_at is null;

alter table public.abandoned_cart_recoveries enable row level security;

drop trigger if exists trg_abandoned_cart_recoveries_set_updated_at on public.abandoned_cart_recoveries;
create trigger trg_abandoned_cart_recoveries_set_updated_at
before update on public.abandoned_cart_recoveries
for each row execute function public.set_updated_at();
