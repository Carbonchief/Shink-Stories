alter table public.store_orders
    add column if not exists items jsonb not null default '[]'::jsonb;

update public.store_orders
set items = jsonb_build_array(
    jsonb_build_object(
        'product_slug', product_slug,
        'product_name', product_name,
        'quantity', quantity,
        'unit_price_zar', unit_price_zar,
        'line_total_zar', total_price_zar
    ))
where jsonb_typeof(items) = 'array'
  and jsonb_array_length(items) = 0
  and quantity > 0
  and unit_price_zar > 0
  and product_slug is not null
  and product_name is not null;

do $$
begin
    if not exists (
        select 1
        from pg_constraint
        where conname = 'store_orders_items_is_array'
          and conrelid = 'public.store_orders'::regclass
    ) then
        alter table public.store_orders
            add constraint store_orders_items_is_array
            check (jsonb_typeof(items) = 'array');
    end if;
end
$$;
