create table if not exists public.auth_sessions (
    session_id uuid primary key,
    email text not null,
    created_at timestamptz not null default now(),
    expires_at timestamptz not null,
    revoked_at timestamptz,
    revoked_reason text,
    user_agent text,
    ip_address text,
    constraint auth_sessions_email_format check (position('@' in email) > 1),
    constraint auth_sessions_expiry_after_created check (expires_at > created_at)
);

create index if not exists idx_auth_sessions_email_active
    on public.auth_sessions(email, revoked_at, expires_at, created_at);

create index if not exists idx_auth_sessions_expires_at
    on public.auth_sessions(expires_at);

alter table public.auth_sessions enable row level security;
