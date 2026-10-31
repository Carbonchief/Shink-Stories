-- Isolated test database only: subscriptions fixture needs subscription_id/status.
-- Apply the recovery schema and both pending billing migrations first.
begin;
do $$
declare
    subscription uuid := '22222222-2222-2222-2222-222222222222';
    recovery uuid := '33333333-3333-3333-3333-333333333333';
    row public.subscription_payment_recoveries;
    changed integer;
begin
    insert into public.subscriptions(subscription_id, status) values (subscription, 'active');
    insert into public.subscription_payment_recoveries(recovery_id, subscription_id, provider, provider_payment_id,
        first_failed_at, grace_ends_at, authorization_retry_status, authorization_retry_due_at)
    values (recovery, subscription, 'paystack', 'SUB_cardupdate', now() - interval '6 days', now() - interval '1 day', 'failed', now() - interval '5 days');

    select * into row from public.reserve_paystack_card_update_attempt(recovery, 'failed', null, 'card-update-retry-one');
    assert row.recovery_id = recovery and row.authorization_retry_status = 'pending', 'Reserve the recovery before provider collection';
    assert row.authorization_retry_reference = 'card-update-retry-one' and row.authorization_retry_due_at is null, 'Persist the attempt reference and exclude it from the scheduled retry worker';
    assert not exists (select from public.reserve_paystack_card_update_attempt(recovery, 'failed', null, 'card-update-retry-two')), 'A distinct verification cannot reserve the same failed renewal';
    assert not exists (select from public.suspend_expired_payment_recovery(recovery)), 'An ambiguous payment must not lose access during expiry';

    -- After a provider-confirmed failure, a different verified card can reserve
    -- the next attempt with a compare-and-swap against the exact prior reference.
    select * into row from public.reserve_paystack_card_update_attempt(recovery, 'pending', 'card-update-retry-one', 'card-update-retry-two');
    assert row.authorization_retry_reference = 'card-update-retry-two';
    update public.subscription_payment_recoveries set authorization_retry_status = 'failed'
     where recovery_id = recovery and authorization_retry_reference = 'card-update-retry-one' and resolved_at is null;
    get diagnostics changed = row_count;
    assert changed = 0, 'A stale worker cannot overwrite the next card attempt';
    assert not exists (select from public.reserve_paystack_card_update_attempt(recovery, 'pending', 'card-update-retry-one', 'card-update-retry-three'));

    update public.subscription_payment_recoveries set authorization_retry_status = 'succeeded' where recovery_id = recovery;
    assert not exists (select from public.suspend_expired_payment_recovery(recovery)), 'Succeeded-but-unresolved recovery must preserve paid access after restart';
    assert (select status = 'active' from public.subscriptions where subscription_id = subscription);
    assert not exists (select from public.reserve_paystack_card_update_attempt(recovery, 'succeeded', 'card-update-retry-two', 'card-update-retry-three'));

    update public.subscription_payment_recoveries set resolved_at = now(), resolution = 'recovered' where recovery_id = recovery;
    assert not exists (select from public.suspend_expired_payment_recovery(recovery)), 'A stale expiry snapshot cannot suspend a resolved payment';
    update public.subscription_payment_recoveries set authorization_retry_status = 'failed' where recovery_id = recovery;
    assert not exists (select from public.reserve_paystack_card_update_attempt(recovery, 'failed', 'card-update-retry-two', 'card-update-retry-three')), 'Resolved debt must never be collected again';

    update public.subscription_payment_recoveries set resolved_at = null, resolution = null where recovery_id = recovery;
    select * into row from public.suspend_expired_payment_recovery(recovery);
    assert row.resolution = 'suspended' and row.resolved_at is not null, 'Genuine expired failed debt must still be suspended';
    assert (select status = 'failed' from public.subscriptions where subscription_id = subscription);

    assert not has_function_privilege('anon', 'public.reserve_paystack_card_update_attempt(uuid,text,text,text)', 'execute');
    assert not has_function_privilege('authenticated', 'public.reserve_paystack_card_update_attempt(uuid,text,text,text)', 'execute');
    assert has_function_privilege('service_role', 'public.reserve_paystack_card_update_attempt(uuid,text,text,text)', 'execute');
    assert not has_function_privilege('anon', 'public.suspend_expired_payment_recovery(uuid)', 'execute');
    assert not has_function_privilege('authenticated', 'public.suspend_expired_payment_recovery(uuid)', 'execute');
end $$;
rollback;
