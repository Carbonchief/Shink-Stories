alter table public.stories
    add column if not exists test_questions jsonb;

update public.stories
set test_questions = null
where test_questions = '[]'::jsonb;

alter table public.stories
    drop constraint if exists stories_test_questions_array;

alter table public.stories
    add constraint stories_test_questions_array
        check (test_questions is null or jsonb_typeof(test_questions) = 'array');

comment on column public.stories.test_questions is
    'Optional A/B story test questions for the Luister story summary modal.';
