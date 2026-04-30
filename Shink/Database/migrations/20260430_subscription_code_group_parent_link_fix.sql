create or replace function public.import_wordpress_subscription_discount_codes(payload jsonb)
returns integer
language plpgsql
security definer
set search_path = public
as $$
declare
    affected_count integer := 0;
    linked_group_count integer := 0;
begin
    with rows as (
        select
            rows.code,
            rows.display_name,
            rows.description,
            coalesce(rows.is_group_code, false) as is_group_code,
            rows.starts_at,
            rows.expires_at,
            coalesce(rows.max_uses, 0) as max_uses,
            coalesce(rows.one_use_per_user, false) as one_use_per_user,
            coalesce(rows.bypass_payment, true) as bypass_payment,
            coalesce(rows.is_active, true) as is_active,
            coalesce(nullif(btrim(rows.source_system), ''), 'wordpress_pmpro') as source_system,
            rows.source_discount_code_id,
            rows.source_group_code_id,
            rows.source_parent_discount_code_id,
            rows.source_order_id,
            rows.raw_source
        from jsonb_to_recordset(coalesce(payload, '[]'::jsonb)) as rows(
            code text,
            display_name text,
            description text,
            is_group_code boolean,
            starts_at timestamptz,
            expires_at timestamptz,
            max_uses integer,
            one_use_per_user boolean,
            bypass_payment boolean,
            is_active boolean,
            source_system text,
            source_discount_code_id bigint,
            source_group_code_id bigint,
            source_parent_discount_code_id bigint,
            source_order_id bigint,
            raw_source jsonb
        )
    ),
    upsert_parent as (
        insert into public.subscription_discount_codes (
            code,
            display_name,
            description,
            is_group_code,
            parent_discount_code_id,
            starts_at,
            expires_at,
            max_uses,
            one_use_per_user,
            bypass_payment,
            is_active,
            source_system,
            source_discount_code_id,
            source_parent_discount_code_id,
            source_order_id,
            raw_source
        )
        select
            coalesce(rows.code, ''),
            nullif(btrim(rows.display_name), ''),
            nullif(btrim(rows.description), ''),
            false,
            null,
            rows.starts_at,
            rows.expires_at,
            rows.max_uses,
            rows.one_use_per_user,
            rows.bypass_payment,
            rows.is_active,
            rows.source_system,
            rows.source_discount_code_id,
            rows.source_parent_discount_code_id,
            rows.source_order_id,
            rows.raw_source
        from rows
        where not rows.is_group_code
          and rows.source_discount_code_id is not null
        on conflict (source_discount_code_id) do update
        set code = excluded.code,
            display_name = excluded.display_name,
            description = excluded.description,
            starts_at = excluded.starts_at,
            expires_at = excluded.expires_at,
            max_uses = excluded.max_uses,
            one_use_per_user = excluded.one_use_per_user,
            bypass_payment = excluded.bypass_payment,
            is_active = excluded.is_active,
            source_system = excluded.source_system,
            source_parent_discount_code_id = excluded.source_parent_discount_code_id,
            source_order_id = excluded.source_order_id,
            raw_source = excluded.raw_source
        returning 1
    ),
    upsert_group as (
        insert into public.subscription_discount_codes (
            code,
            display_name,
            description,
            is_group_code,
            parent_discount_code_id,
            starts_at,
            expires_at,
            max_uses,
            one_use_per_user,
            bypass_payment,
            is_active,
            source_system,
            source_group_code_id,
            source_parent_discount_code_id,
            source_order_id,
            raw_source
        )
        select
            coalesce(rows.code, ''),
            nullif(btrim(rows.display_name), ''),
            nullif(btrim(rows.description), ''),
            true,
            parent.discount_code_id,
            rows.starts_at,
            rows.expires_at,
            case when rows.max_uses <= 0 then 1 else rows.max_uses end,
            true,
            rows.bypass_payment,
            rows.is_active,
            rows.source_system,
            rows.source_group_code_id,
            rows.source_parent_discount_code_id,
            rows.source_order_id,
            rows.raw_source
        from rows
        left join public.subscription_discount_codes parent
            on parent.source_discount_code_id = rows.source_parent_discount_code_id
        where rows.is_group_code
          and rows.source_group_code_id is not null
        on conflict (source_group_code_id) do update
        set code = excluded.code,
            display_name = excluded.display_name,
            description = excluded.description,
            parent_discount_code_id = excluded.parent_discount_code_id,
            starts_at = excluded.starts_at,
            expires_at = excluded.expires_at,
            max_uses = excluded.max_uses,
            one_use_per_user = excluded.one_use_per_user,
            bypass_payment = excluded.bypass_payment,
            is_active = excluded.is_active,
            source_system = excluded.source_system,
            source_parent_discount_code_id = excluded.source_parent_discount_code_id,
            source_order_id = excluded.source_order_id,
            raw_source = excluded.raw_source
        returning 1
    )
    select coalesce((select count(*) from upsert_parent), 0) + coalesce((select count(*) from upsert_group), 0)
    into affected_count;

    update public.subscription_discount_codes child
    set parent_discount_code_id = parent.discount_code_id
    from public.subscription_discount_codes parent
    where child.is_group_code
      and child.source_system = 'wordpress_pmpro'
      and child.source_parent_discount_code_id is not null
      and parent.source_discount_code_id = child.source_parent_discount_code_id
      and child.parent_discount_code_id is distinct from parent.discount_code_id;

    get diagnostics linked_group_count = row_count;

    return coalesce(affected_count, 0) + coalesce(linked_group_count, 0);
end;
$$;

update public.subscription_discount_codes child
set parent_discount_code_id = parent.discount_code_id
from public.subscription_discount_codes parent
where child.is_group_code
  and child.source_system = 'wordpress_pmpro'
  and child.source_parent_discount_code_id is not null
  and parent.source_discount_code_id = child.source_parent_discount_code_id
  and child.parent_discount_code_id is distinct from parent.discount_code_id;
