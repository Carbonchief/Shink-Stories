-- Run only in a disposable local database:
-- psql -h 127.0.0.1 -p PORT -d schink_push_test -v ON_ERROR_STOP=1 -f scripts/tests/mobile_push_database.sql
\set ON_ERROR_STOP on
do $$ begin
    if current_database() not like 'schink_push_test%' or inet_server_addr() <> '127.0.0.1'::inet then
        raise exception 'This test requires a disposable local schink_push_test database';
    end if;
end $$;
do $$ begin
    if not exists(select from pg_roles where rolname = 'anon') then create role anon nologin; end if;
    if not exists(select from pg_roles where rolname = 'authenticated') then create role authenticated nologin; end if;
    if not exists(select from pg_roles where rolname = 'service_role') then create role service_role nologin bypassrls; end if;
end $$;
create table public.subscribers (
    subscriber_id uuid primary key, email text not null unique, disabled_at timestamptz
);
create table public.subscriber_notifications (
    notification_id uuid primary key default gen_random_uuid(),
    subscriber_id uuid not null references public.subscribers(subscriber_id) on delete cascade,
    notification_type text not null default 'story_published', source_key text not null,
    title text not null default 'Nuwe storie beskikbaar', body text, href text,
    created_at timestamptz not null default now(), read_at timestamptz, cleared_at timestamptz,
    unique(subscriber_id, source_key)
);
grant select on public.subscribers, public.subscriber_notifications to service_role;
grant insert on public.subscriber_notifications to service_role;
\ir ../../Shink/Database/migrations/20261029135259_mobile_push_notifications.sql

insert into public.subscribers(subscriber_id, email) values
    ('10000000-0000-0000-0000-000000000001', 'a@example.test'),
    ('10000000-0000-0000-0000-000000000002', 'b@example.test');

do $$ begin
    assert not has_table_privilege('anon', 'public.mobile_push_devices', 'SELECT');
    assert not has_table_privilege('authenticated', 'public.mobile_push_devices', 'INSERT');
    assert not has_table_privilege('authenticated', 'public.mobile_push_deliveries', 'SELECT');
    assert not has_function_privilege('anon', 'public.claim_mobile_push_deliveries(text[],integer)', 'EXECUTE');
    assert not has_function_privilege('authenticated', 'public.register_mobile_push_device(text,uuid,text,text,text,text)', 'EXECUTE');
    assert (select relrowsecurity from pg_class where oid = 'public.mobile_push_devices'::regclass);
    assert (select relrowsecurity from pg_class where oid = 'public.mobile_push_deliveries'::regclass);
end $$;

set role service_role;
select public.register_mobile_push_device('a@example.test', '20000000-0000-0000-0000-000000000001', repeat('a',64), 'apns', 'production', repeat('1',64));
select public.register_mobile_push_device('a@example.test', '20000000-0000-0000-0000-000000000002', repeat('b',64), 'fcm', 'production', 'fake-fcm-token-for-local-test');
select public.register_mobile_push_device('b@example.test', '20000000-0000-0000-0000-000000000003', repeat('c',64), 'apns', 'sandbox', repeat('2',64));
insert into public.subscriber_notifications(subscriber_id, source_key, body, href)
values ('10000000-0000-0000-0000-000000000001', 'first-story', 'Storie een', '/luister/een');
insert into public.subscriber_notifications(subscriber_id, source_key)
values ('10000000-0000-0000-0000-000000000001', 'first-story') on conflict do nothing;
do $$ declare v_claim record; begin
    assert (select count(*) from public.mobile_push_deliveries) = 2, 'Only the inbox recipient devices are queued, once';
    select * into v_claim from public.claim_mobile_push_deliveries(array['apns']);
    assert v_claim.platform = 'apns' and v_claim.body = 'Storie een' and v_claim.href = '/luister/een';
    assert public.can_send_mobile_push_delivery(v_claim.delivery_id, v_claim.lease_id);
    assert (select count(*) from public.claim_mobile_push_deliveries(array['apns'])) = 0, 'An active lease cannot be reclaimed';
    perform public.complete_mobile_push_delivery(v_claim.delivery_id, gen_random_uuid(), 'sent');
    assert (select state from public.mobile_push_deliveries where delivery_id = v_claim.delivery_id) = 'processing', 'Wrong lease cannot acknowledge';
    perform public.complete_mobile_push_delivery(v_claim.delivery_id, v_claim.lease_id, 'retry', 'test_transient', 120);
    assert (select state = 'pending' and attempts = 1 and available_at > now() + interval '100 seconds'
        from public.mobile_push_deliveries where delivery_id = v_claim.delivery_id);
end $$;
-- Registration after publication must not replay the old inbox.
select public.register_mobile_push_device('a@example.test', '20000000-0000-0000-0000-000000000004', repeat('d',64), 'apns', 'production', repeat('3',64));
do $$ begin assert (select count(*) from public.mobile_push_deliveries) = 2; end $$;

-- Wrong proof cannot revoke or reassign a known installation.
select public.unregister_mobile_push_device('20000000-0000-0000-0000-000000000001', repeat('f',64));
do $$ begin
    assert (select enabled from public.mobile_push_devices where installation_id = '20000000-0000-0000-0000-000000000001');
    begin
        perform public.register_mobile_push_device('b@example.test', '20000000-0000-0000-0000-000000000001', repeat('f',64), 'apns', 'production', repeat('1',64));
        raise exception 'Wrong proof accepted';
    exception when insufficient_privilege then null; end;
end $$;

-- Account switches invalidate queued messages for the previous owner.
select public.register_mobile_push_device('b@example.test', '20000000-0000-0000-0000-000000000001', repeat('a',64), 'apns', 'production', repeat('1',64));
do $$ begin
    assert (select state from public.mobile_push_deliveries where installation_id = '20000000-0000-0000-0000-000000000001') = 'cancelled';
end $$;
select public.unregister_mobile_push_device('20000000-0000-0000-0000-000000000002', repeat('b',64));
do $$ begin
    assert not (select enabled from public.mobile_push_devices where installation_id = '20000000-0000-0000-0000-000000000002');
    assert (select state from public.mobile_push_deliveries where installation_id = '20000000-0000-0000-0000-000000000002') = 'cancelled';
end $$;

-- Old token responses cannot deactivate a replacement registration.
insert into public.subscriber_notifications(subscriber_id, source_key)
values ('10000000-0000-0000-0000-000000000001', 'second-story');
do $$ declare v_claim record; begin
    select * into v_claim from public.claim_mobile_push_deliveries(array['apns']);
    perform public.register_mobile_push_device('a@example.test', '20000000-0000-0000-0000-000000000004', repeat('d',64), 'apns', 'production', repeat('4',64));
    assert not public.can_send_mobile_push_delivery(v_claim.delivery_id, v_claim.lease_id), 'Revoked claims must not reach provider';
    perform public.complete_mobile_push_delivery(v_claim.delivery_id, v_claim.lease_id, 'invalid', 'old_token');
    assert (select enabled from public.mobile_push_devices where installation_id = v_claim.installation_id);
end $$;
insert into public.subscriber_notifications(subscriber_id, source_key)
values ('10000000-0000-0000-0000-000000000001', 'third-story');
do $$ declare v_claim record; begin
    select * into v_claim from public.claim_mobile_push_deliveries(array['apns']);
    perform public.complete_mobile_push_delivery(v_claim.delivery_id, v_claim.lease_id, 'invalid', 'new_token_invalid');
    assert not (select enabled from public.mobile_push_devices where installation_id = v_claim.installation_id);
end $$;

reset role;
-- A process crash can recover an expired lease, but the old worker cannot ack it.
insert into public.subscriber_notifications(subscriber_id, source_key)
values ('10000000-0000-0000-0000-000000000002', 'lease-recovery');
do $$ declare v_old record; v_new record; begin
    select * into v_old from public.claim_mobile_push_deliveries(array['apns'], 1);
    update public.mobile_push_deliveries set available_at = now() - interval '1 second' where delivery_id = v_old.delivery_id;
    select * into v_new from public.claim_mobile_push_deliveries(array['apns'], 1);
    assert v_old.delivery_id = v_new.delivery_id and v_old.lease_id <> v_new.lease_id;
    perform public.complete_mobile_push_delivery(v_old.delivery_id, v_old.lease_id, 'sent');
    assert (select state from public.mobile_push_deliveries where delivery_id = v_new.delivery_id) = 'processing';
    perform public.complete_mobile_push_delivery(v_new.delivery_id, v_new.lease_id, 'sent');
end $$;
-- Exhausted retries stop dispatching.
update public.mobile_push_deliveries set state = 'pending', attempts = 8, available_at = now() - interval '1 second'
where state = 'pending';
do $$ begin assert (select count(*) from public.claim_mobile_push_deliveries(array['apns','fcm'])) = 0; end $$;

-- Read/cleared/expired notifications, disabled accounts, and stale devices never dispatch.
select public.register_mobile_push_device('a@example.test', '20000000-0000-0000-0000-000000000004', repeat('d',64), 'apns', 'production', repeat('4',64));
insert into public.subscriber_notifications(subscriber_id, source_key) values
    ('10000000-0000-0000-0000-000000000001', 'read-before-delivery'),
    ('10000000-0000-0000-0000-000000000001', 'cleared-before-delivery'),
    ('10000000-0000-0000-0000-000000000001', 'expired-before-delivery');
update public.subscriber_notifications set read_at = now() where source_key = 'read-before-delivery';
update public.subscriber_notifications set cleared_at = now() where source_key = 'cleared-before-delivery';
update public.mobile_push_deliveries set created_at = now() - interval '25 hours' where notification_id in
    (select notification_id from public.subscriber_notifications where source_key = 'expired-before-delivery');
do $$ begin assert (select count(*) from public.claim_mobile_push_deliveries(array['apns','fcm'])) = 0; end $$;
update public.subscribers set disabled_at = now() where email = 'a@example.test';
insert into public.subscriber_notifications(subscriber_id, source_key)
values ('10000000-0000-0000-0000-000000000001', 'disabled-account');
do $$ begin assert not exists (select 1 from public.mobile_push_deliveries q join public.subscriber_notifications n
    on n.notification_id = q.notification_id where n.source_key = 'disabled-account'); end $$;
select 'PASS: queue targeting, dedupe, leases, retries, registration proof, revocation, account switching, token rotation, invalid tokens, expiry and RLS' as result;
