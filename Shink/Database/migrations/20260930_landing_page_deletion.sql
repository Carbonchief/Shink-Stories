-- Additive soft deletion: content and uploaded assets are retained.
-- Apply before running the updated landing-page admin service.
alter table public.landing_pages add column if not exists deleted_at timestamptz;
do $$
begin
    if not exists (select 1 from pg_constraint where conrelid = 'public.landing_pages'::regclass and conname = 'landing_pages_deleted_not_published') then
        alter table public.landing_pages add constraint landing_pages_deleted_not_published check (deleted_at is null or not is_published);
    end if;
end;
$$;
