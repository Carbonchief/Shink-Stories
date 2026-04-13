create or replace function private.current_jwt_email()
returns text
language sql
stable
set search_path = pg_catalog
as $$
    select lower(coalesce(auth.jwt() ->> 'email', ''))
$$;
