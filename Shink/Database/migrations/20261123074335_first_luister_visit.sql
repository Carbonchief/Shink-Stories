-- Existing subscribers have already been welcomed. Only future inserts start pending.
alter table public.subscribers add column if not exists first_luister_visit_id uuid;
alter table public.subscribers alter column first_luister_visit_id
    set default '00000000-0000-0000-0000-000000000000';

comment on column public.subscribers.first_luister_visit_id is
    'NULL for pre-existing accounts, zero UUID before the first Luister visit, otherwise the ID of that visit.';

create or replace function public.record_first_luister_visit(p_email text, p_visit_id uuid)
returns boolean
language plpgsql
security invoker
set search_path = ''
as $$
declare
    pending constant uuid := '00000000-0000-0000-0000-000000000000';
begin
    if p_visit_id is null or p_visit_id = pending then
        return false;
    end if;

    update public.subscribers
    set first_luister_visit_id = p_visit_id
    where email = lower(btrim(p_email)) and first_luister_visit_id = pending;
    if found then
        return true;
    end if;

    -- A retried request for the winning visit keeps the same display order.
    return exists (select from public.subscribers
        where email = lower(btrim(p_email)) and first_luister_visit_id = p_visit_id);
end;
$$;

revoke all on function public.record_first_luister_visit(text, uuid) from public, anon, authenticated;
grant execute on function public.record_first_luister_visit(text, uuid) to service_role;
