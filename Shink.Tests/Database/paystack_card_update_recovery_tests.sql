-- Run in the isolated subscription fixture with the recovery migrations applied.
begin;
do $$
declare
    subscription uuid := '22222222-2222-2222-2222-222222222222';
    recovery uuid := '33333333-3333-3333-3333-333333333333';
    row public.subscription_payment_recoveries;
    changed integer;
    subscriber uuid := '11111111-1111-4111-8111-111111111111';
    email text := 'card-update@example.invalid';
    due timestamptz := now() + interval '23 days';
    first_ref text := 'card-update-retry-' || repeat('a',32);
    second_ref text := 'card-update-retry-' || repeat('b',32);
    third_ref text := 'card-update-retry-' || repeat('c',32);
begin
    insert into public.subscribers(subscriber_id,email) values (subscriber,email);
    insert into public.subscriptions(subscription_id, subscriber_id, tier_code, provider, provider_payment_id,
        status,billing_amount_zar,recurring_billing_mode)
    values (subscription, subscriber, 'all_stories_monthly', 'paystack', 'SUB_cardupdate','active',79,'provider_subscription');
    insert into public.subscription_payment_recoveries(recovery_id, subscription_id, provider, provider_payment_id,
        first_failed_at, grace_ends_at, authorization_retry_status, authorization_retry_due_at)
    values (recovery, subscription, 'paystack', 'SUB_cardupdate', now() - interval '6 days', now() - interval '1 day', 'failed', now() - interval '5 days');

    select * into row from public.reserve_customer_payment_recovery(recovery, 'failed', null, first_ref, 79,email,'SUB_cardupdate',due);
    assert row.recovery_id = recovery and row.authorization_retry_status = 'pending', 'Reserve the recovery before provider collection';
    assert row.authorization_retry_reference = first_ref and row.authorization_retry_due_at is null, 'Persist the attempt reference and exclude it from the scheduled retry worker';
    assert not exists (select from public.reserve_customer_payment_recovery(recovery, 'failed', null, second_ref, 79,email,'SUB_cardupdate',due)), 'A distinct verification cannot reserve the same failed renewal';
    assert not exists (select from public.suspend_expired_payment_recovery(recovery)), 'An ambiguous payment must not lose access during expiry';

    -- After a provider-confirmed failure, a different verified card can reserve
    -- the next attempt with a compare-and-swap against the exact prior reference.
    assert not exists(select from public.reserve_customer_payment_recovery(recovery,'pending',first_ref,second_ref,79,email,'SUB_cardupdate',due));
    update public.subscription_payment_recoveries set authorization_retry_status='failed' where recovery_id=recovery;
    select * into row from public.reserve_customer_payment_recovery(recovery, 'failed', first_ref, second_ref, 79,email,'SUB_cardupdate',due);
    assert row.authorization_retry_reference = second_ref;
    update public.subscription_payment_recoveries set authorization_retry_status = 'failed'
     where recovery_id = recovery and authorization_retry_reference = first_ref and resolved_at is null;
    get diagnostics changed = row_count;
    assert changed = 0, 'A stale worker cannot overwrite the next card attempt';
    assert not exists (select from public.reserve_customer_payment_recovery(recovery, 'pending', first_ref, third_ref, 79,email,'SUB_cardupdate',due));

    update public.subscription_payment_recoveries set authorization_retry_status = 'succeeded' where recovery_id = recovery;
    assert not exists (select from public.suspend_expired_payment_recovery(recovery)), 'Succeeded-but-unresolved recovery must preserve paid access after restart';
    assert (select status = 'active' from public.subscriptions where subscription_id = subscription);
    assert not exists (select from public.reserve_customer_payment_recovery(recovery, 'succeeded', second_ref, third_ref, 79,email,'SUB_cardupdate',due));

    update public.subscription_payment_recoveries set resolved_at = now(), resolution = 'recovered' where recovery_id = recovery;
    assert not exists (select from public.suspend_expired_payment_recovery(recovery)), 'A stale expiry snapshot cannot suspend a resolved payment';
    update public.subscription_payment_recoveries set authorization_retry_status = 'failed' where recovery_id = recovery;
    assert not exists (select from public.reserve_customer_payment_recovery(recovery, 'failed', second_ref, third_ref, 79,email,'SUB_cardupdate',due)), 'Resolved debt must never be collected again';

    update public.subscription_payment_recoveries set resolved_at = null, resolution = null where recovery_id = recovery;
    select * into row from public.suspend_expired_payment_recovery(recovery);
    assert row.resolution = 'suspended' and row.resolved_at is not null, 'Genuine expired failed debt must still be suspended';
    assert (select status = 'failed' from public.subscriptions where subscription_id = subscription);

    assert not has_function_privilege('anon', 'public.reserve_paystack_card_update_attempt(uuid,text,text,text)', 'execute');
    assert not has_function_privilege('authenticated', 'public.reserve_paystack_card_update_attempt(uuid,text,text,text)', 'execute');
    assert not has_function_privilege('service_role', 'public.reserve_paystack_card_update_attempt(uuid,text,text,text)', 'execute');
    assert not has_function_privilege('anon', 'public.suspend_expired_payment_recovery(uuid)', 'execute');
    assert not has_function_privilege('authenticated', 'public.suspend_expired_payment_recovery(uuid)', 'execute');
end $$;
rollback;
