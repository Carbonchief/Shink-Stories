alter table public.stories
    add column if not exists deleted_at timestamptz,
    add column if not exists deleted_by_admin_email text;

create index if not exists idx_stories_deleted_at
    on public.stories(deleted_at)
    where deleted_at is not null;

comment on column public.stories.deleted_at is
    'Soft-delete marker used by admin story management. Soft-deleted stories remain in the table and are archived for public playback.';

comment on column public.stories.deleted_by_admin_email is
    'Admin email that soft-deleted the story.';
