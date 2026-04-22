create extension if not exists pgcrypto;

create table if not exists public.store_products (
    store_product_id uuid primary key default gen_random_uuid(),
    slug text not null unique,
    name text not null,
    description text,
    image_path text not null,
    alt_text text,
    theme_class text,
    unit_price_zar numeric(10, 2) not null check (unit_price_zar > 0),
    sort_order integer not null default 100,
    is_enabled boolean not null default true,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint store_products_slug_format check (slug ~ '^[a-z0-9]+(?:-[a-z0-9]+)*$'),
    constraint store_products_slug_not_blank check (btrim(slug) <> ''),
    constraint store_products_name_not_blank check (btrim(name) <> ''),
    constraint store_products_image_path_not_blank check (btrim(image_path) <> '')
);

comment on table public.store_products is 'Admin-managed products displayed and sold on the public /winkel page.';
comment on column public.store_products.unit_price_zar is 'Unit price in ZAR used for checkout totals.';
comment on column public.store_products.theme_class is 'Optional CSS class for themed product card styling on /winkel.';

create index if not exists idx_store_products_enabled_sort
    on public.store_products(is_enabled, sort_order, name);

alter table public.store_products enable row level security;

drop policy if exists store_products_read_enabled on public.store_products;
create policy store_products_read_enabled
on public.store_products
for select
to anon, authenticated
using (is_enabled = true);

drop policy if exists store_products_service_role_all on public.store_products;
create policy store_products_service_role_all
on public.store_products
for all
to service_role
using (true)
with check (true);

drop trigger if exists trg_store_products_set_updated_at on public.store_products;
create trigger trg_store_products_set_updated_at
before update on public.store_products
for each row execute function public.set_updated_at();

insert into public.store_products (
    slug,
    name,
    description,
    image_path,
    alt_text,
    theme_class,
    unit_price_zar,
    sort_order,
    is_enabled
)
values
    (
        'suurlemoentjie',
        'Suurlemoentjie',
        'Helder, vrolik en gereed vir stories vol sonskyn en moed.',
        '/branding/winkel/storie-tjommie-suurlemoentjie.png',
        'Suurlemoentjie StorieTjommie teddie',
        'is-suurlemoentjie',
        250.00,
        10,
        true
    ),
    (
        'tiekie',
        'Tiekie',
        'Vir kinders wat hou van sagte troos en ''n bekende maatjie naby.',
        '/branding/winkel/storie-tjommie-tiekie.png',
        'Tiekie StorieTjommie teddie',
        'is-tiekie',
        250.00,
        20,
        true
    ),
    (
        'lama-lama-pajama-lama',
        'Lama Lama Pajama Lama',
        'Speels en knus vir slaaptyd, speeltyd en elke giggel tussenin.',
        '/branding/winkel/storie-tjommie-lama-lama-pajama-lama.png',
        'Lama Lama Pajama Lama StorieTjommie teddie',
        'is-lama',
        250.00,
        30,
        true
    ),
    (
        'georgie',
        'Georgie',
        'Rustige geselskap vir kinders wat lief is vir Georgie se warm persoonlikheid.',
        '/branding/winkel/storie-tjommie-georgie.png',
        'Georgie StorieTjommie teddie',
        'is-georgie',
        250.00,
        40,
        true
    )
on conflict (slug) do update
set
    name = excluded.name,
    description = excluded.description,
    image_path = excluded.image_path,
    alt_text = excluded.alt_text,
    theme_class = excluded.theme_class,
    unit_price_zar = excluded.unit_price_zar,
    sort_order = excluded.sort_order,
    is_enabled = excluded.is_enabled;

alter table public.store_orders drop constraint if exists store_orders_quantity_check;
alter table public.store_orders drop constraint if exists store_orders_total_price_zar_check;

do $$
begin
    if not exists (
        select 1
        from pg_constraint
        where conname = 'store_orders_quantity_range_check'
          and conrelid = 'public.store_orders'::regclass
    ) then
        alter table public.store_orders
            add constraint store_orders_quantity_range_check
            check (quantity between 1 and 500);
    end if;
end
$$;

do $$
begin
    if not exists (
        select 1
        from pg_constraint
        where conname = 'store_orders_total_price_nonnegative_check'
          and conrelid = 'public.store_orders'::regclass
    ) then
        alter table public.store_orders
            add constraint store_orders_total_price_nonnegative_check
            check (total_price_zar >= 0);
    end if;
end
$$;
