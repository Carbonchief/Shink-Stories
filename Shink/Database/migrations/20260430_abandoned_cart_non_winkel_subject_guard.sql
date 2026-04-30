drop index if exists public.uq_abandoned_cart_recoveries_active_customer_source;

with ranked_active_recoveries as (
    select
        recovery_id,
        row_number() over (
            partition by customer_email, source_type
            order by created_at, recovery_id
        ) as active_rank
    from public.abandoned_cart_recoveries
    where resolved_at is null
      and source_type <> 'store_order'
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

create unique index if not exists uq_abandoned_cart_recoveries_active_non_winkel_subject
    on public.abandoned_cart_recoveries(customer_email, source_type)
    where resolved_at is null
      and source_type <> 'store_order';
