begin;

-- The settings overview needs totals. When searching, the same lookup returns
-- only code IDs with matching emails, including redemptions on child codes.
create or replace function public.admin_discount_use_counts(p_email_search text default '')
returns jsonb language sql stable security invoker set search_path = '' as $$
    select coalesce(jsonb_object_agg(discount_code_id, uses), '{}'::jsonb)
    from (
        select discount_code_id, count(*) as uses
        from public.subscription_discount_code_redemptions
        where p_email_search = '' or strpos(lower(email), lower(p_email_search)) > 0
        group by discount_code_id
    ) as counts;
$$;

create or replace function public.admin_discount_subcode_users(p_parent_id uuid)
returns jsonb language sql stable security invoker set search_path = '' as $$
    select coalesce(jsonb_object_agg(discount_code_id, emails), '{}'::jsonb)
    from (
        select discount_code_id, string_agg(email, ', ' order by last_use desc, email) as emails
        from (
            select c.discount_code_id, lower(btrim(r.email)) as email, max(r.redeemed_at) as last_use
            from public.subscription_discount_codes c
            join public.subscription_discount_code_redemptions r using (discount_code_id)
            where c.parent_discount_code_id = p_parent_id and c.is_group_code
            group by c.discount_code_id, lower(btrim(r.email))
        ) as users
        group by discount_code_id
    ) as subcodes;
$$;

create or replace function public.admin_blog_list_page(p_search text, p_status text, p_page integer)
returns jsonb language sql stable security invoker set search_path = '' as $$
    with matches as materialized (
        select post_id, title, is_published, published_at, updated_at
        from public.blog_posts
        where case p_status
            when 'draft' then not is_published
            when 'published' then is_published and published_at <= now()
            when 'scheduled' then is_published and published_at > now()
            else true end
          and (p_search = '' or strpos(lower(title), lower(p_search)) > 0
                or strpos(lower(slug), lower(p_search)) > 0
                or strpos(lower(coalesce(nullif(left(btrim(summary), 600), ''), left(btrim(plain_text_content), 240))), lower(p_search)) > 0)
    ), bounds as (
        select count(*) as total, least(greatest(0, p_page), greatest(0, count(*) - 1) / 25) as page
        from matches
    ), items as (
        select post_id as "postId", title, is_published as "isPublished",
               published_at as "publishedAt", updated_at as "updatedAt"
        from matches
        order by updated_at desc, published_at desc nulls last, lower(title), post_id
        limit 25 offset (select page * 25 from bounds)
    )
    select jsonb_build_object('total', total, 'page', page, 'items',
        coalesce((select jsonb_agg(i order by i."updatedAt" desc, i."publishedAt" desc nulls last, lower(i.title), i."postId") from items i), '[]'::jsonb))
    from bounds;
$$;

revoke all on function public.admin_discount_use_counts(text), public.admin_discount_subcode_users(uuid),
    public.admin_blog_list_page(text, text, integer) from public, anon, authenticated;
grant execute on function public.admin_discount_use_counts(text), public.admin_discount_subcode_users(uuid),
    public.admin_blog_list_page(text, text, integer) to service_role;

commit;
