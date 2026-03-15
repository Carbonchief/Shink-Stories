-- Update tags for Skepper se Tuin Deel 02.

update public.stories
set tags = (
    select array_agg(distinct tag order by tag)
    from unnest(coalesce(public.stories.tags, '{}'::text[]) || array['subscriber','bybel','byble']) as tag
    where tag is not null and btrim(tag) <> ''
)
where slug = 'schink-stories-die-skepper-se-tuin-deel-02';