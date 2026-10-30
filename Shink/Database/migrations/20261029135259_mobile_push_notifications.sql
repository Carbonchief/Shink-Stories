-- New inbox rows are the single source of truth for mobile push. Existing rows
-- are deliberately not backfilled. All device/queue access is server-only.
begin;

create table public.mobile_push_devices (
    installation_id uuid primary key,
    installation_secret_hash text not null check (length(installation_secret_hash) = 64),
    registration_id uuid not null default gen_random_uuid(),
    subscriber_id uuid not null references public.subscribers(subscriber_id) on delete cascade,
    platform text not null check (platform in ('apns', 'fcm')),
    environment text not null check (environment in ('production', 'sandbox')),
    token text not null check (length(token) between 16 and 2048),
    enabled boolean not null default true,
    updated_at timestamptz not null default now(),
    check (platform <> 'fcm' or environment = 'production'),
    unique (platform, environment, token)
);
create index mobile_push_devices_subscriber on public.mobile_push_devices(subscriber_id) where enabled;

create table public.mobile_push_deliveries (
    delivery_id uuid primary key default gen_random_uuid(),
    notification_id uuid not null references public.subscriber_notifications(notification_id) on delete cascade,
    installation_id uuid not null references public.mobile_push_devices(installation_id) on delete cascade,
    registration_id uuid not null,
    state text not null default 'pending' check (state in ('pending', 'processing', 'sent', 'failed', 'cancelled')),
    attempts integer not null default 0,
    available_at timestamptz not null default now(),
    lease_id uuid,
    created_at timestamptz not null default now(),
    completed_at timestamptz,
    last_error text,
    unique (notification_id, installation_id)
);
create index mobile_push_deliveries_ready on public.mobile_push_deliveries(available_at)
    where state in ('pending', 'processing');
create index mobile_push_deliveries_installation on public.mobile_push_deliveries(installation_id)
    where state in ('pending', 'processing');
create index mobile_push_deliveries_completed on public.mobile_push_deliveries(completed_at)
    where completed_at is not null;

alter table public.mobile_push_devices enable row level security;
alter table public.mobile_push_deliveries enable row level security;
revoke all on public.mobile_push_devices, public.mobile_push_deliveries from public, anon, authenticated;
grant select, insert, update, delete on public.mobile_push_devices, public.mobile_push_deliveries to service_role;

create function public.register_mobile_push_device(
    p_email text, p_installation_id uuid, p_secret_hash text,
    p_platform text, p_environment text, p_token text
) returns uuid language plpgsql security invoker set search_path = '' as $$
declare
    v_subscriber uuid;
    v_registration uuid;
begin
    select s.subscriber_id into v_subscriber from public.subscribers s
    where lower(s.email) = lower(btrim(p_email)) and s.disabled_at is null;
    if v_subscriber is null then
        raise exception 'Subscriber unavailable' using errcode = '42501';
    end if;

    -- Serializes token rotation, account switches and revocation for this install.
    perform pg_catalog.pg_advisory_xact_lock(pg_catalog.hashtextextended(p_installation_id::text, 0));
    if exists (select 1 from public.mobile_push_devices d
        where d.installation_id = p_installation_id and d.installation_secret_hash <> p_secret_hash) then
        raise exception 'Installation proof invalid' using errcode = '42501';
    end if;

    insert into public.mobile_push_devices as d
        (installation_id, installation_secret_hash, subscriber_id, platform, environment, token)
    values (p_installation_id, p_secret_hash, v_subscriber, p_platform, p_environment, p_token)
    on conflict (installation_id) do update set
        registration_id = case when d.subscriber_id <> excluded.subscriber_id or d.token <> excluded.token
            or d.platform <> excluded.platform or d.environment <> excluded.environment or not d.enabled
            then gen_random_uuid() else d.registration_id end,
        subscriber_id = excluded.subscriber_id, platform = excluded.platform,
        environment = excluded.environment, token = excluded.token, enabled = true, updated_at = now()
    returning registration_id into v_registration;

    update public.mobile_push_deliveries set state = 'cancelled', completed_at = now()
    where installation_id = p_installation_id and registration_id <> v_registration
        and state in ('pending', 'processing');
    return v_registration;
end;
$$;

-- A random per-installation secret also permits revocation after an auth session
-- expires. Knowing an installation ID or another user's email is insufficient.
create function public.unregister_mobile_push_device(p_installation_id uuid, p_secret_hash text)
returns void language plpgsql security invoker set search_path = '' as $$
begin
    perform pg_catalog.pg_advisory_xact_lock(pg_catalog.hashtextextended(p_installation_id::text, 0));
    update public.mobile_push_devices set enabled = false, updated_at = now()
    where installation_id = p_installation_id and installation_secret_hash = p_secret_hash;
    if found then
        update public.mobile_push_deliveries set state = 'cancelled', completed_at = now()
        where installation_id = p_installation_id and state in ('pending', 'processing');
    end if;
end;
$$;

create function public.enqueue_mobile_push_notification()
returns trigger language plpgsql security invoker set search_path = '' as $$
begin
    insert into public.mobile_push_deliveries(notification_id, installation_id, registration_id)
    select new.notification_id, d.installation_id, d.registration_id
    from public.mobile_push_devices d
    join public.subscribers s on s.subscriber_id = d.subscriber_id
    where d.subscriber_id = new.subscriber_id and d.enabled and s.disabled_at is null
        and d.updated_at > now() - interval '90 days'
        and new.created_at > now() - interval '24 hours'
        and new.read_at is null and new.cleared_at is null
    on conflict do nothing;
    return new;
end;
$$;
create trigger enqueue_mobile_push_notification after insert on public.subscriber_notifications
    for each row execute function public.enqueue_mobile_push_notification();

create function public.claim_mobile_push_deliveries(p_platforms text[], p_take integer default 20)
returns table (
    delivery_id uuid, lease_id uuid, installation_id uuid, registration_id uuid,
    notification_id uuid, platform text, environment text, token text,
    title text, body text, notification_type text, href text
) language plpgsql security invoker set search_path = '' as $$
begin
    -- Small, bounded retention cleanup also runs while the queue is idle.
    delete from public.mobile_push_deliveries q where q.delivery_id in (
        select d.delivery_id from public.mobile_push_deliveries d
        where d.completed_at < now() - interval '30 days' limit 1000);

    update public.mobile_push_deliveries q set state = 'cancelled', completed_at = now()
    where q.state in ('pending', 'processing') and q.available_at <= now()
        and (q.created_at < now() - interval '24 hours' or q.attempts >= 8 or not exists (
            select 1 from public.mobile_push_devices d
            join public.subscribers s on s.subscriber_id = d.subscriber_id
            join public.subscriber_notifications n on n.notification_id = q.notification_id
            where d.installation_id = q.installation_id and d.registration_id = q.registration_id
                and d.subscriber_id = n.subscriber_id and d.enabled and s.disabled_at is null
                and d.updated_at > now() - interval '90 days' and n.read_at is null and n.cleared_at is null));

    return query
    with candidates as (
        select q.delivery_id from public.mobile_push_deliveries q
        join public.mobile_push_devices d on d.installation_id = q.installation_id
        where q.state in ('pending', 'processing') and q.available_at <= now()
            and d.platform = any(p_platforms)
        order by q.available_at, q.delivery_id
        limit greatest(1, least(p_take, 100)) for update of q skip locked
    ), claimed as (
        update public.mobile_push_deliveries q set state = 'processing', attempts = q.attempts + 1,
            lease_id = gen_random_uuid(), available_at = now() + interval '5 minutes'
        from candidates c where q.delivery_id = c.delivery_id returning q.*
    )
    select q.delivery_id, q.lease_id, q.installation_id, q.registration_id, q.notification_id,
        d.platform, d.environment, d.token, n.title, n.body, n.notification_type, n.href
    from claimed q join public.mobile_push_devices d on d.installation_id = q.installation_id
    join public.subscriber_notifications n on n.notification_id = q.notification_id;
end;
$$;

-- Recheck immediately before contacting the provider; a claimed batch can be
-- cancelled by logout, deletion or account switching while other items send.
create function public.can_send_mobile_push_delivery(p_delivery_id uuid, p_lease_id uuid)
returns boolean language sql stable security invoker set search_path = '' as $$
    select exists (
        select 1 from public.mobile_push_deliveries q
        join public.mobile_push_devices d on d.installation_id = q.installation_id and d.registration_id = q.registration_id
        join public.subscriber_notifications n on n.notification_id = q.notification_id and n.subscriber_id = d.subscriber_id
        join public.subscribers s on s.subscriber_id = d.subscriber_id
        where q.delivery_id = p_delivery_id and q.lease_id = p_lease_id and q.state = 'processing'
            and q.available_at > now() and q.created_at > now() - interval '24 hours'
            and d.enabled and s.disabled_at is null and n.read_at is null and n.cleared_at is null
    );
$$;

create function public.complete_mobile_push_delivery(
    p_delivery_id uuid, p_lease_id uuid, p_result text, p_error text default null,
    p_retry_seconds integer default 60
) returns void language plpgsql security invoker set search_path = '' as $$
declare v_installation uuid; v_registration uuid;
begin
    if p_result not in ('sent', 'retry', 'invalid', 'failed') then
        raise exception 'Invalid delivery result';
    end if;
    update public.mobile_push_deliveries q set
        state = case when p_result = 'sent' then 'sent'
            when p_result = 'retry' and q.attempts < 8 then 'pending' else 'failed' end,
        completed_at = case when p_result = 'retry' and q.attempts < 8 then null else now() end,
        available_at = now() + make_interval(secs => greatest(60, least(p_retry_seconds, 3600))),
        last_error = left(p_error, 100), lease_id = null
    where q.delivery_id = p_delivery_id and q.lease_id = p_lease_id and q.state = 'processing'
    returning q.installation_id, q.registration_id into v_installation, v_registration;
    if p_result = 'invalid' and v_installation is not null then
        -- A failed old token must never disable a newer registration.
        update public.mobile_push_devices set enabled = false
        where installation_id = v_installation and registration_id = v_registration;
    end if;
end;
$$;

revoke all on function public.register_mobile_push_device(text, uuid, text, text, text, text),
    public.unregister_mobile_push_device(uuid, text), public.enqueue_mobile_push_notification(),
    public.claim_mobile_push_deliveries(text[], integer),
    public.can_send_mobile_push_delivery(uuid, uuid),
    public.complete_mobile_push_delivery(uuid, uuid, text, text, integer) from public, anon, authenticated;
grant execute on function public.register_mobile_push_device(text, uuid, text, text, text, text),
    public.unregister_mobile_push_device(uuid, text), public.enqueue_mobile_push_notification(),
    public.claim_mobile_push_deliveries(text[], integer),
    public.can_send_mobile_push_delivery(uuid, uuid),
    public.complete_mobile_push_delivery(uuid, uuid, text, text, integer) to service_role;

commit;
