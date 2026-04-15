create extension if not exists pgcrypto;

alter table public.resource_types
    alter column source_directory drop not null;

alter table public.resource_types
    drop constraint if exists resource_types_source_directory_not_blank;

comment on column public.resource_types.source_directory is 'Legacy local-folder source path. New resource uploads are stored in Cloudflare R2.';

create table if not exists public.resource_documents (
    resource_document_id uuid primary key default gen_random_uuid(),
    resource_type_id uuid not null references public.resource_types(resource_type_id) on delete cascade,
    slug text not null,
    title text not null,
    description text,
    file_name text not null,
    content_type text not null default 'application/pdf',
    size_bytes bigint not null default 0,
    storage_provider text not null default 'r2',
    storage_bucket text not null,
    storage_object_key text not null,
    sort_order integer not null default 100,
    is_enabled boolean not null default true,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint resource_documents_slug_format check (slug ~ '^[a-z0-9]+(?:-[a-z0-9]+)*$'),
    constraint resource_documents_slug_not_blank check (btrim(slug) <> ''),
    constraint resource_documents_title_not_blank check (btrim(title) <> ''),
    constraint resource_documents_file_name_not_blank check (btrim(file_name) <> ''),
    constraint resource_documents_storage_provider check (lower(storage_provider) = 'r2'),
    constraint resource_documents_storage_bucket_not_blank check (btrim(storage_bucket) <> ''),
    constraint resource_documents_storage_object_key_not_blank check (btrim(storage_object_key) <> ''),
    constraint resource_documents_pdf_content_type check (lower(content_type) = 'application/pdf'),
    constraint resource_documents_non_negative_size check (size_bytes >= 0),
    constraint resource_documents_resource_type_id_slug_key unique (resource_type_id, slug),
    constraint resource_documents_storage_object_key_key unique (storage_object_key)
);

comment on table public.resource_documents is 'Uploaded PDF resources stored in Cloudflare R2 with searchable metadata in Supabase.';
comment on column public.resource_documents.resource_type_id is 'Owning resource type displayed on the public resources page.';
comment on column public.resource_documents.storage_object_key is 'Cloudflare R2 object key for the uploaded PDF.';

create index if not exists idx_resource_documents_type_enabled_sort
    on public.resource_documents(resource_type_id, is_enabled, sort_order, title);

create index if not exists idx_resource_documents_search
    on public.resource_documents
    using gin (to_tsvector('simple', coalesce(title, '') || ' ' || coalesce(description, '') || ' ' || coalesce(file_name, '')));

alter table public.resource_documents enable row level security;

drop policy if exists resource_documents_read_enabled on public.resource_documents;
create policy resource_documents_read_enabled
on public.resource_documents
for select
to anon, authenticated
using (
    is_enabled = true and exists (
        select 1
        from public.resource_types resource_type
        where resource_type.resource_type_id = resource_documents.resource_type_id
          and resource_type.is_enabled = true
    )
);

drop policy if exists resource_documents_service_role_all on public.resource_documents;
create policy resource_documents_service_role_all
on public.resource_documents
for all
to service_role
using (true)
with check (true);

drop trigger if exists trg_resource_documents_set_updated_at on public.resource_documents;
create trigger trg_resource_documents_set_updated_at
before update on public.resource_documents
for each row execute function public.set_updated_at();
