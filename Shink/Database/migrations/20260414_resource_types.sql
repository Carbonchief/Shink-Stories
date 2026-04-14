create extension if not exists pgcrypto;

create table if not exists public.resource_types (
    resource_type_id uuid primary key default gen_random_uuid(),
    slug text not null unique,
    name text not null,
    description text,
    source_directory text not null,
    sort_order integer not null default 100,
    is_enabled boolean not null default true,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint resource_types_slug_format check (slug ~ '^[a-z0-9]+(?:-[a-z0-9]+)*$'),
    constraint resource_types_slug_not_blank check (btrim(slug) <> ''),
    constraint resource_types_name_not_blank check (btrim(name) <> ''),
    constraint resource_types_source_directory_not_blank check (btrim(source_directory) <> '')
);

comment on table public.resource_types is 'Configurable resource categories displayed on the public resources page.';
comment on column public.resource_types.source_directory is 'Absolute or app-relative directory path scanned for PDF files.';
comment on column public.resource_types.sort_order is 'Lower values appear first on the resources page.';

create index if not exists idx_resource_types_enabled_sort
    on public.resource_types(is_enabled, sort_order, name);

alter table public.resource_types enable row level security;

drop policy if exists resource_types_read_enabled on public.resource_types;
create policy resource_types_read_enabled
on public.resource_types
for select
to anon, authenticated
using (is_enabled = true);

drop policy if exists resource_types_service_role_all on public.resource_types;
create policy resource_types_service_role_all
on public.resource_types
for all
to service_role
using (true)
with check (true);

drop trigger if exists trg_resource_types_set_updated_at on public.resource_types;
create trigger trg_resource_types_set_updated_at
before update on public.resource_types
for each row execute function public.set_updated_at();

insert into public.resource_types (
    slug,
    name,
    description,
    source_directory,
    sort_order,
    is_enabled
)
values
    (
        'aktiwiteite',
        'Aktiwiteite',
        'Drukbare aktiwiteite vir kinders om ná storietyd saam te geniet.',
        '/Users/luanvanderwalt/Downloads/Aktiwiteite',
        10,
        true
    ),
    (
        'storiekaarte',
        'Storiekaarte',
        'Geselskaarte en storiekaarte om waardes en vrae saam deur te werk.',
        '/Users/luanvanderwalt/Downloads/Storiekaarte',
        20,
        true
    )
on conflict (slug) do update
set
    name = excluded.name,
    description = excluded.description,
    source_directory = excluded.source_directory,
    sort_order = excluded.sort_order,
    is_enabled = excluded.is_enabled;
