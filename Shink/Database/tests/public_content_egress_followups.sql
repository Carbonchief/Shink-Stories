-- Run ONLY against an empty, isolated local database with this exact name.
\set ON_ERROR_STOP on
begin;
do $guard$
begin
 if current_database() <> 'shink_egress_followups_regression' or exists (
  select 1 from information_schema.tables where table_schema='public') then
  raise exception 'Requires an empty isolated shink_egress_followups_regression database';
 end if;
end;
$guard$;
do $roles$
begin
 if not exists(select 1 from pg_roles where rolname='anon') then create role anon; end if;
 if not exists(select 1 from pg_roles where rolname='authenticated') then create role authenticated; end if;
 if not exists(select 1 from pg_roles where rolname='service_role') then create role service_role; end if;
end;
$roles$;
create table public.character_audio_plays (
 play_id bigserial primary key, subscriber_id uuid, character_id uuid, character_slug text, occurred_at timestamptz);
create index on public.character_audio_plays(subscriber_id,character_id,occurred_at desc);
create table public.subscriber_notifications (
 notification_id uuid primary key, subscriber_id uuid, notification_type text default 'story_published',
 title text default 'Story',body text default 'Message body',image_path text,image_alt text,href text,
 metadata jsonb default '{}',created_at timestamptz,read_at timestamptz,cleared_at timestamptz);
create index on public.subscriber_notifications(subscriber_id,created_at desc);
create table public.stories (
 story_id uuid,slug text,title text,summary text,description text,youtube_url text,test_questions jsonb,
 cover_image_path text,thumbnail_image_path text,audio_provider text,audio_bucket text,audio_object_key text,
 audio_content_type text,video_bucket text,video_object_key text,video_content_type text,story_type text,
 access_level text,status text,sort_order integer,published_at timestamptz,duration_seconds integer,tags text[],metadata jsonb);
do $tables$
declare name text;
begin
 foreach name in array array['story_playlists','story_playlist_items','story_characters','blog_posts',
  'blog_categories','blog_tags','blog_post_tags','store_products','resource_types','resource_documents','landing_pages'] loop
  execute format('create table public.%I(id integer,value text)',name);
 end loop;
end;
$tables$;
grant usage on schema public to anon, authenticated, service_role;
grant select on public.character_audio_plays,public.subscriber_notifications to service_role;
grant select,insert,update,delete on public.stories,public.story_playlists to service_role;
alter table public.stories enable row level security;
grant select on public.stories to anon,authenticated;
create policy stories_read on public.stories for select to anon,authenticated using(status='published');
create policy stories_server on public.stories for all to service_role using(true) with check(true);
\ir ../migrations/20261023084940_public_content_egress_followups.sql

insert into public.character_audio_plays(subscriber_id,character_id,character_slug,occurred_at)
 select '11111111-1111-1111-1111-111111111111','22222222-2222-2222-2222-222222222222',
 case when i%2=0 then U&'\0009PaNdA\00A0' else 'panda' end,'2026-10-01Z'::timestamptz + i*interval '1 second'
 from generate_series(1,1866) i;
insert into public.character_audio_plays(subscriber_id,character_id,character_slug,occurred_at) values
 ('33333333-3333-3333-3333-333333333333','22222222-2222-2222-2222-222222222222','panda','2026-10-02Z'),
 ('11111111-1111-1111-1111-111111111111','00000000-0000-0000-0000-000000000000','invalid','2026-10-02Z'),
 ('11111111-1111-1111-1111-111111111111','22222222-2222-2222-2222-222222222222',U&'\0009\00A0','2026-10-02Z');
insert into public.subscriber_notifications(notification_id,subscriber_id,created_at,read_at,cleared_at)
 select md5(i::text)::uuid,'11111111-1111-1111-1111-111111111111','2026-10-01Z'::timestamptz+i*interval '1 minute',
 case when i between 13 and 15 then now() end,case when i>15 then now() end from generate_series(1,18) i;
insert into public.subscriber_notifications(notification_id,subscriber_id,created_at)
 values(md5('other')::uuid,'33333333-3333-3333-3333-333333333333','2026-10-02Z');
insert into public.stories(story_id,slug,title,status,description,audio_object_key,metadata,test_questions) values
 ('44444444-4444-4444-4444-444444444444','published','Published','published','Searchable text','protected.mp3',
 '{"import_note":"bybel","story_details":{"synopsis":"Synopsis","characters":["Panda"],"values":["Kindness"],"lessons":["Full lesson"],"conversation_questions":["Full question"]}}','[{"question":"Full test question"}]'),
 ('55555555-5555-5555-5555-555555555555','draft','Draft','draft','Draft body','protected.mp3','{}','[]'),
 ('66666666-6666-6666-6666-666666666666','preview','Preview','published','Preview body','preview.mp3','{"import_note":"old soundbite"}','[]');

do $checks$
declare snapshot jsonb; body jsonb; before_revision bigint; entry record; raw_bytes integer; summary_bytes integer;
begin
 snapshot := public.get_character_profile_listen_snapshot('11111111-1111-1111-1111-111111111111');
 assert jsonb_array_length(snapshot)=1;
 assert snapshot->0->>'character_slug'='panda';
 assert (snapshot->0->>'listen_count')::integer=1866;
 assert (public.get_character_profile_listen_snapshot('33333333-3333-3333-3333-333333333333')->0->>'listen_count')::integer=1;
 select octet_length(jsonb_agg(jsonb_build_object('character_id',character_id,'character_slug',character_slug,'occurred_at',occurred_at))::text)
 into raw_bytes from public.character_audio_plays where subscriber_id='11111111-1111-1111-1111-111111111111';
 summary_bytes := octet_length(snapshot::text);
 assert summary_bytes < raw_bytes/20;
 insert into public.character_audio_plays(subscriber_id,character_id,character_slug,occurred_at)
 values('11111111-1111-1111-1111-111111111111','22222222-2222-2222-2222-222222222222','panda','2026-10-23Z');
 assert (public.get_character_profile_listen_snapshot('11111111-1111-1111-1111-111111111111')->0->>'listen_count')::integer=1867;

 snapshot := public.get_notification_page_snapshot('11111111-1111-1111-1111-111111111111');
 assert jsonb_array_length(snapshot->'notifications')=10;
 assert (snapshot->>'unread_count')::integer=12;
 assert (snapshot->>'has_more')::boolean and (snapshot->>'has_history')::boolean;
 assert snapshot->'notifications'->0->>'body'='Message body';
 snapshot := public.get_notification_page_snapshot('11111111-1111-1111-1111-111111111111',10,'2026-10-01T00:06:00Z',false,true);
 assert jsonb_array_length(snapshot->'notifications')=5;
 assert not (snapshot->>'has_more')::boolean and not (snapshot->>'has_history')::boolean;
 snapshot := public.get_notification_page_snapshot('11111111-1111-1111-1111-111111111111',100,null,true,true);
 assert jsonb_array_length(snapshot->'notifications')=18;
 assert not (snapshot->>'has_more')::boolean;
 update public.subscriber_notifications set read_at=now() where notification_id=md5('1')::uuid;
 update public.subscriber_notifications set cleared_at=now() where notification_id=md5('2')::uuid;
 snapshot := public.get_notification_page_snapshot('11111111-1111-1111-1111-111111111111',10,null,false,false);
 assert (snapshot->>'unread_count')::integer=10;
 assert jsonb_array_length(snapshot->'notifications')=0;
 assert not (snapshot->>'has_more')::boolean and not (snapshot->>'has_history')::boolean;
 assert (public.get_notification_page_snapshot('33333333-3333-3333-3333-333333333333')->>'unread_count')::integer=1;

 snapshot := public.get_story_catalogue_listing();
 assert jsonb_array_length(snapshot)=2;
 select value into body from jsonb_array_elements(snapshot) where value->>'slug'='published';
 assert (body->>'is_summary')::boolean;
 assert body->>'description'='Searchable text';
 assert body->'metadata'->'story_details'->'characters'='["Panda"]'::jsonb;
 assert (body->'metadata'->>'bybel')::boolean;
 assert not body ? 'test_questions';
 assert not (body->'metadata'->'story_details') ? 'lessons';
 select value into body from jsonb_array_elements(snapshot) where value->>'slug'='preview';
 assert (body->'metadata'->>'soundbite')::boolean;

 -- All content tables invalidate on insert, edit, delete and truncate.
 for entry in select * from (values
  ('story_playlists','stories'),('story_playlist_items','stories'),('story_characters','characters'),
  ('blog_posts','blogs'),('blog_categories','blogs'),('blog_tags','blogs'),('blog_post_tags','blogs'),
  ('store_products','products'),('resource_types','resources'),('resource_documents','resources'),('landing_pages','landing_pages')
 ) as mapping(table_name,cache_key) loop
  select revision into before_revision from public.public_content_cache_revisions where cache_key=entry.cache_key;
  execute format('insert into public.%I values(1,''published'')',entry.table_name);
  execute format('update public.%I set value=''edited''',entry.table_name);
  execute format('delete from public.%I',entry.table_name);
  execute format('truncate public.%I',entry.table_name);
  assert (select revision=before_revision+4 from public.public_content_cache_revisions where cache_key=entry.cache_key);
 end loop;
 select revision into before_revision from public.public_content_cache_revisions where cache_key='stories';
 update public.stories set status='published' where slug='draft';
 assert jsonb_array_length(public.get_story_catalogue_listing())=3;
 update public.stories set status='draft' where slug='draft';
 delete from public.stories where slug='preview';
 assert (select revision=before_revision+3 from public.public_content_cache_revisions where cache_key='stories');
 assert not has_function_privilege('anon','public.get_character_profile_listen_snapshot(uuid)','EXECUTE');
 assert not has_function_privilege('authenticated','public.get_notification_page_snapshot(uuid,integer,timestamptz,boolean,boolean)','EXECUTE');
 assert has_function_privilege('service_role','public.get_character_profile_listen_snapshot(uuid)','EXECUTE');
 assert not has_table_privilege('service_role','public.public_content_cache_revisions','UPDATE');
 assert not has_function_privilege('service_role','public.bump_public_content_cache_revision()','EXECUTE');
 raise notice 'SQL parity, fresh writes, isolation, invalidation and privileges passed; profile bytes % -> %',raw_bytes,summary_bytes;
end;
$checks$;
set local role anon;
select count(*)=6 as anonymous_can_read_revisions from public.public_content_cache_revisions;
do $denied$
begin
 begin update public.public_content_cache_revisions set revision=revision+1;
  raise exception 'Anonymous revision writes must be denied';
 exception when insufficient_privilege then null; end;
 begin perform public.get_character_profile_listen_snapshot('11111111-1111-1111-1111-111111111111');
  raise exception 'Anonymous personal RPC access must be denied';
 exception when insufficient_privilege then null; end;
end;
$denied$;
reset role;
set local role service_role;
insert into public.story_playlists values(2,'server publish');
select jsonb_array_length(public.get_character_profile_listen_snapshot('11111111-1111-1111-1111-111111111111'))=1 as server_can_read_summary;
reset role;
rollback;
