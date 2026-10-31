-- Run only against an isolated database after applying the card-update migration.
begin;
do $$
declare
    original_payload jsonb := '{"event":"charge.success","data":{"reference":"card-update-test"}}';
    job public.paystack_card_update_jobs;
    original_lease uuid;
    changed integer;
begin
    insert into public.paystack_card_update_jobs(job_key, payload) values ('card-update-test', original_payload);
    select * into job from public.claim_paystack_card_update_job();
    assert job.job_key = 'card-update-test' and job.status = 'processing' and job.attempts = 1, 'One due job must be leased';
    original_lease := job.lease_token;
    assert not exists (select from public.claim_paystack_card_update_job()), 'A second worker cannot claim the same active job';

    update public.paystack_card_update_jobs set locked_until = now() - interval '1 minute' where job_key = 'card-update-test';
    select * into job from public.claim_paystack_card_update_job();
    assert job.attempts = 2 and job.lease_token <> original_lease, 'A restarted worker can reclaim an expired lease';
    update public.paystack_card_update_jobs set status = 'completed', lease_token = null, locked_until = null
     where job_key = 'card-update-test' and lease_token = original_lease;
    get diagnostics changed = row_count;
    assert changed = 0, 'A stale worker cannot complete a newly leased job';

    update public.paystack_card_update_jobs set status = 'pending', available_at = now() + interval '1 day', lease_token = null, locked_until = null where job_key = 'card-update-test';
    assert not exists (select from public.claim_paystack_card_update_job()), 'Backoff must prevent immediate retries';
    update public.paystack_card_update_jobs set available_at = now() where job_key = 'card-update-test';
    select * into job from public.claim_paystack_card_update_job();
    assert job.attempts = 3, 'A due retry must become claimable';

    update public.paystack_card_update_jobs set status = 'completed', lease_token = null, locked_until = null where job_key = 'card-update-test' and lease_token = job.lease_token;
    insert into public.paystack_card_update_jobs(job_key, payload) values ('card-update-test', '{"changed":true}') on conflict (job_key) do nothing;
    assert (select count(*) from public.paystack_card_update_jobs where job_key = 'card-update-test') = 1, 'Duplicate webhooks must retain one job';
    assert (select payload from public.paystack_card_update_jobs where job_key = 'card-update-test') = original_payload, 'Duplicate webhooks must preserve the recorded envelope';
    assert not exists (select from public.claim_paystack_card_update_job()), 'A completed job must not run again';
    update public.paystack_card_update_jobs set status = 'needs_attention' where job_key = 'card-update-test';
    assert not exists (select from public.claim_paystack_card_update_job()), 'Escalated jobs must not charge automatically';

    assert not has_table_privilege('anon', 'public.paystack_card_update_jobs', 'select');
    assert not has_table_privilege('authenticated', 'public.paystack_card_update_jobs', 'select');
    assert not has_table_privilege('anon', 'public.paystack_card_update_jobs', 'insert');
    assert not has_function_privilege('anon', 'public.claim_paystack_card_update_job()', 'execute');
    assert not has_function_privilege('authenticated', 'public.claim_paystack_card_update_job()', 'execute');
    assert has_function_privilege('service_role', 'public.claim_paystack_card_update_job()', 'execute');
end $$;
rollback;
