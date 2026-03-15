-- Archive older duplicate Biltong story entry.

update public.stories
set
    status = 'archived',
    metadata = jsonb_set(
        coalesce(metadata, '{}'::jsonb),
        '{duplicate_of}',
        to_jsonb('schink-stories-die-bester-biltong-ooit'::text),
        true
    )
where slug = 'schink-stories-diebester-biltong-ooit-storie'
  and status = 'published';