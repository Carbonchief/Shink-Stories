alter table public.stories
    add column if not exists youtube_url text;

update public.stories
set youtube_url = null
where youtube_url is not null
  and btrim(youtube_url) = '';

alter table public.stories
    drop constraint if exists stories_youtube_url_not_blank;

alter table public.stories
    add constraint stories_youtube_url_not_blank
        check (youtube_url is null or btrim(youtube_url) <> '');

comment on column public.stories.youtube_url is
    'Optional YouTube URL for the Luister story summary card video embed.';
