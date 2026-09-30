-- Landing page drafts and their independently published content snapshots.
-- No row-deletion privilege or policy is granted to the application service role.
create table if not exists public.landing_pages (
    page_id uuid primary key default gen_random_uuid(),
    slug text not null,
    draft_content jsonb not null default '{"schemaVersion":1,"title":"","backgroundColor":"#ff7133","textColor":"#ffffff","sharingTitle":"","sharingDescription":"","blocks":[]}'::jsonb,
    published_content jsonb,
    is_published boolean not null default false,
    has_ever_published boolean not null default false,
    revision bigint not null default 1,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint landing_pages_slug_key unique (slug),
    constraint landing_pages_slug_format check (slug ~ '^[a-z0-9]+(?:-[a-z0-9]+)*$'),
    constraint landing_pages_draft_object check (jsonb_typeof(draft_content) = 'object'),
    constraint landing_pages_published_object check (published_content is null or jsonb_typeof(published_content) = 'object'),
    constraint landing_pages_revision_positive check (revision >= 1),
    constraint landing_pages_publish_snapshot_required check (
        not has_ever_published or published_content is not null
    ),
    constraint landing_pages_published_state_valid check (
        not is_published or (has_ever_published and published_content is not null)
    )
);

comment on table public.landing_pages is
    'Admin-managed landing pages with a private draft and a separate published JSON snapshot.';
comment on column public.landing_pages.draft_content is
    'Latest editable content; edits never change published_content.';
comment on column public.landing_pages.published_content is
    'Snapshot copied from a validated draft by one conditional publish update.';
comment on column public.landing_pages.revision is
    'Optimistic concurrency token incremented once for every row update.';
comment on column public.landing_pages.has_ever_published is
    'Keeps the slug immutable after the first successful publish, including after unpublish.';

create index if not exists landing_pages_published_slug_idx
    on public.landing_pages (slug)
    where is_published = true;

create or replace function public.guard_landing_page_revision_and_slug()
returns trigger
language plpgsql
set search_path = ''
as $$
begin
    if new.revision <> old.revision + 1 then
        raise exception 'landing_page_revision_must_increment_once' using errcode = '23514';
    end if;

    if old.has_ever_published and new.slug is distinct from old.slug then
        raise exception 'landing_page_slug_immutable' using errcode = '23514';
    end if;

    if old.has_ever_published and not new.has_ever_published then
        raise exception 'landing_page_publish_history_is_immutable' using errcode = '23514';
    end if;

    return new;
end;
$$;

revoke all on function public.guard_landing_page_revision_and_slug() from public, anon, authenticated, service_role;

create or replace trigger guard_landing_page_revision_and_slug
before update on public.landing_pages
for each row execute function public.guard_landing_page_revision_and_slug();

alter table public.landing_pages enable row level security;

do $$
begin
    if not exists (
        select 1 from pg_policies
        where schemaname = 'public'
          and tablename = 'landing_pages'
          and policyname = 'landing_pages_service_role_select'
    ) then
        execute 'create policy landing_pages_service_role_select on public.landing_pages for select to service_role using (true)';
    end if;

    if not exists (
        select 1 from pg_policies
        where schemaname = 'public'
          and tablename = 'landing_pages'
          and policyname = 'landing_pages_service_role_insert'
    ) then
        execute 'create policy landing_pages_service_role_insert on public.landing_pages for insert to service_role with check (true)';
    end if;

    if not exists (
        select 1 from pg_policies
        where schemaname = 'public'
          and tablename = 'landing_pages'
          and policyname = 'landing_pages_service_role_update'
    ) then
        execute 'create policy landing_pages_service_role_update on public.landing_pages for update to service_role using (true) with check (true)';
    end if;
end;
$$;

revoke all on table public.landing_pages from public, anon, authenticated, service_role;
grant select, insert, update on table public.landing_pages to service_role;
