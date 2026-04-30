alter table public.abandoned_cart_recoveries
    drop constraint if exists abandoned_cart_recoveries_resolution_check;

alter table public.abandoned_cart_recoveries
    add constraint abandoned_cart_recoveries_resolution_check
    check (resolution in ('paid', 'cancelled', 'opted_out', 'expired', 'duplicate_suppressed'));

with ranked_active_recoveries as (
    select
        recovery_id,
        row_number() over (
            partition by source_type, source_key, customer_email
            order by created_at, recovery_id
        ) as active_rank
    from public.abandoned_cart_recoveries
    where resolved_at is null
)
update public.abandoned_cart_recoveries recovery
set
    resolved_at = now(),
    resolution = 'duplicate_suppressed',
    updated_at = now()
from ranked_active_recoveries ranked
where recovery.recovery_id = ranked.recovery_id
  and ranked.active_rank > 1
  and recovery.resolved_at is null;

create unique index if not exists uq_abandoned_cart_recoveries_active_customer_source
    on public.abandoned_cart_recoveries(source_type, source_key, customer_email)
    where resolved_at is null;
