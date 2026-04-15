alter table if exists public.resource_documents
    add column if not exists preview_image_content_type text,
    add column if not exists preview_image_bucket text,
    add column if not exists preview_image_object_key text,
    add column if not exists preview_generated_at timestamptz,
    add column if not exists document_updated_at timestamptz;

update public.resource_documents
set document_updated_at = coalesce(document_updated_at, updated_at, created_at, now())
where document_updated_at is null;

update public.resource_documents
set preview_generated_at = coalesce(preview_generated_at, updated_at, created_at, now())
where preview_image_object_key is not null
  and preview_generated_at is null;

alter table public.resource_documents
    alter column document_updated_at set default now();

alter table public.resource_documents
    alter column document_updated_at set not null;

alter table public.resource_documents
    drop constraint if exists resource_documents_preview_metadata_valid;

alter table public.resource_documents
    add constraint resource_documents_preview_metadata_valid
    check (
        (
            nullif(btrim(coalesce(preview_image_content_type, '')), '') is null and
            nullif(btrim(coalesce(preview_image_bucket, '')), '') is null and
            nullif(btrim(coalesce(preview_image_object_key, '')), '') is null
        )
        or
        (
            lower(coalesce(preview_image_content_type, '')) = 'image/png' and
            nullif(btrim(coalesce(preview_image_bucket, '')), '') is not null and
            nullif(btrim(coalesce(preview_image_object_key, '')), '') is not null
        )
    );

create unique index if not exists idx_resource_documents_preview_object_key_unique
    on public.resource_documents(preview_image_object_key)
    where preview_image_object_key is not null;

comment on column public.resource_documents.preview_image_content_type is 'Content type for the generated preview image stored alongside the PDF in Cloudflare R2.';
comment on column public.resource_documents.preview_image_bucket is 'Cloudflare R2 bucket containing the generated preview image.';
comment on column public.resource_documents.preview_image_object_key is 'Cloudflare R2 object key for the generated preview image.';
comment on column public.resource_documents.preview_generated_at is 'Timestamp when the current preview image was generated.';
comment on column public.resource_documents.document_updated_at is 'Public-facing document update timestamp used on the resources page.';
