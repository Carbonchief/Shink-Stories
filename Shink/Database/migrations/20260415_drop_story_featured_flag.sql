drop index if exists idx_stories_featured_sort;

alter table public.stories
drop column if exists is_featured;
