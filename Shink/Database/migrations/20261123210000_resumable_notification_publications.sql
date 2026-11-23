begin;

-- One durable publication per content item. A completed row keeps its dedupe
-- key, but releases the audience array. The cursor counts committed recipients.
create table public.notification_publications (
    source_key text primary key,
    notification_type text not null,
    title text not null,
    body text,
    href text not null,
    image_path text not null,
    image_alt text not null,
    metadata jsonb not null,
    audience uuid[] not null,
    processed integer not null default 0,
    created_at timestamptz not null default now(),
    completed_at timestamptz
);
create index notification_publications_pending on public.notification_publications(created_at, source_key)
    where completed_at is null;
alter table public.notification_publications enable row level security;
revoke all on public.notification_publications from public, anon, authenticated;
grant select, insert, update on public.notification_publications to service_role;

-- Commands are consumed in the content transaction. Old rows and imports that
-- do not request a notification never enqueue an announcement.
alter table public.stories add column queue_publication_notification boolean not null default false;
alter table public.blog_posts add column queue_publication_notification boolean not null default false;
alter table public.resource_documents add column queue_publication_notification boolean not null default false;

create function public.queue_notification_publication()
returns trigger language plpgsql security invoker set search_path = '' as $$
declare
    job public.notification_publications;
    content_title text := btrim(new.title);
    slug text := lower(btrim(new.slug));
    type_name text;
begin
    new.queue_publication_notification := false;
    case tg_table_name
    when 'stories' then
        if new.status <> 'published' then return new; end if;
        if tg_op = 'UPDATE' then
            if old.status = 'published' or old.published_at is not null then return new; end if;
        end if;
        job.source_key := 'story-published-' || replace(new.story_id::text, '-', '');
        job.notification_type := 'story_published';
        job.title := 'Nuwe storie beskikbaar';
        job.body := content_title || ' is nou beskikbaar. ' || coalesce(nullif(btrim(new.summary), ''), 'Tik om te luister.');
        job.href := '/luister/' || slug;
        job.image_path := coalesce(nullif(btrim(new.thumbnail_image_path), ''), nullif(btrim(new.cover_image_path), ''));
        job.image_alt := 'Voorblad van ' || content_title;
        job.metadata := jsonb_build_object('story_id', new.story_id, 'story_slug', slug, 'story_title', content_title,
            'access_level', coalesce(nullif(lower(btrim(new.access_level)), ''), 'subscriber'));
    when 'blog_posts' then
        if not new.is_published or new.published_at is null or new.published_at > now() then return new; end if;
        if tg_op = 'UPDATE' then
            if old.is_published and old.published_at <= now() then return new; end if;
        end if;
        job.source_key := 'blog-published-' || replace(new.post_id::text, '-', '');
        job.notification_type := 'blog_published';
        job.title := content_title;
        job.body := content_title;
        job.href := '/blog/' || slug;
        job.image_path := nullif(btrim(new.featured_image_url), '');
        job.image_alt := 'Hoofprent vir ' || content_title;
        job.metadata := jsonb_build_object('post_id', new.post_id, 'post_slug', slug, 'post_title', content_title);
    when 'resource_documents' then
        if tg_op <> 'INSERT' or not new.is_enabled then return new; end if;
        select r.slug, r.name into strict slug, type_name
            from public.resource_types r where r.resource_type_id = new.resource_type_id;
        job.source_key := 'resource-document-published-' || replace(new.resource_document_id::text, '-', '');
        job.notification_type := 'resource_document_published';
        job.title := 'Nuwe hulpbron beskikbaar';
        job.body := content_title || ' is nou beskikbaar.';
        job.href := '/resources?tipe=' || slug;
        job.image_path := '/media/resources/' || new.resource_document_id::text || '/preview';
        job.image_alt := 'Voorskou van ' || content_title;
        job.metadata := jsonb_build_object('resource_document_id', new.resource_document_id, 'resource_type_slug', slug,
            'resource_type_name', type_name, 'resource_document_title', content_title);
    end case;

    select coalesce(array_agg(s.subscriber_id order by s.subscriber_id), '{}'::uuid[]) into job.audience
    from public.subscribers s
    where s.disabled_at is null and exists (
        select 1 from public.subscriptions sub where sub.subscriber_id = s.subscriber_id
            and sub.status = 'active'
            and (sub.cancelled_at is null or sub.cancelled_at > now())
            and (sub.next_renewal_at is null or sub.next_renewal_at >= now())
    );
    insert into public.notification_publications
        (source_key, notification_type, title, body, href, image_path, image_alt, metadata, audience, completed_at)
    values (job.source_key, job.notification_type, job.title, job.body, job.href,
        coalesce(regexp_replace(job.image_path, '^~/', '/'), '/branding/schink-placeholder.png'),
        job.image_alt, job.metadata, job.audience, case when cardinality(job.audience) = 0 then now() end)
    on conflict (source_key) do nothing;
    return new;
end;
$$;

create trigger queue_notification_publication before insert or update of queue_publication_notification on public.stories
    for each row when (new.queue_publication_notification) execute function public.queue_notification_publication();
create trigger queue_notification_publication before insert or update of queue_publication_notification on public.blog_posts
    for each row when (new.queue_publication_notification) execute function public.queue_notification_publication();
create trigger queue_notification_publication before insert or update of queue_publication_notification on public.resource_documents
    for each row when (new.queue_publication_notification) execute function public.queue_notification_publication();

create function public.process_notification_publication_batch()
returns integer language plpgsql security invoker set search_path = '' as $$
declare
    job public.notification_publications;
    batch_end integer;
    finished boolean;
begin
    select * into job from public.notification_publications
    where completed_at is null order by created_at, source_key limit 1 for update skip locked;
    if not found then return 0; end if;
    batch_end := least(job.processed + 200, cardinality(job.audience));
    finished := batch_end = cardinality(job.audience);

    insert into public.subscriber_notifications
        (subscriber_id, notification_type, source_key, title, body, href, image_path, image_alt, metadata, created_at)
    select s.subscriber_id, job.notification_type, job.source_key, job.title, job.body,
        job.href, job.image_path, job.image_alt, job.metadata, job.created_at
    from unnest(job.audience[job.processed + 1:batch_end]) as recipient(id)
    join public.subscribers s on s.subscriber_id = recipient.id and s.disabled_at is null
    on conflict (subscriber_id, source_key) do nothing;

    update public.notification_publications set processed = batch_end,
        completed_at = case when finished then now() end,
        audience = case when finished then '{}'::uuid[] else audience end
    where source_key = job.source_key;
    return batch_end - job.processed;
end;
$$;

-- A device enabling notifications after publication must not receive old work
-- when the inbox worker resumes. Token refresh on the same opt-in stays eligible.
alter table public.mobile_push_devices add column opted_in_at timestamptz not null default '-infinity';
alter table public.mobile_push_devices alter column opted_in_at set default now();

create function public.track_mobile_push_opt_in()
returns trigger language plpgsql security invoker set search_path = '' as $$
begin
    new.opted_in_at := now();
    return new;
end;
$$;
create trigger track_mobile_push_opt_in before update on public.mobile_push_devices
    for each row when (new.enabled and (not old.enabled or new.subscriber_id <> old.subscriber_id))
    execute function public.track_mobile_push_opt_in();

create or replace function public.enqueue_mobile_push_notification()
returns trigger language plpgsql security invoker set search_path = '' as $$
begin
    insert into public.mobile_push_deliveries(notification_id, installation_id, registration_id, created_at)
    select new.notification_id, d.installation_id, d.registration_id, new.created_at
    from public.mobile_push_devices d
    join public.subscribers s on s.subscriber_id = d.subscriber_id
    where d.subscriber_id = new.subscriber_id and d.enabled and s.disabled_at is null
        and d.opted_in_at <= new.created_at and d.updated_at > now() - interval '90 days'
        and new.created_at > now() - interval '24 hours'
        and new.read_at is null and new.cleared_at is null
    on conflict do nothing;
    return new;
end;
$$;

-- Reclaiming an interrupted attempt replaces its lease without spending a new
-- provider attempt. Eight rejected attempts and the original 24-hour expiry still apply.
create or replace function public.claim_mobile_push_deliveries(p_platforms text[], p_take integer default 20)
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
        and (q.created_at < now() - interval '24 hours' or (q.state = 'pending' and q.attempts >= 8) or not exists (
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
        update public.mobile_push_deliveries q set state = 'processing',
            attempts = case when q.state = 'processing' then q.attempts else q.attempts + 1 end,
            lease_id = gen_random_uuid(), available_at = now() + interval '5 minutes'
        from candidates c where q.delivery_id = c.delivery_id returning q.*
    )
    select q.delivery_id, q.lease_id, q.installation_id, q.registration_id, q.notification_id,
        d.platform, d.environment, d.token, n.title, n.body, n.notification_type, n.href
    from claimed q join public.mobile_push_devices d on d.installation_id = q.installation_id
    join public.subscriber_notifications n on n.notification_id = q.notification_id;
end;
$$;

-- Acceptance is a fact about the immutable delivery and registration. A worker
-- can replay it under a new lease even if the previous acknowledgement committed.
create function public.record_mobile_push_acceptance(p_delivery_id uuid, p_registration_id uuid)
returns void language sql security invoker set search_path = '' as $$
    update public.mobile_push_deliveries set state = 'sent', completed_at = now(), lease_id = null, last_error = null
    where delivery_id = p_delivery_id and registration_id = p_registration_id and state in ('pending', 'processing');
$$;

revoke all on function public.queue_notification_publication(), public.process_notification_publication_batch(),
    public.track_mobile_push_opt_in(), public.record_mobile_push_acceptance(uuid, uuid) from public, anon, authenticated;
grant execute on function public.queue_notification_publication(), public.process_notification_publication_batch(),
    public.track_mobile_push_opt_in(), public.record_mobile_push_acceptance(uuid, uuid) to service_role;
commit;
