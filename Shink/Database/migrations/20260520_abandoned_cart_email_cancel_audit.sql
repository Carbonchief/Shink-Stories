alter table public.abandoned_cart_recoveries
    add column if not exists first_email_cancel_status text,
    add column if not exists first_email_cancel_attempted_at timestamptz,
    add column if not exists first_email_cancel_error text,
    add column if not exists second_email_cancel_status text,
    add column if not exists second_email_cancel_attempted_at timestamptz,
    add column if not exists second_email_cancel_error text,
    add column if not exists final_email_cancel_status text,
    add column if not exists final_email_cancel_attempted_at timestamptz,
    add column if not exists final_email_cancel_error text;

alter table public.abandoned_cart_recoveries
    drop constraint if exists chk_abandoned_cart_email_cancel_status;

alter table public.abandoned_cart_recoveries
    add constraint chk_abandoned_cart_email_cancel_status
    check (
        (first_email_cancel_status is null or first_email_cancel_status in ('cancelled', 'failed', 'missing', 'not_cancellable')) and
        (second_email_cancel_status is null or second_email_cancel_status in ('cancelled', 'failed', 'missing', 'not_cancellable')) and
        (final_email_cancel_status is null or final_email_cancel_status in ('cancelled', 'failed', 'missing', 'not_cancellable'))
    );

create index if not exists idx_abandoned_cart_recoveries_resolved_cancel_audit
    on public.abandoned_cart_recoveries(resolved_at)
    where resolved_at is not null
      and (
          first_email_cancel_status is null or
          second_email_cancel_status is null or
          final_email_cancel_status is null
      );
