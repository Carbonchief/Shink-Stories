-- Archive duplicate Skepper se Tuin entry so only Deel 03 remains published.

update public.stories
set
    status = 'archived',
    metadata = jsonb_set(
        coalesce(metadata, '{}'::jsonb),
        '{duplicate_of}',
        to_jsonb('schink-stories-die-skepper-se-tuin-deel-03'::text),
        true
    )
where slug = 'schink-stories-die-skepper-se-tuin-deel'
  and status = 'published';