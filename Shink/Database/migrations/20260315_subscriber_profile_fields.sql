alter table public.subscribers
    add column if not exists display_name text;

alter table public.subscribers
    add column if not exists mobile_number text;

do $$
begin
    if not exists (
        select 1
        from pg_constraint
        where conname = 'subscribers_display_name_length'
          and conrelid = 'public.subscribers'::regclass
    ) then
        alter table public.subscribers
            add constraint subscribers_display_name_length
            check (display_name is null or char_length(display_name) between 2 and 120);
    end if;
end;
$$;

do $$
begin
    if not exists (
        select 1
        from pg_constraint
        where conname = 'subscribers_mobile_number_format'
          and conrelid = 'public.subscribers'::regclass
    ) then
        alter table public.subscribers
            add constraint subscribers_mobile_number_format
            check (mobile_number is null or mobile_number ~ '^\+?[0-9]{7,20}$');
    end if;
end;
$$;
