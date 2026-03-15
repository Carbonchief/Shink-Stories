-- Archive old DieKwaaiGrommel entry in favor of Die Kwaai Grommel GROM.

update public.stories
set
    status = 'archived',
    metadata = jsonb_set(
        coalesce(metadata, '{}'::jsonb),
        '{duplicate_of}',
        to_jsonb('die-kwaai-grommel-grom'::text),
        true
    )
where slug = 'diekwaaigrommel'
  and status = 'published';