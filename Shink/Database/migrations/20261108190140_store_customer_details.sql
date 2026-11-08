-- Checkout preferences are private server-side account data, separate from orders.
create table if not exists public.store_customer_details (
    account_email text primary key references public.subscribers(email) on update cascade on delete cascade,
    customer_name text not null check (char_length(customer_name) between 1 and 120),
    customer_email text not null check (char_length(customer_email) between 3 and 254 and position('@' in customer_email) > 1),
    customer_phone text not null check (char_length(customer_phone) between 1 and 40),
    delivery_address_line_1 text not null check (char_length(delivery_address_line_1) between 1 and 250),
    delivery_address_line_2 text check (char_length(delivery_address_line_2) <= 250),
    delivery_suburb text check (char_length(delivery_suburb) <= 120),
    delivery_city text not null check (char_length(delivery_city) between 1 and 120),
    delivery_postal_code text not null check (char_length(delivery_postal_code) between 3 and 20),
    updated_at timestamptz not null default now(),
    constraint store_customer_details_owner_lowercase check (account_email = lower(account_email))
);

alter table public.store_customer_details enable row level security;
revoke all on public.store_customer_details from public, anon, authenticated;
grant select, insert, update, delete on public.store_customer_details to service_role;
comment on table public.store_customer_details is 'Private checkout preferences. Server derives account_email from its authenticated session. No public API role access.';
