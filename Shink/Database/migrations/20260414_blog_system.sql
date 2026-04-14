create extension if not exists pgcrypto;

create table if not exists public.blog_categories (
    category_id uuid primary key default gen_random_uuid(),
    slug text not null unique,
    name text not null,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint blog_categories_slug_format check (slug ~ '^[a-z0-9]+(?:-[a-z0-9]+)*$'),
    constraint blog_categories_slug_not_blank check (btrim(slug) <> ''),
    constraint blog_categories_name_not_blank check (btrim(name) <> '')
);

create table if not exists public.blog_tags (
    tag_id uuid primary key default gen_random_uuid(),
    slug text not null unique,
    name text not null,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint blog_tags_slug_format check (slug ~ '^[a-z0-9]+(?:-[a-z0-9]+)*$'),
    constraint blog_tags_slug_not_blank check (btrim(slug) <> ''),
    constraint blog_tags_name_not_blank check (btrim(name) <> '')
);

create table if not exists public.blog_posts (
    post_id uuid primary key default gen_random_uuid(),
    slug text not null unique,
    title text not null,
    summary text,
    plain_text_content text not null default '',
    content_markdown text not null,
    content_html text not null default '',
    featured_image_url text,
    author_name text,
    category_id uuid references public.blog_categories(category_id) on delete set null,
    is_published boolean not null default false,
    published_at timestamptz,
    seo_title text,
    seo_description text,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint blog_posts_slug_format check (slug ~ '^[a-z0-9]+(?:-[a-z0-9]+)*$'),
    constraint blog_posts_slug_not_blank check (btrim(slug) <> ''),
    constraint blog_posts_title_not_blank check (btrim(title) <> ''),
    constraint blog_posts_content_markdown_not_blank check (btrim(content_markdown) <> ''),
    constraint blog_posts_plain_text_not_blank check (btrim(plain_text_content) <> ''),
    constraint blog_posts_published_at_required check (not is_published or published_at is not null)
);

create table if not exists public.blog_post_tags (
    post_id uuid not null references public.blog_posts(post_id) on delete cascade,
    tag_id uuid not null references public.blog_tags(tag_id) on delete cascade,
    created_at timestamptz not null default now(),
    primary key (post_id, tag_id)
);

comment on table public.blog_categories is 'Blog categories used by public /blog pages and admin management.';
comment on table public.blog_tags is 'Blog tags assigned to posts for filtering and discovery.';
comment on table public.blog_posts is 'Markdown-authored blog posts rendered on the public Schink Stories site.';
comment on table public.blog_post_tags is 'Many-to-many relationship between blog posts and tags.';
comment on column public.blog_posts.plain_text_content is 'Sanitized plain-text version of the markdown body for search and reading-time calculations.';
comment on column public.blog_posts.content_html is 'Sanitized HTML generated from the markdown body.';
comment on column public.blog_posts.is_published is 'Admin-controlled publication flag. Future-dated posts stay hidden until published_at.';

create index if not exists idx_blog_posts_public_publish
    on public.blog_posts(published_at desc, created_at desc)
    where is_published = true;

create index if not exists idx_blog_posts_category_id
    on public.blog_posts(category_id);

create index if not exists idx_blog_post_tags_tag_id
    on public.blog_post_tags(tag_id);

create index if not exists idx_blog_categories_name
    on public.blog_categories(name);

create index if not exists idx_blog_tags_name
    on public.blog_tags(name);

alter table public.blog_categories enable row level security;
alter table public.blog_tags enable row level security;
alter table public.blog_posts enable row level security;
alter table public.blog_post_tags enable row level security;

drop policy if exists blog_categories_public_select on public.blog_categories;
create policy blog_categories_public_select
on public.blog_categories
for select
to anon, authenticated
using (true);

drop policy if exists blog_tags_public_select on public.blog_tags;
create policy blog_tags_public_select
on public.blog_tags
for select
to anon, authenticated
using (true);

drop policy if exists blog_posts_public_select on public.blog_posts;
create policy blog_posts_public_select
on public.blog_posts
for select
to anon, authenticated
using (
    is_published = true
    and published_at is not null
    and published_at <= now()
);

drop policy if exists blog_post_tags_public_select on public.blog_post_tags;
create policy blog_post_tags_public_select
on public.blog_post_tags
for select
to anon, authenticated
using (
    exists (
        select 1
        from public.blog_posts
        where blog_posts.post_id = blog_post_tags.post_id
          and blog_posts.is_published = true
          and blog_posts.published_at is not null
          and blog_posts.published_at <= now()
    )
);

drop policy if exists blog_categories_service_role_all on public.blog_categories;
create policy blog_categories_service_role_all
on public.blog_categories
for all
to service_role
using (true)
with check (true);

drop policy if exists blog_tags_service_role_all on public.blog_tags;
create policy blog_tags_service_role_all
on public.blog_tags
for all
to service_role
using (true)
with check (true);

drop policy if exists blog_posts_service_role_all on public.blog_posts;
create policy blog_posts_service_role_all
on public.blog_posts
for all
to service_role
using (true)
with check (true);

drop policy if exists blog_post_tags_service_role_all on public.blog_post_tags;
create policy blog_post_tags_service_role_all
on public.blog_post_tags
for all
to service_role
using (true)
with check (true);

drop trigger if exists trg_blog_categories_set_updated_at on public.blog_categories;
create trigger trg_blog_categories_set_updated_at
before update on public.blog_categories
for each row execute function public.set_updated_at();

drop trigger if exists trg_blog_tags_set_updated_at on public.blog_tags;
create trigger trg_blog_tags_set_updated_at
before update on public.blog_tags
for each row execute function public.set_updated_at();

drop trigger if exists trg_blog_posts_set_updated_at on public.blog_posts;
create trigger trg_blog_posts_set_updated_at
before update on public.blog_posts
for each row execute function public.set_updated_at();
