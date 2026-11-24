-- Run with psql against an empty disposable local database.
\set ON_ERROR_STOP on
do $$
begin
    if current_database() <> 'shink_admin_read_regression' or inet_server_addr() is not null then
        raise exception 'Use a disposable local socket database named shink_admin_read_regression';
    end if;
    if not exists (select from pg_roles where rolname = 'anon') then create role anon; end if;
    if not exists (select from pg_roles where rolname = 'authenticated') then create role authenticated; end if;
    if not exists (select from pg_roles where rolname = 'service_role') then create role service_role; end if;
end $$;

create table public.blog_posts(post_id uuid primary key, title text, slug text, summary text, plain_text_content text,
    is_published boolean, published_at timestamptz, updated_at timestamptz);
create table public.subscription_discount_codes(discount_code_id uuid primary key, parent_discount_code_id uuid, is_group_code boolean);
create table public.subscription_discount_code_redemptions(discount_code_id uuid, email text, redeemed_at timestamptz);

\ir ../migrations/20261124120000_admin_settings_blog_reads.sql

insert into public.blog_posts
select md5(i::text)::uuid, 'Post ' || i, 'post-' || i, case when i = 40 then 'Code 40%_off' end,
       'Fallback ' || i, i % 3 <> 0, case i % 3 when 1 then now() - interval '1 day' when 2 then now() + interval '1 day' end,
       date '2026-01-01' + i
from generate_series(1,55) i;

insert into public.subscription_discount_codes values (md5('parent')::uuid, null, false), (md5('child')::uuid, md5('parent')::uuid, true);
insert into public.subscription_discount_code_redemptions
select md5('child')::uuid, case when i = 1 then 'Older@Example.test' else 'other@example.test' end,
       date '2020-01-01' + interval '1 minute' * i from generate_series(1,20001) i;
grant select on all tables in schema public to service_role;

do $$
declare f regprocedure;
begin
    foreach f in array array['public.admin_discount_use_counts(text)'::regprocedure, 'public.admin_discount_subcode_users(uuid)'::regprocedure,
                              'public.admin_blog_list_page(text,text,integer)'::regprocedure]
    loop
        if has_function_privilege('anon', f, 'execute') or has_function_privilege('authenticated', f, 'execute')
            or not has_function_privilege('service_role', f, 'execute') then
            raise exception 'Incorrect grants for %', f;
        end if;
    end loop;
end $$;

set role service_role;
do $$
declare first_page jsonb := public.admin_blog_list_page('', 'all', 0);
        second_page jsonb := public.admin_blog_list_page('', 'all', 1);
        last_page jsonb := public.admin_blog_list_page('', 'all', 999);
        child text := md5('child')::uuid::text;
begin
    if (first_page->>'total')::int <> 55 or jsonb_array_length(first_page->'items') <> 25
       or first_page#>>'{items,0,title}' <> 'Post 55' or second_page#>>'{items,0,title}' <> 'Post 30'
       or jsonb_array_length(last_page->'items') <> 5 or (last_page->>'page')::int <> 2
       or (select count(*) from jsonb_object_keys(first_page#>'{items,0}')) <> 5 then
        raise exception 'Blog pages have incorrect bounds, order or projections';
    end if;
    if public.admin_blog_list_page('%_', 'all', 0)#>>'{items,0,title}' <> 'Post 40'
        or (public.admin_blog_list_page('fallback 55', 'all', 0)->>'total')::int <> 1
        or (public.admin_blog_list_page('', 'draft', 0)->>'total')::int <> 18
        or (public.admin_blog_list_page('', 'published', 0)->>'total')::int <> 19
        or (public.admin_blog_list_page('', 'scheduled', 0)->>'total')::int <> 18
        or jsonb_array_length(public.admin_blog_list_page('absent', 'all', 5)->'items') <> 0 then
        raise exception 'Blog searches and publication filters must cover every page';
    end if;
    if public.admin_discount_use_counts('')->>child is distinct from '20001'
        or public.admin_discount_use_counts('OLDER@')->>child is distinct from '1'
        or public.admin_discount_use_counts('%') <> '{}'::jsonb
        or public.admin_discount_subcode_users(md5('parent')::uuid)->>child is distinct from 'other@example.test, older@example.test' then
        raise exception 'Incorrect redemption totals, search or distinct subcode users';
    end if;
end $$;
reset role;
