create extension if not exists pgcrypto;

create table if not exists public.admin_users (
    admin_user_id uuid primary key default gen_random_uuid(),
    email text not null unique,
    full_name text,
    is_enabled boolean not null default true,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint admin_users_email_format check (position('@' in email) > 1),
    constraint admin_users_email_not_blank check (btrim(email) <> ''),
    constraint admin_users_full_name_length check (full_name is null or char_length(full_name) between 2 and 120)
);

comment on table public.admin_users is 'Back-office administrators allowed to access the /admin management interface.';
comment on column public.admin_users.email is 'Canonical admin sign-in email address.';
comment on column public.admin_users.is_enabled is 'Soft disable flag to immediately revoke admin access.';

create index if not exists idx_admin_users_enabled_email
    on public.admin_users(is_enabled, email);

alter table public.admin_users enable row level security;

drop policy if exists admin_users_service_role_all on public.admin_users;
create policy admin_users_service_role_all
on public.admin_users
for all
to service_role
using (true)
with check (true);

drop trigger if exists trg_admin_users_set_updated_at on public.admin_users;
create trigger trg_admin_users_set_updated_at
before update on public.admin_users
for each row execute function public.set_updated_at();
