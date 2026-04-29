alter table public.store_orders drop constraint if exists store_orders_check;

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
