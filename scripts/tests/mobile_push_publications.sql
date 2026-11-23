-- The included fixture refuses all but a disposable loopback schink_push_test* DB.
\ir mobile_push_database.sql
set plpgsql.check_asserts = on;

create table public.stories(story_id uuid primary key default gen_random_uuid(), title text, slug text,
    status text, published_at timestamptz, summary text, access_level text, thumbnail_image_path text, cover_image_path text);
create table public.blog_posts(post_id uuid primary key default gen_random_uuid(), title text, slug text,
    is_published boolean, published_at timestamptz, featured_image_url text);
create table public.resource_types(resource_type_id uuid primary key default gen_random_uuid(), name text, slug text);
create table public.resource_documents(resource_document_id uuid primary key default gen_random_uuid(), title text,
    slug text, is_enabled boolean, resource_type_id uuid references public.resource_types);
create table public.subscriptions(subscriber_id uuid references public.subscribers, status text, next_renewal_at timestamptz, cancelled_at timestamptz);
alter table public.subscriber_notifications add column image_alt text, add column metadata jsonb;
grant select on public.subscriptions, public.resource_types to service_role;
grant select, insert, update on public.stories, public.blog_posts, public.resource_documents to service_role;
\ir ../../Shink/Database/migrations/20261123210000_resumable_notification_publications.sql

create table expected as
select i, gen_random_uuid() as subscriber_id, gen_random_uuid() as installation_id
from generate_series(1,3744) i;
insert into public.subscribers(subscriber_id,email) select subscriber_id, 'fixture-' || i || '@example.test' from expected;
insert into public.subscriptions select subscriber_id, 'active', null, null from expected;
-- A subscriber with two qualifying subscriptions still gets one inbox row.
insert into public.subscriptions select subscriber_id, 'active', null, null from expected where i=7;
insert into public.mobile_push_devices(installation_id, installation_secret_hash, subscriber_id, platform, environment, token)
select installation_id, repeat('a',64), subscriber_id, case when i%2=0 then 'apns' else 'fcm' end,
    'production', repeat(md5(installation_id::text),2) from expected where i<=2621;

insert into public.subscribers(subscriber_id,email,disabled_at)
select gen_random_uuid(), reason || '@example.test', case when reason='disabled' then now() end
from unnest(array['disabled','cancelled','expired','inactive','late']) reason;
insert into public.subscriptions
select subscriber_id, case when email='inactive@example.test' then 'pending' else 'active' end,
    case when email='expired@example.test' then now()-interval '1 day' end,
    case when email='cancelled@example.test' then now()-interval '1 day' end
from public.subscribers where email in ('disabled@example.test','inactive@example.test','expired@example.test','cancelled@example.test');

-- A failure saving the publication must also abort the content write.
create function public.test_fail_write() returns trigger language plpgsql as $$
begin raise exception 'injected publication failure' using errcode = 'Z0001'; end $$;
create trigger fail_publication before insert on public.notification_publications for each row execute function public.test_fail_write();
set role service_role;
do $$ begin
    begin
        insert into public.stories(title,slug,status,queue_publication_notification)
            values ('Rollback','rollback','published',true);
        raise exception 'Expected publication failure';
    exception when sqlstate 'Z0001' then null; end;
    assert not exists(select from public.stories where slug='rollback');
end $$;
reset role;
drop trigger fail_publication on public.notification_publications;

set role service_role;
insert into public.stories(title,slug,status,summary,cover_image_path,queue_publication_notification)
values ('Kaptein Knopkop','kaptein-knopkop','draft','Lees saam.','~/covers/kaptein.jpg',true),
       ('Silent','silent','published',null,null,false);
update public.stories set status='published', published_at=now(), queue_publication_notification=true where slug='kaptein-knopkop';
update public.stories set title='Edited after publication', queue_publication_notification=true where slug='kaptein-knopkop';
do $$ begin
    assert (select count(*)=1 from public.notification_publications);
    assert (select cardinality(audience)=3744 and processed=0 from public.notification_publications);
    assert not exists(select from public.stories where queue_publication_notification);
end $$;
reset role;

-- Hold an uncommitted batch open in another connection. A competing worker
-- skips the locked job. After the first worker rolls back, its batch is available.
create extension dblink;
select dblink_connect('worker', format('host=127.0.0.1 port=%s dbname=%s', inet_server_port(), current_database()));
select dblink_exec('worker','set role service_role');
select dblink_exec('worker','begin');
do $$ begin
    assert (select n=200 from dblink('worker','select public.process_notification_publication_batch()') as t(n integer));
    assert public.process_notification_publication_batch()=0;
    assert (select processed=0 from public.notification_publications);
end $$;
select dblink_exec('worker','rollback');
select dblink_disconnect('worker');
set role service_role;
do $$ begin assert public.process_notification_publication_batch()=200; end $$;
reset role;

-- Fail the checkpoint after this batch's inbox rows and deliveries have been inserted.
-- The entire batch must roll back. A later connection can resume from that checkpoint.
create trigger fail_checkpoint before update on public.notification_publications for each row execute function public.test_fail_write();
set role service_role;
do $$ begin
    begin
        perform public.process_notification_publication_batch();
        raise exception 'Expected checkpoint failure';
    exception when sqlstate 'Z0001' then null; end;
    assert (select processed=200 from public.notification_publications);
    assert (select count(*)=200 from public.subscriber_notifications where href='/luister/kaptein-knopkop');
end $$;
reset role;
drop trigger fail_checkpoint on public.notification_publications;
-- Subscription churn must not change the snapshot. Later device opt-ins do not backfill.
update public.subscriptions set status='cancelled' where subscriber_id in (select subscriber_id from expected where i%10=0);
insert into public.subscriptions select subscriber_id,'active',null,null from public.subscribers where email='late@example.test';
insert into public.mobile_push_devices(installation_id,installation_secret_hash,subscriber_id,platform,environment,token)
select installation_id, repeat('b',64),subscriber_id,'fcm','production',repeat(md5(installation_id::text),2) from expected where i=3744;

-- The call commits but its client discards the result. Reconnect and run again.
set role service_role;
select public.process_notification_publication_batch() \g /dev/null
\connect -
set role service_role;
do $$ begin
    assert (select processed=400 from public.notification_publications);
    while public.process_notification_publication_batch()>0 loop end loop;
    assert public.process_notification_publication_batch()=0;
    assert (select processed=3744 and completed_at is not null and cardinality(audience)=0 from public.notification_publications);
end $$;
reset role;

create view bulk_inbox as select * from public.subscriber_notifications where href='/luister/kaptein-knopkop';
create view bulk_deliveries as select d.* from public.mobile_push_deliveries d join bulk_inbox n using(notification_id);
do $$ begin
    assert (select count(*)=3744 and count(distinct subscriber_id)=3744 from bulk_inbox);
    assert not exists(select subscriber_id from expected except select subscriber_id from bulk_inbox);
    assert not exists(select from bulk_inbox where image_path<>'/covers/kaptein.jpg' or body<>'Kaptein Knopkop is nou beskikbaar. Lees saam.'
        or metadata->>'story_title'<>'Kaptein Knopkop');
    assert (select count(*)=2621 and count(distinct installation_id)=2621 from bulk_deliveries);
    assert not exists(select installation_id from expected where i<=2621 except select installation_id from bulk_deliveries);
end $$;

-- Only these synthetic queue records can be dispatched to the SQL fake provider.
-- Lose one lease before its first send, and reject a second send to any installation.
create table fake_provider_receipts(installation_id uuid primary key, notification_id uuid not null, platform text not null);
do $$ declare abandoned record; first_claim uuid; begin
    for i in 1..10 loop
        select * into strict abandoned from public.claim_mobile_push_deliveries_with_artwork(array['apns','fcm'],1);
        if i=1 then first_claim := abandoned.delivery_id; end if;
        assert abandoned.delivery_id=first_claim;
        assert exists(select from bulk_deliveries where delivery_id=abandoned.delivery_id and attempts=1);
        update public.mobile_push_deliveries set available_at=now()-interval '23 hours' where delivery_id=abandoned.delivery_id;
    end loop;
end $$;
do $$ declare item record; batch_count integer; begin
    loop
        batch_count := 0;
        for item in select * from public.claim_mobile_push_deliveries_with_artwork(array['apns','fcm'],20) loop
            batch_count := batch_count + 1;
            assert item.href='/luister/kaptein-knopkop';
            assert public.can_send_mobile_push_delivery(item.delivery_id,item.lease_id);
            insert into fake_provider_receipts values (item.installation_id,item.notification_id,item.platform);
            perform public.record_mobile_push_acceptance(item.delivery_id,gen_random_uuid());
            assert public.can_send_mobile_push_delivery(item.delivery_id,item.lease_id), 'Wrong registration acknowledged';
            perform public.record_mobile_push_acceptance(item.delivery_id,item.registration_id);
            perform public.record_mobile_push_acceptance(item.delivery_id,item.registration_id); -- DB reply lost
        end loop;
        exit when batch_count=0;
    end loop;
    assert (select count(*)=2621 from fake_provider_receipts);
    assert (select count(*)=2621 from bulk_deliveries where state='sent');
    assert not exists(select installation_id from expected where i<=2621 except select installation_id from fake_provider_receipts);
end $$;
select (select count(*) from bulk_inbox) as inbox_recipients,
       count(*) filter(where platform='apns') as simulated_apple,
       count(*) filter(where platform='fcm') as simulated_android,
       count(distinct installation_id) as distinct_accepted from fake_provider_receipts;

insert into public.resource_types(name,slug) values ('Aktiwiteite','aktiwiteite');
set role service_role;
insert into public.resource_documents(title,slug,resource_type_id,is_enabled,queue_publication_notification)
select 'Storiekaart','storiekaart',resource_type_id,true,true from public.resource_types;
update public.resource_documents set title='Changed',queue_publication_notification=true where slug='storiekaart';
insert into public.blog_posts(title,slug,is_published,published_at,queue_publication_notification) values
    ('Artikel','artikel',true,now(),true),('Later','later',true,now()+interval '1 week',true);
update public.blog_posts set title='Edited',queue_publication_notification=true where slug='artikel';
do $$ begin
    assert (select count(*)=3 from public.notification_publications);
    while public.process_notification_publication_batch()>0 loop end loop;
    assert exists(select from public.subscriber_notifications where href='/blog/artikel' and title='Artikel' and body=title);
    assert exists(select from public.subscriber_notifications where href='/resources?tipe=aktiwiteite'
        and body='Storiekaart is nou beskikbaar.' and image_path='/media/resources/' || (metadata->>'resource_document_id') || '/preview');
    assert not has_function_privilege('authenticated','public.process_notification_publication_batch()','EXECUTE');
    assert not has_function_privilege('anon','public.record_mobile_push_acceptance(uuid,uuid)','EXECUTE');
    assert not has_table_privilege('authenticated','public.notification_publications','SELECT');
end $$;
reset role;
select 'PASS: publication rollback, fixed audience, batch rollback, restart, lost replies, exact recipient and device sets, all publication types' as result;
