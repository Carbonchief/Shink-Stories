alter table public.subscription_payment_recoveries
    add column if not exists authorization_retry_paid_through timestamptz,
    add column if not exists authorization_retry_check_after timestamptz,
    add column if not exists authorization_retry_follow_up boolean not null default false;

create index if not exists idx_paystack_recovery_checks
    on public.subscription_payment_recoveries (authorization_retry_check_after nulls first, recovery_id)
    where provider = 'paystack' and resolved_at is null and authorization_retry_status in ('pending','succeeded');

-- Poll outstanding provider references independently of the collection schedule.
-- Moving the next check before releasing the lock keeps unresolved payments from starving later rows.
create or replace function public.claim_payment_recovery_checks()
returns setof public.subscription_payment_recoveries language sql security invoker set search_path = '' as $$
    with due as (
        select recovery_id from public.subscription_payment_recoveries
         where provider = 'paystack' and resolved_at is null and authorization_retry_status in ('pending','succeeded')
           and ((authorization_retry_reference is not null and (authorization_retry_due_at is null or authorization_retry_status='succeeded'))
                or (authorization_retry_status='pending' and authorization_retry_due_at <= now()))
           and coalesce(authorization_retry_check_after,'-infinity') <= now()
         order by authorization_retry_check_after nulls first, recovery_id
         limit 100 for update skip locked
    )
    update public.subscription_payment_recoveries r set authorization_retry_check_after = now() + interval '5 minutes'
      from due where r.recovery_id = due.recovery_id returning r.*;
$$;
revoke all on function public.claim_payment_recovery_checks() from public, anon, authenticated;
grant execute on function public.claim_payment_recovery_checks() to service_role;

-- The former card-only RPC could replace an outstanding reference without checking the invoice.
revoke execute on function public.reserve_paystack_card_update_attempt(uuid,text,text,text) from service_role;

-- Reserve the existing unpaid renewal without granting any access. A repeat
-- request can only use its reserved reference until Paystack settles it.
create or replace function public.reserve_customer_payment_recovery(
    p_recovery_id uuid, p_expected_status text, p_expected_reference text, p_attempt_reference text,
    p_amount_zar numeric, p_email text, p_provider_payment_id text, p_paid_through timestamptz)
returns setof public.subscription_payment_recoveries
language sql security invoker set search_path = '' as $$
    update public.subscription_payment_recoveries r
       set authorization_retry_status = 'pending', authorization_retry_reference = p_attempt_reference,
           authorization_retry_attempted_at = now(), authorization_retry_due_at = null,
           authorization_retry_paid_through = p_paid_through,
           authorization_retry_check_after = now() + interval '5 minutes',
           authorization_retry_follow_up = r.authorization_retry_follow_up or r.authorization_retry_attempted_at is not null,
           resolved_at = null, resolution = null
      from public.subscriptions s join public.subscribers u using (subscriber_id)
     where r.recovery_id = p_recovery_id and r.subscription_id = s.subscription_id
       and r.provider = 'paystack' and s.provider = 'paystack' and s.provider_payment_id = r.provider_payment_id
       and s.status in ('active', 'failed') and s.cancelled_at is null and u.disabled_at is null
       and s.billing_amount_zar = p_amount_zar and p_amount_zar > 0 and lower(u.email) = lower(p_email)
       and s.provider_payment_id = p_provider_payment_id and s.recurring_billing_mode = 'provider_subscription'
       and (r.resolved_at is null or (r.resolution = 'suspended' and s.status = 'failed'))
       and r.authorization_retry_status is not distinct from p_expected_status
       and r.authorization_retry_reference is not distinct from p_expected_reference
       and p_paid_through > now()
       and ((r.authorization_retry_status is null and r.authorization_retry_reference is null)
            or r.authorization_retry_status in ('failed','skipped') or
            (r.authorization_retry_status = 'pending' and r.authorization_retry_due_at is not null
             and ((p_attempt_reference like 'retry-%' and r.authorization_retry_due_at <= now())
                  or p_attempt_reference like 'customer-retry-%')))
       and (p_attempt_reference ~ '^(customer-retry-|card-update-retry-)[a-f0-9]{32}$'
            or p_attempt_reference = 'retry-' || to_char(r.authorization_retry_due_at at time zone 'UTC','YYYYMMDDHH24MISS') || '-' || r.recovery_id::text)
       and p_attempt_reference is distinct from r.authorization_retry_reference
       and not exists (select from public.subscription_payment_recoveries newer where newer.subscription_id = s.subscription_id
                       and newer.recovery_id <> r.recovery_id and (newer.first_failed_at >= r.first_failed_at or newer.resolved_at is null))
       and not exists (select from public.subscriptions other where other.subscriber_id = s.subscriber_id
                       and other.subscription_id <> s.subscription_id and other.tier_code <> 'gratis' and other.status = 'active')
       and not exists (select from public.subscription_payment_pauses p where p.subscriber_id = s.subscriber_id and p.status in ('active','resume_pending'))
       and not exists (select from public.subscription_plan_changes c where c.subscriber_id = s.subscriber_id and c.status not in ('completed','cancelled','failed'))
       and not exists (select from public.paystack_checkout_sessions c where c.customer_email = u.email
                       and c.checkout_kind = 'subscription' and c.status = 'pending' and c.expires_at > now())
    returning r.*;
$$;
revoke all on function public.reserve_customer_payment_recovery(uuid,text,text,text,numeric,text,text,timestamptz) from public, anon, authenticated;
grant execute on function public.reserve_customer_payment_recovery(uuid,text,text,text,numeric,text,text,timestamptz) to service_role;

-- The immediate response and the signed webhook may arrive in either order.
create or replace function public.complete_customer_payment_recovery(
    p_recovery_id uuid, p_subscription_id uuid, p_reference text, p_amount_zar numeric,
    p_email text, p_provider_payment_id text, p_next_payment timestamptz)
returns boolean
language plpgsql security invoker set search_path = '' as $$
declare
    debt public.subscription_payment_recoveries;
begin
    select * into debt from public.subscription_payment_recoveries where recovery_id = p_recovery_id
       and subscription_id = p_subscription_id and provider = 'paystack' and provider_payment_id = p_provider_payment_id
       for update;
    if not found or p_reference is null or p_next_payment is null or p_amount_zar is null or p_amount_zar <= 0 then return false; end if;
    if debt.resolution = 'recovered' then
        return debt.authorization_retry_status = 'succeeded' and debt.authorization_retry_reference = p_reference
           and debt.authorization_retry_paid_through is not distinct from p_next_payment
           and exists (select from public.subscriptions s join public.subscribers u using (subscriber_id)
                        where s.subscription_id=p_subscription_id and lower(u.email)=lower(p_email)
                          and s.provider='paystack' and s.provider_payment_id=p_provider_payment_id and s.billing_amount_zar=p_amount_zar);
    end if;
    if debt.resolution is not null and debt.resolution <> 'suspended' then return false; end if;
    if exists (select from public.subscription_payment_recoveries newer
               where newer.subscription_id = p_subscription_id and newer.recovery_id <> p_recovery_id
               and (newer.first_failed_at >= debt.first_failed_at or newer.resolved_at is null))
    then return false; end if;
    if debt.authorization_retry_reference is distinct from p_reference then
        -- Legacy workers could lose a queued reference. A different stored reference
        -- can be superseded only by a verified receipt tied to that exact prior attempt.
        if not exists (
            select from public.subscription_events e where e.subscription_id = p_subscription_id
            and e.provider = 'paystack' and e.provider_payment_id = p_provider_payment_id
            and e.received_at >= debt.first_failed_at
            and ((e.event_type in ('paystack.verified_recovery_invoice','paystack.verified_recovery_payment')
                  and e.payload->>'recovery_id' = p_recovery_id::text
                  and e.payload->>'previous_reference' is not distinct from debt.authorization_retry_reference)
                 or (debt.authorization_retry_reference is null and
                     e.event_type in ('paystack.authorization_retry','paystack.card_update_retry','paystack.card_update_refund_retry','charge.success')))
            and coalesce(e.payload #>> '{data,reference}',e.payload->>'retry_reference',e.payload->>'reference') = p_reference
        ) then return false; end if;
    end if;
    if debt.authorization_retry_paid_through is not null and debt.authorization_retry_paid_through <> p_next_payment
       then return false; end if;
    if p_next_payment <= now() then return false; end if;

    -- Serialize against inserts (subscriber FK) and updates of existing agreements.
    perform 1 from public.subscribers u join public.subscriptions s using (subscriber_id)
      where s.subscription_id = p_subscription_id for update of u;
    perform 1 from public.subscriptions where subscriber_id = (
        select subscriber_id from public.subscriptions where subscription_id = p_subscription_id)
      order by subscription_id for update;

    update public.subscriptions s set status = 'active', next_renewal_at = p_next_payment, updated_at = now()
      from public.subscribers u where s.subscription_id = p_subscription_id and s.subscriber_id = u.subscriber_id
       and lower(u.email) = lower(p_email) and u.disabled_at is null and s.cancelled_at is null
       and s.status in ('active','failed') and s.billing_amount_zar = p_amount_zar
       and s.provider = 'paystack' and s.provider_payment_id = p_provider_payment_id
       and not exists (select from public.subscriptions other where other.subscriber_id=s.subscriber_id
                       and other.subscription_id<>s.subscription_id and other.tier_code<>'gratis' and other.status='active')
       and not exists (select from public.subscription_payment_pauses p where p.subscriber_id=s.subscriber_id and p.status in ('active','resume_pending'))
       and not exists (select from public.subscription_plan_changes c where c.subscriber_id=s.subscriber_id and c.status not in ('completed','cancelled','failed'));
    if not found then return false; end if;
    update public.subscription_payment_recoveries set resolution = 'recovered', resolved_at = now(),
        authorization_retry_status = 'succeeded', authorization_retry_reference = p_reference, authorization_retry_paid_through = p_next_payment,
        authorization_retry_error = null, authorization_retry_due_at = null
      where recovery_id = p_recovery_id;
    return true;
end;
$$;
revoke all on function public.complete_customer_payment_recovery(uuid,uuid,text,numeric,text,text,timestamptz) from public, anon, authenticated;
grant execute on function public.complete_customer_payment_recovery(uuid,uuid,text,numeric,text,text,timestamptz) to service_role;
