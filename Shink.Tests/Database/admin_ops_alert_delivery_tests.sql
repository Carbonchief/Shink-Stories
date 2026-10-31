-- Run against an isolated local database after applying the delivery migration.
begin;
do $$
declare
    claim record;
    original_lease uuid;
    original_payload jsonb := '{"template":{"id":"published-alert","variables":{"OCCURRED_AT":"first occurrence"}}}';
    changed_payload jsonb := '{"template":{"id":"published-alert","variables":{"OCCURRED_AT":"later retry"}}}';
begin
    select * into claim from public.claim_admin_ops_alert('delivery-test', original_payload);
    assert claim.alert_key = 'delivery-test', 'First worker must claim the incident';
    original_lease := claim.lease_token;
    assert not exists (select from public.claim_admin_ops_alert('delivery-test', changed_payload)), 'Concurrent worker must not obtain a live lease';

    update public.admin_ops_alert_deliveries set lease_expires_at = now() - interval '1 minute' where alert_key = 'delivery-test';
    select * into claim from public.claim_admin_ops_alert('delivery-test', changed_payload);
    assert claim.lease_token <> original_lease, 'A stale lease must be replaced';
    assert claim.request_payload = original_payload, 'Retry must preserve the first payload and timestamp';
    update public.admin_ops_alert_deliveries set sent_at = now(), provider_email_id = 'provider-id'
     where alert_key = 'delivery-test' and lease_token = claim.lease_token;
    update public.admin_ops_alert_deliveries set first_attempt_at = now() - interval '3 days', lease_expires_at = now() - interval '1 day' where alert_key = 'delivery-test';
    assert not exists (select from public.claim_admin_ops_alert('delivery-test', changed_payload)), 'Sent incidents must remain suppressed after provider expiry';

    perform * from public.claim_admin_ops_alert('uncertain-delivery', original_payload);
    update public.admin_ops_alert_deliveries set first_attempt_at = now() - interval '24 hours', lease_expires_at = now() - interval '1 day' where alert_key = 'uncertain-delivery';
    assert not exists (select from public.claim_admin_ops_alert('uncertain-delivery', changed_payload)), 'Ambiguous delivery must never be resent outside the safe provider window';

    insert into public.admin_ops_alert_deliveries(alert_key, request_payload, lease_expires_at)
    values ('queued-terminal-alert', original_payload, now() - interval '2 days');
    assert (select first_attempt_at is null from public.admin_ops_alert_deliveries where alert_key = 'queued-terminal-alert'), 'Queueing must not start the provider idempotency clock';
    select * into claim from public.claim_pending_admin_ops_alert();
    assert claim.alert_key = 'queued-terminal-alert' and claim.first_attempt_at = now(), 'Unsent backlog must be independently dispatched';
    original_lease := claim.lease_token;
    assert not exists (select from public.claim_pending_admin_ops_alert()), 'Competing dispatchers must not share a lease';
    update public.admin_ops_alert_deliveries set lease_expires_at = now() - interval '1 minute' where alert_key = 'queued-terminal-alert';
    select * into claim from public.claim_pending_admin_ops_alert();
    assert claim.alert_key = 'queued-terminal-alert' and claim.lease_token <> original_lease, 'An ambiguous send must retry after restart without another incident';
    assert claim.request_payload = original_payload, 'Background dispatch must preserve the immutable published template payload';
    update public.admin_ops_alert_deliveries set sent_at = now() where alert_key = claim.alert_key and lease_token = claim.lease_token;
    assert not exists (select from public.claim_pending_admin_ops_alert()), 'Neither delivered nor out-of-window incidents may be dispatched';

    assert not has_table_privilege('anon', 'public.admin_ops_alert_deliveries', 'select'), 'Anonymous clients cannot read internal alert payloads';
    assert not has_table_privilege('authenticated', 'public.admin_ops_alert_deliveries', 'select'), 'Signed-in clients cannot read internal alert payloads';
    assert not has_function_privilege('anon', 'public.claim_admin_ops_alert(text,jsonb)', 'execute'), 'Anonymous clients cannot claim alerts';
    assert not has_function_privilege('authenticated', 'public.claim_admin_ops_alert(text,jsonb)', 'execute'), 'Signed-in clients cannot claim alerts';
    assert has_function_privilege('service_role', 'public.claim_admin_ops_alert(text,jsonb)', 'execute'), 'Server must be able to claim alerts';
    assert not has_function_privilege('anon', 'public.claim_pending_admin_ops_alert()', 'execute');
    assert not has_function_privilege('authenticated', 'public.claim_pending_admin_ops_alert()', 'execute');
    assert has_function_privilege('service_role', 'public.claim_pending_admin_ops_alert()', 'execute');
end $$;
rollback;
