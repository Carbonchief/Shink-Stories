-- Reclassify <=60s audio as soundbites and align storage paths.

update public.stories
set audio_object_key = case
    when audio_object_key = 'imported/non-story-audio/2024/05/Voorbeeld_Storie.mp3'
        then 'imported/soundbites/2024/05/Voorbeeld_Storie.mp3'
    when audio_object_key = 'imported/non-story-audio/2024/05/Voorbeeld_Storie_02.mp3'
        then 'imported/soundbites/2024/05/Voorbeeld_Storie_02.mp3'
    when audio_object_key = 'imported/stories/2025/09/Schink-_Stories_Die_Kwaai_Grommel_GROM.mp3'
        then 'imported/soundbites/2025/09/Schink-_Stories_Die_Kwaai_Grommel_GROM.mp3'
    when audio_object_key like 'imported/non-story-audio/%'
        then regexp_replace(audio_object_key, '^imported/non-story-audio/', 'imported/soundbites/')
    when duration_seconds is not null and duration_seconds <= 60 and audio_object_key like 'imported/stories/%'
        then regexp_replace(audio_object_key, '^imported/stories/', 'imported/soundbites/')
    else audio_object_key
end
where audio_object_key is not null
  and (
      audio_object_key in (
          'imported/non-story-audio/2024/05/Voorbeeld_Storie.mp3',
          'imported/non-story-audio/2024/05/Voorbeeld_Storie_02.mp3',
          'imported/stories/2025/09/Schink-_Stories_Die_Kwaai_Grommel_GROM.mp3'
      )
      or audio_object_key like 'imported/non-story-audio/%'
      or (duration_seconds is not null and duration_seconds <= 60 and audio_object_key like 'imported/stories/%')
  );

update public.stories
set duration_seconds = case
    when audio_object_key = 'imported/soundbites/2024/05/Voorbeeld_Storie.mp3' then 43
    when audio_object_key = 'imported/soundbites/2024/05/Voorbeeld_Storie_02.mp3' then 59
    when audio_object_key = 'imported/soundbites/2025/09/Schink-_Stories_Die_Kwaai_Grommel_GROM.mp3' then 15
    else duration_seconds
end
where audio_object_key in (
    'imported/soundbites/2024/05/Voorbeeld_Storie.mp3',
    'imported/soundbites/2024/05/Voorbeeld_Storie_02.mp3',
    'imported/soundbites/2025/09/Schink-_Stories_Die_Kwaai_Grommel_GROM.mp3'
);

update public.stories
set metadata = jsonb_set(
        coalesce(metadata, '{}'::jsonb),
        '{content_type}',
        to_jsonb('soundbite'::text),
        true
    )
where
    (duration_seconds is not null and duration_seconds <= 60)
    or (audio_object_key is not null and audio_object_key like 'imported/soundbites/%')
    or (audio_object_key is not null and audio_object_key like 'imported/non-story-audio/%');