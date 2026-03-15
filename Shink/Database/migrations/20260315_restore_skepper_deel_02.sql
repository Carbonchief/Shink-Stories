-- Restore missing Skepper se Tuin Deel 02 story.

insert into public.stories (
    slug,
    title,
    summary,
    description,
    cover_image_path,
    thumbnail_image_path,
    audio_provider,
    audio_object_key,
    audio_content_type,
    access_level,
    status,
    is_featured,
    sort_order,
    duration_seconds,
    published_at,
    metadata
)
values (
    'schink-stories-die-skepper-se-tuin-deel-02',
    'Schink Stories Die Skepper se Tuin Deel 02',
    'Deel 02 van Die Skepper se Tuin.',
    'Luister na Schink Stories Die Skepper se Tuin Deel 02 op Schink Stories.',
    '/stories/imported/2025/11/Schink-Stories_Die_Skepper_se_tuin_02-600x600.png',
    '/stories/imported/2025/11/Schink-Stories_Die_Skepper_se_tuin_02-600x600.png',
    'local',
    'imported/stories/2025/11/Schink-_Stories_Skepper_02.mp3',
    'audio/mpeg',
    'subscriber',
    'published',
    false,
    0,
    291,
    '2025-11-01T00:12:00+00:00'::timestamptz,
    jsonb_build_object(
        'restored', true,
        'restored_reason', 'missing_deel_02',
        'restored_at_utc', now()
    )
)
on conflict (slug) do update
set
    title = excluded.title,
    summary = excluded.summary,
    description = excluded.description,
    cover_image_path = excluded.cover_image_path,
    thumbnail_image_path = excluded.thumbnail_image_path,
    audio_provider = excluded.audio_provider,
    audio_object_key = excluded.audio_object_key,
    audio_content_type = excluded.audio_content_type,
    access_level = excluded.access_level,
    status = excluded.status,
    is_featured = excluded.is_featured,
    sort_order = excluded.sort_order,
    duration_seconds = excluded.duration_seconds,
    published_at = excluded.published_at,
    metadata = coalesce(public.stories.metadata, '{}'::jsonb) || excluded.metadata;