alter table if exists public.resource_documents
    add column if not exists required_tier_code text;

update public.resource_documents
set required_tier_code = nullif(lower(btrim(required_tier_code)), '')
where required_tier_code is not null;

alter table public.resource_documents
    drop constraint if exists resource_documents_required_tier_code_format;

alter table public.resource_documents
    add constraint resource_documents_required_tier_code_format
    check (
        required_tier_code is null
        or (
            btrim(required_tier_code) <> ''
            and required_tier_code ~ '^[a-z0-9_]+$'
        )
    );

do $$
begin
    if not exists (
        select 1
        from pg_constraint
        where conname = 'resource_documents_required_tier_code_fkey'
          and conrelid = 'public.resource_documents'::regclass
    ) then
        alter table public.resource_documents
            add constraint resource_documents_required_tier_code_fkey
            foreign key (required_tier_code)
            references public.subscription_tiers(tier_code)
            on delete set null;
    end if;
end;
$$;

create index if not exists idx_resource_documents_required_tier
    on public.resource_documents(required_tier_code)
    where required_tier_code is not null;

comment on column public.resource_documents.required_tier_code is 'Optional subscription tier required to open/download this resource document.';
