alter table public.subscription_payment_recoveries
    add column if not exists authorization_retry_due_at timestamptz,
    add column if not exists authorization_retry_attempted_at timestamptz,
    add column if not exists authorization_retry_status text,
    add column if not exists authorization_retry_reference text,
    add column if not exists authorization_retry_error text,
    add column if not exists emails_scheduled_at timestamptz;

do $$
begin
    if not exists (
        select 1
        from pg_constraint
        where conname = 'chk_subscription_payment_recoveries_authorization_retry_status'
          and conrelid = 'public.subscription_payment_recoveries'::regclass
    ) then
        alter table public.subscription_payment_recoveries
            add constraint chk_subscription_payment_recoveries_authorization_retry_status
            check (
                authorization_retry_status is null
                or authorization_retry_status in ('pending', 'succeeded', 'failed', 'skipped')
            );
    end if;
end $$;

create index if not exists idx_subscription_payment_recoveries_authorization_retry
    on public.subscription_payment_recoveries(authorization_retry_due_at)
    where resolved_at is null
      and authorization_retry_status = 'pending';
