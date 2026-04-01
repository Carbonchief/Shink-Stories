-- Switch story audio metadata from local paths to Cloudflare R2 (public development URL).

with normalized as (
    select
        story_id,
        split_part(regexp_replace(replace(audio_object_key, E'\\', '/'), '^.*/', ''), '?', 1) as normalized_file_name,
        lower(split_part(regexp_replace(replace(audio_object_key, E'\\', '/'), '^.*/', ''), '?', 1)) as normalized_file_name_lower
    from public.stories
    where
        audio_object_key is not null
        and btrim(audio_object_key) <> ''
)
update public.stories as stories
set
    audio_provider = 'r2',
    audio_bucket = 'media.prioritybit.co.za',
    audio_object_key = normalized.normalized_file_name,
    audio_content_type = coalesce(
        nullif(stories.audio_content_type, ''),
        case
            when normalized.normalized_file_name_lower like '%.mp3' then 'audio/mpeg'
            when normalized.normalized_file_name_lower like '%.mpeg' then 'audio/mpeg'
            when normalized.normalized_file_name_lower like '%.m4a' then 'audio/mp4'
            when normalized.normalized_file_name_lower like '%.wav' then 'audio/wav'
            when normalized.normalized_file_name_lower like '%.ogg' then 'audio/ogg'
            else 'audio/mpeg'
        end
    )
from normalized
where stories.story_id = normalized.story_id;
