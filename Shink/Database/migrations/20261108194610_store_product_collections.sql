alter table public.store_products add column if not exists collection_slug text;

-- Initialise the existing catalogue once; never replace later admin assignments.
update public.store_products
set collection_slug = case
    when slug = 'suurlemoentjie-boek' then 'kinderboeke'
    when slug in ('suurlemoentjie-boek-tjommie', 'suurlemoentjie-boek-subskripsie', 'suurlemoentjie-boek-pakket') then 'pakkette'
    else 'storietjommies'
end
where collection_slug is null;

alter table public.store_products alter column collection_slug set default 'storietjommies';
alter table public.store_products alter column collection_slug set not null;
alter table public.store_products drop constraint if exists store_products_collection_slug_check;
alter table public.store_products add constraint store_products_collection_slug_check
    check (collection_slug in ('kinderboeke', 'storietjommies', 'pakkette'));

comment on column public.store_products.collection_slug is 'Admin-assigned storefront collection. Existing store_products RLS and grants apply.';
notify pgrst, 'reload schema';
