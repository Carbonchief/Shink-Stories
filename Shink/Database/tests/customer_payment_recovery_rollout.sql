-- Synthetic, uncommitted rows stay invisible to the production workers.
begin;
set local statement_timeout = '15s';
set local lock_timeout = '3s';
set local role service_role;

do $$
declare
    subscriber uuid := gen_random_uuid();
    subscription uuid := gen_random_uuid();
    recovery uuid := gen_random_uuid();
    email text := 'release-check+' || subscriber::text || '@example.invalid';
    agreement text := 'SUB_release_' || replace(subscription::text, '-', '');
    reference text := 'customer-retry-' || replace(recovery::text, '-', '');
    other_reference text := 'customer-retry-' || replace(gen_random_uuid()::text, '-', '');
    paid_through timestamptz := now() + interval '30 days';
begin
    insert into public.subscribers(subscriber_id, email) values (subscriber, email);
    insert into public.subscriptions(subscription_id, subscriber_id, tier_code, provider, provider_payment_id,
        status, subscribed_at, recurring_billing_mode, billing_amount_zar)
    values (subscription, subscriber, 'all_stories_monthly', 'paystack', agreement,
        'failed', now() - interval '1 month', 'provider_subscription', 79);
    insert into public.subscription_payment_recoveries(recovery_id, subscription_id, provider, provider_payment_id,
        first_failed_at, grace_ends_at, resolution, resolved_at, authorization_retry_status)
    values (recovery, subscription, 'paystack', agreement, now() - interval '6 days',
        now() - interval '1 day', 'suspended', now(), 'failed');

    if not exists (select from public.reserve_customer_payment_recovery(
        recovery, 'failed', null, reference, 79, email, agreement, paid_through)) then
        raise exception 'Eligible suspended renewal could not reserve a payment reference';
    end if;
    if exists (select from public.reserve_customer_payment_recovery(
        recovery, 'pending', reference, other_reference, 79, email, agreement, paid_through)) then
        raise exception 'Second payment reference was accepted';
    end if;
    if public.complete_customer_payment_recovery(recovery, subscription, reference, 1, email, agreement, paid_through)
       or not exists (select from public.subscriptions where subscription_id=subscription and status='failed') then
        raise exception 'An incorrect amount granted access';
    end if;
    if not public.complete_customer_payment_recovery(recovery, subscription, reference, 79, email, agreement, paid_through) then
        raise exception 'Full settlement could not restore access';
    end if;
    if not public.complete_customer_payment_recovery(recovery, subscription, reference, 79, email, agreement, paid_through)
       or not exists (select from public.subscriptions where subscription_id=subscription and status='active'
                      and tier_code='all_stories_monthly' and next_renewal_at=paid_through)
       or not exists (select from public.subscription_payment_recoveries where recovery_id=recovery and resolution='recovered'
                      and authorization_retry_reference=reference and authorization_retry_status='succeeded') then
        raise exception 'Repeated settlement must retain the original paid tier and renewal';
    end if;
end $$;
rollback;
select 'Recovery reservation, duplicate protection, amount verification, restoration and repeat settlement passed. Synthetic rows rolled back.' as verification;
