alter table public.stories
    add column if not exists story_type text not null default 'story';

do $$
begin
    if not exists (
        select 1
        from pg_constraint
        where conname = 'stories_story_type_check'
          and conrelid = 'public.stories'::regclass
    ) then
        alter table public.stories
            add constraint stories_story_type_check check (story_type in ('story', 'music'));
    end if;
end $$;

comment on column public.stories.story_type is 'Content type for Luister entries: story or music.';
