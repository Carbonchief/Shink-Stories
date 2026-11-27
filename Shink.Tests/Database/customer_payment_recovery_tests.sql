-- Run only in an isolated fixture database with the recovery migrations applied.
begin;
-- Inject a failure between the subscription update and the recovery update.
create function pg_temp.reject_audit_settlement() returns trigger language plpgsql as $$
begin
    if current_setting('schink_test.fail_settlement', true) = 'on' and new.resolution = 'recovered' then
        raise exception 'simulated recovery write failure';
    end if;
    return new;
end $$;
create trigger reject_audit_settlement before update on public.subscription_payment_recoveries
    for each row execute function pg_temp.reject_audit_settlement();
do $$
declare
    u uuid := '11111111-1111-4111-8111-111111111111';
    s uuid := '22222222-2222-4222-8222-222222222222';
    r uuid := '33333333-3333-4333-8333-333333333333';
    ref text := 'customer-retry-' || repeat('a',32);
    email text := 'retry@example.invalid';
    next_due timestamptz := now() + interval '23 days';
begin
    assert not has_function_privilege('service_role','public.reserve_paystack_card_update_attempt(uuid,text,text,text)','execute');
    assert has_function_privilege('service_role','public.reserve_customer_payment_recovery(uuid,text,text,text,numeric,text,text,timestamptz)','execute');
    insert into public.subscribers(subscriber_id,email) values (u,email);
    insert into public.subscriptions(subscription_id,subscriber_id,tier_code,provider,provider_payment_id,
        status,billing_amount_zar,recurring_billing_mode)
    values (s,u,'all_stories_monthly','paystack','SUB_fixture','failed',79,'provider_subscription');
    insert into public.subscription_payment_recoveries(recovery_id,subscription_id,provider,provider_payment_id,
        first_failed_at,grace_ends_at,resolved_at,resolution,authorization_retry_status)
    values (r,s,'paystack','SUB_fixture',now()-interval '6 days',now()-interval '2 days',now(),'suspended','failed');

    update public.subscribers set disabled_at=now() where subscriber_id=u;
    assert not exists(select from public.reserve_customer_payment_recovery(r,'failed',null,ref,79,email,'SUB_fixture',next_due));
    update public.subscribers set disabled_at=null where subscriber_id=u;
    assert exists(select from public.reserve_customer_payment_recovery(r,'failed',null,ref,79,email,'SUB_fixture',next_due));
    assert not exists(select from public.reserve_customer_payment_recovery(r,'failed',null,'customer-retry-' || repeat('b',32),79,email,'SUB_fixture',next_due));
    assert (select status='failed' from public.subscriptions where subscription_id=s);
    assert not public.complete_customer_payment_recovery(r,s,ref,1,email,'SUB_fixture',next_due);
    assert not public.complete_customer_payment_recovery(r,s,ref,79,'wrong@example.invalid','SUB_fixture',next_due);
    assert not public.complete_customer_payment_recovery(r,s,ref,79,email,'SUB_wrong',next_due);
    assert not public.complete_customer_payment_recovery(r,s,'wrong',79,email,'SUB_fixture',next_due);
    update public.subscriptions set cancelled_at=now() where subscription_id=s;
    assert not public.complete_customer_payment_recovery(r,s,ref,79,email,'SUB_fixture',next_due);
    update public.subscriptions set cancelled_at=null where subscription_id=s;
    insert into public.subscriptions(subscription_id,subscriber_id,tier_code,provider,provider_payment_id,
        status,billing_amount_zar,recurring_billing_mode) values ('44444444-4444-4444-8444-444444444444',u,'all_stories_monthly',
        'google_play','second-agreement','active',79,'provider_subscription');
    assert not public.complete_customer_payment_recovery(r,s,ref,79,email,'SUB_fixture',next_due);
    assert (select status = 'failed' from public.subscriptions where subscription_id=s);
    update public.subscriptions set status='failed' where subscription_id='44444444-4444-4444-8444-444444444444';
    assert not public.complete_customer_payment_recovery(r,s,ref,79,email,'SUB_fixture',next_due + interval '1 month');
    update public.subscription_payment_recoveries set authorization_retry_check_after=null where recovery_id=r;
    assert exists(select from public.claim_payment_recovery_checks() where recovery_id=r);
    assert not exists(select from public.claim_payment_recovery_checks() where recovery_id=r);
    perform set_config('schink_test.fail_settlement','on',true);
    begin
        perform public.complete_customer_payment_recovery(r,s,ref,79,email,'SUB_fixture',next_due);
        raise exception 'expected injected failure';
    exception when raise_exception then
        if sqlerrm <> 'simulated recovery write failure' then raise; end if;
    end;
    assert (select status='failed' from public.subscriptions where subscription_id=s);
    assert (select resolved_at is null and authorization_retry_status='pending' from public.subscription_payment_recoveries where recovery_id=r);
    perform set_config('schink_test.fail_settlement','off',true);
    assert public.complete_customer_payment_recovery(r,s,ref,79,email,'SUB_fixture',next_due);
    assert public.complete_customer_payment_recovery(r,s,ref,79,email,'SUB_fixture',next_due);
    assert not public.complete_customer_payment_recovery(r,s,ref,1,email,'SUB_fixture',next_due);
    assert not public.complete_customer_payment_recovery(r,s,ref,79,'different@example.invalid','SUB_fixture',next_due);
    assert not public.complete_customer_payment_recovery(r,s,ref,79,email,'SUB_fixture',next_due + interval '1 month');
    assert (select status='active' and next_renewal_at=next_due from public.subscriptions where subscription_id=s);
    assert (select resolved_at is not null and resolution='recovered' and authorization_retry_status='succeeded'
        from public.subscription_payment_recoveries where recovery_id=r);
    assert not exists(select from public.reserve_customer_payment_recovery(r,'succeeded',ref,'customer-retry-' || repeat('b',32),79,email,'SUB_fixture',next_due));

    -- An earlier worker could lose its reference after a queued provider charge.
    -- Only adopt the independently verified reference from this recovery's ledger.
    update public.subscriptions set status='failed' where subscription_id=s;
    update public.subscription_payment_recoveries set authorization_retry_status='failed', authorization_retry_reference=null,
        resolved_at=now(), resolution='suspended' where recovery_id=r;
    assert not public.complete_customer_payment_recovery(r,s,'legacy-paid-retry',79,email,'SUB_fixture',next_due);
    insert into public.subscription_events(subscription_id,provider,provider_payment_id,event_type,received_at,payload)
    values (s,'paystack','SUB_fixture','paystack.authorization_retry',now(), '{"data":{"reference":"legacy-paid-retry"}}');
    assert public.complete_customer_payment_recovery(r,s,'legacy-paid-retry',79,email,'SUB_fixture',next_due);
    assert (select status='active' from public.subscriptions where subscription_id=s);
    update public.subscriptions set status='failed' where subscription_id=s;
    update public.subscription_payment_recoveries set resolution=null, resolved_at=null,
        authorization_retry_status='failed',authorization_retry_reference='previous-decline' where recovery_id=r;
    insert into public.subscription_events(subscription_id,provider,provider_payment_id,event_type,received_at,payload)
    values (s,'paystack','SUB_fixture','paystack.verified_recovery_invoice',now(),
        jsonb_build_object('reference','provider-paid','recovery_id',r,'previous_reference','another-attempt'));
    assert not public.complete_customer_payment_recovery(r,s,'provider-paid',79,email,'SUB_fixture',next_due);
    insert into public.subscription_events(subscription_id,provider,provider_payment_id,event_type,received_at,payload)
    values (s,'paystack','SUB_fixture','paystack.verified_recovery_invoice',now(),
        jsonb_build_object('reference','provider-paid','recovery_id',r,'previous_reference','previous-decline'));
    assert public.complete_customer_payment_recovery(r,s,'provider-paid',79,email,'SUB_fixture',next_due);

    -- Older unpaid rows predate the authorization retry columns.
    update public.subscriptions set status='failed' where subscription_id=s;
    update public.subscription_payment_recoveries set resolution='suspended', resolved_at=now(),
        authorization_retry_status=null, authorization_retry_reference=null where recovery_id=r;
    assert exists(select from public.reserve_customer_payment_recovery(r,null,null,ref,79,email,'SUB_fixture',next_due));
    assert (select status='failed' from public.subscriptions where subscription_id=s);
    update public.subscription_payment_recoveries set authorization_retry_check_after=null,provider='payfast' where recovery_id=r;
    assert not exists(select from public.claim_payment_recovery_checks() where recovery_id=r);
end $$;
rollback;
