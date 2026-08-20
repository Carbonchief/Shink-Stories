create extension if not exists pgcrypto;

create table if not exists public.referral_codes (
    referral_code_id uuid primary key default gen_random_uuid(),
    code text not null unique,
    referrer_name text not null,
    referrer_email text,
    created_by_admin_email text not null,
    created_at timestamptz not null default now(),
    disabled_at timestamptz,
    constraint referral_codes_code_format check (code ~ '^[A-Z0-9]{12}$'),
    constraint referral_codes_referrer_name_length check (char_length(btrim(referrer_name)) between 2 and 120),
    constraint referral_codes_referrer_email_format check (
        referrer_email is null or position('@' in referrer_email) > 1
    ),
    constraint referral_codes_created_by_email_format check (position('@' in created_by_admin_email) > 1)
);

comment on table public.referral_codes is 'Admin-created referral links. Codes are random so links never reveal the referrer identity.';

create index if not exists idx_referral_codes_created_at
    on public.referral_codes(created_at desc);

create table if not exists public.referral_signups (
    referral_signup_id uuid primary key default gen_random_uuid(),
    referral_code_id uuid not null references public.referral_codes(referral_code_id) on delete restrict,
    referred_user_id uuid not null references auth.users(id) on delete cascade,
    created_at timestamptz not null default now(),
    constraint referral_signups_referred_user_unique unique (referred_user_id)
);

comment on table public.referral_signups is 'The first valid referral code supplied when an Auth user is created is locked to that user.';

create index if not exists idx_referral_signups_code_created_at
    on public.referral_signups(referral_code_id, created_at desc);

alter table public.referral_codes enable row level security;
alter table public.referral_signups enable row level security;

revoke all on table public.referral_codes, public.referral_signups from anon, authenticated;
grant select, insert, update, delete on table public.referral_codes, public.referral_signups to service_role;

drop policy if exists referral_codes_service_role_all on public.referral_codes;
create policy referral_codes_service_role_all
on public.referral_codes
for all
to service_role
using (true)
with check (true);

drop policy if exists referral_signups_service_role_all on public.referral_signups;
create policy referral_signups_service_role_all
on public.referral_signups
for all
to service_role
using (true)
with check (true);

create or replace function public.capture_referral_signup_from_auth_user()
returns trigger
language plpgsql
security definer
set search_path = public, pg_temp
as $$
declare
    selected_referral_code_id uuid;
    supplied_code text;
begin
    supplied_code := upper(nullif(btrim(new.raw_user_meta_data ->> 'referral_code'), ''));
    if supplied_code is null or supplied_code !~ '^[A-Z0-9]{12}$' then
        return new;
    end if;

    select referral_code_id
    into selected_referral_code_id
    from public.referral_codes
    where code = supplied_code
      and disabled_at is null
    limit 1;

    if selected_referral_code_id is null then
        return new;
    end if;

    insert into public.referral_signups (referral_code_id, referred_user_id)
    values (selected_referral_code_id, new.id)
    on conflict (referred_user_id) do nothing;

    return new;
end;
$$;

revoke all on function public.capture_referral_signup_from_auth_user() from public, anon, authenticated;

drop trigger if exists trg_auth_users_capture_referral_signup on auth.users;
create trigger trg_auth_users_capture_referral_signup
after insert on auth.users
for each row execute function public.capture_referral_signup_from_auth_user();

create or replace function public.admin_referral_codes_summary()
returns jsonb
language sql
stable
security invoker
set search_path = public, pg_temp
as $$
    select jsonb_build_object(
        'total_referrals', count(referral_code.referral_code_id)::integer,
        'total_signups', coalesce(sum(signup_summary.signup_count), 0)::integer,
        'items', coalesce(
            jsonb_agg(
                jsonb_build_object(
                    'code', referral_code.code,
                    'referrer_name', referral_code.referrer_name,
                    'referrer_email', referral_code.referrer_email,
                    'created_at', referral_code.created_at,
                    'signup_count', signup_summary.signup_count,
                    'last_signup_at', signup_summary.last_signup_at
                )
                order by referral_code.created_at desc
            ),
            '[]'::jsonb
        )
    )
    from public.referral_codes referral_code
    left join lateral (
        select
            count(*)::integer as signup_count,
            max(referral_signup.created_at) as last_signup_at
        from public.referral_signups referral_signup
        where referral_signup.referral_code_id = referral_code.referral_code_id
    ) signup_summary on true;
$$;

revoke all on function public.admin_referral_codes_summary() from public, anon, authenticated;
grant execute on function public.admin_referral_codes_summary() to service_role;
