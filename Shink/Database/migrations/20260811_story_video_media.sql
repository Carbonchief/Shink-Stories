-- Store uploaded video media separately from story audio.

alter table public.stories
    add column if not exists video_bucket text,
    add column if not exists video_object_key text,
    add column if not exists video_content_type text;

alter table public.stories
    drop constraint if exists stories_video_bucket_not_blank,
    drop constraint if exists stories_video_object_key_not_blank,
    drop constraint if exists stories_video_media_fields_required,
    drop constraint if exists stories_published_requires_audio,
    drop constraint if exists stories_published_requires_media;

alter table public.stories
    add constraint stories_video_bucket_not_blank
        check (video_bucket is null or btrim(video_bucket) <> ''),
    add constraint stories_video_object_key_not_blank
        check (video_object_key is null or btrim(video_object_key) <> ''),
    add constraint stories_video_media_fields_required
        check (video_object_key is null or video_bucket is not null),
    add constraint stories_published_requires_media
        check (
            status <> 'published'
            or (
                (coalesce(story_type, 'story') = 'video' and video_object_key is not null)
                or (coalesce(story_type, 'story') <> 'video' and audio_object_key is not null)
            )
        );

comment on column public.stories.video_bucket is
    'Cloudflare R2 bucket containing the uploaded video object.';
comment on column public.stories.video_object_key is
    'Cloudflare R2 object key for a video story.';
comment on column public.stories.video_content_type is
    'MIME type for the video story object, normally video/mp4 or video/webm.';
