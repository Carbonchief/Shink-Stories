create table if not exists public.app_error_logs (
    error_log_id uuid primary key default gen_random_uuid(),
    occurred_at timestamptz not null default now(),
    level text not null,
    category text not null,
    event_id integer,
    event_name text,
    message text not null,
    exception_text text,
    request_method text,
    request_path text,
    trace_identifier text,
    user_email text,
    environment_name text,
    machine_name text,
    metadata jsonb not null default '{}'::jsonb,
    created_at timestamptz not null default now(),
    constraint app_error_logs_level_not_blank check (btrim(level) <> ''),
    constraint app_error_logs_category_not_blank check (btrim(category) <> ''),
    constraint app_error_logs_message_not_blank check (btrim(message) <> '')
);

create index if not exists idx_app_error_logs_occurred_at
    on public.app_error_logs(occurred_at desc);

create index if not exists idx_app_error_logs_level_occurred_at
    on public.app_error_logs(level, occurred_at desc);

create index if not exists idx_app_error_logs_category_occurred_at
    on public.app_error_logs(category, occurred_at desc);

create index if not exists idx_app_error_logs_trace_identifier
    on public.app_error_logs(trace_identifier)
    where trace_identifier is not null;

alter table public.app_error_logs enable row level security;
