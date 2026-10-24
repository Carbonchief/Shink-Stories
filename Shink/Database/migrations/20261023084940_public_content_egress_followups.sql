-- Compact, fresh character counts; retain raw events for analytics.
create or replace function public.get_character_profile_listen_snapshot(p_subscriber_id uuid)
returns jsonb language sql stable security invoker set search_path = '' as $function$
with whitespace as (
 select U&'\0009\000A\000B\000C\000D\0020\0085\00A0\1680\2000\2001\2002\2003\2004\2005\2006\2007\2008\2009\200A\2028\2029\202F\205F\3000' as chars
), summaries as (
 select p.character_id, lower(btrim(p.character_slug, w.chars)) as character_slug,
        count(*) as listen_count, max(p.occurred_at) as last_listened_at
 from public.character_audio_plays p cross join whitespace w
 where p.subscriber_id = p_subscriber_id
   and p.character_id is not null
   and p.character_id <> '00000000-0000-0000-0000-000000000000'::uuid
   and nullif(btrim(p.character_slug, w.chars), '') is not null
 group by p.character_id, lower(btrim(p.character_slug, w.chars))
)
select coalesce(jsonb_agg(to_jsonb(summaries) order by last_listened_at desc, character_slug, character_id), '[]'::jsonb)
from summaries;
$function$;
revoke all on function public.get_character_profile_listen_snapshot(uuid) from public, anon, authenticated;
grant execute on function public.get_character_profile_listen_snapshot(uuid) to service_role;

-- One response replaces page + unread count + history count. Badge requests
-- never select notification message bodies. Existing subscriber RLS still applies.
create or replace function public.get_notification_page_snapshot(
 p_subscriber_id uuid, p_take integer default 10, p_before timestamptz default null,
 p_history boolean default false, p_include_items boolean default true)
returns jsonb language sql stable security invoker set search_path = '' as $function$
with settings as (
 select greatest(1, least(coalesce(p_take, 10), 25)) as take
), page as materialized (
 select n.notification_id, n.notification_type, n.title, n.body, n.image_path,
        n.image_alt, n.href, n.metadata, n.created_at, n.read_at, n.cleared_at
 from public.subscriber_notifications n
 where p_include_items and n.subscriber_id = p_subscriber_id
   and (p_history or n.cleared_at is null)
   and (p_before is null or n.created_at < p_before)
 order by n.created_at desc
 limit (select take + 1 from settings)
), shown as materialized (
 select * from page order by created_at desc limit (select take from settings)
), oldest as (
 select min(created_at) as created_at from shown
 where notification_id <> '00000000-0000-0000-0000-000000000000'::uuid
)
select jsonb_build_object(
 'notifications', coalesce((select jsonb_agg(to_jsonb(shown) order by created_at desc) from shown), '[]'::jsonb),
 'unread_count', (select count(*) from public.subscriber_notifications n
                  where n.subscriber_id=p_subscriber_id and n.read_at is null and n.cleared_at is null),
 'has_more', p_include_items and (select count(*) from page) > (select take from settings),
 'has_history', p_include_items and exists (
   select 1 from public.subscriber_notifications n
   where n.subscriber_id=p_subscriber_id
     and ((select created_at from oldest) is null or n.created_at < (select created_at from oldest))
 ));
$function$;
revoke all on function public.get_notification_page_snapshot(uuid,integer,timestamptz,boolean,boolean) from public, anon, authenticated;
grant execute on function public.get_notification_page_snapshot(uuid,integer,timestamptz,boolean,boolean) to service_role;

-- Keep listing/search/access fields and character previews. Player-only fields
-- are fetched for a single selected story. Preserve legacy metadata classifiers.
create or replace function public.get_story_catalogue_listing()
returns jsonb language sql stable security invoker set search_path = '' as $function$
select coalesce(jsonb_agg(jsonb_build_object(
 'story_id',s.story_id,'slug',s.slug,'title',s.title,'summary',s.summary,'description',s.description,
 'cover_image_path',s.cover_image_path,'thumbnail_image_path',s.thumbnail_image_path,
 'audio_provider',s.audio_provider,'audio_bucket',s.audio_bucket,'audio_object_key',s.audio_object_key,
 'audio_content_type',s.audio_content_type,'video_bucket',s.video_bucket,'video_object_key',s.video_object_key,
 'video_content_type',s.video_content_type,'story_type',s.story_type,'access_level',s.access_level,
 'status',s.status,'sort_order',s.sort_order,'published_at',s.published_at,'duration_seconds',s.duration_seconds,
 'tags',s.tags,'is_summary',true,
 'metadata',
   (case when jsonb_typeof(s.metadata)='object' and jsonb_typeof(s.metadata->'story_details')='object'
     then jsonb_build_object('story_details', jsonb_strip_nulls(jsonb_build_object(
       'synopsis',s.metadata->'story_details'->'synopsis',
       'characters',s.metadata->'story_details'->'characters',
       'values',s.metadata->'story_details'->'values',
       'value_tags',s.metadata->'story_details'->'value_tags')))
     else '{}'::jsonb end)
   || (case when s.metadata::text ilike '%soundbite%' then '{"soundbite":true}'::jsonb else '{}'::jsonb end)
   || (case when s.metadata::text ilike '%bybel%' or s.metadata::text ilike '%byble%'
        then '{"bybel":true}'::jsonb else '{}'::jsonb end)
) order by s.published_at desc nulls last, s.sort_order), '[]'::jsonb)
from public.stories s where s.status='published';
$function$;
revoke all on function public.get_story_catalogue_listing() from public;
grant execute on function public.get_story_catalogue_listing() to anon, authenticated, service_role;

-- Revisions contain no user data. Public clients may read these six counters;
-- only the trigger owner may update them. This covers writes on every instance
-- and direct database publishing, unpublishing, edits and deletion.
create table public.public_content_cache_revisions (
 cache_key text primary key,
 revision bigint not null default 1 check (revision > 0)
);
alter table public.public_content_cache_revisions enable row level security;
revoke all on table public.public_content_cache_revisions from public, anon, authenticated, service_role;
grant select on table public.public_content_cache_revisions to anon, authenticated, service_role;
create policy public_content_cache_revisions_read on public.public_content_cache_revisions
 for select to anon, authenticated, service_role using (true);
insert into public.public_content_cache_revisions(cache_key)
 values ('stories'),('characters'),('blogs'),('products'),('resources'),('landing_pages');

create or replace function public.bump_public_content_cache_revision()
returns trigger language plpgsql security definer set search_path = '' as $function$
begin
 update public.public_content_cache_revisions set revision=revision+1 where cache_key=TG_ARGV[0];
 return null;
end;
$function$;
revoke all on function public.bump_public_content_cache_revision() from public, anon, authenticated, service_role;

do $triggers$
declare entry record;
begin
 for entry in select * from (values
  ('stories','stories'),('story_playlists','stories'),('story_playlist_items','stories'),
  ('story_characters','characters'),('blog_posts','blogs'),('blog_categories','blogs'),
  ('blog_tags','blogs'),('blog_post_tags','blogs'),('store_products','products'),
  ('resource_types','resources'),('resource_documents','resources'),('landing_pages','landing_pages')
 ) as mapping(table_name, cache_key)
 loop
  execute format('create trigger invalidate_public_content_cache after insert or update or delete or truncate on public.%I for each statement execute function public.bump_public_content_cache_revision(%L)', entry.table_name, entry.cache_key);
 end loop;
end;
$triggers$;
notify pgrst, 'reload schema';
