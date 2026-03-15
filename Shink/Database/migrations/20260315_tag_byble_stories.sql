-- Tag Bible/Christian stories with user-requested tag spelling: byble.

update public.stories
set tags = (
    select array_agg(distinct tag order by tag)
    from unnest(coalesce(public.stories.tags, '{}'::text[]) || array['byble']) as tag
    where tag is not null and btrim(tag) <> ''
)
where
    coalesce(audio_object_key, '') ilike '%bybel%'
    or coalesce(title, '') ~* '(bybel|bible|christ|jesus|abram|abraham|isak|jacob|josef|adam|eva|kain|abel|noag|eden|skepping|paasfees|babel|koning)'
    or coalesce(slug, '') ~* '(bybel|bible|jesus|abram|abraham|isak|jacob|josef|adam|eva|kain|abel|noag|eden|skepping|paasfees|babel|koning)'
    or coalesce(tags, '{}'::text[]) @> array['bybel']::text[];