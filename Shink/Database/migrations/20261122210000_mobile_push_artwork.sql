-- The original claim function owns leasing and audience checks. Keep it callable by older workers.
begin;
create or replace function public.claim_mobile_push_deliveries_with_artwork(p_platforms text[], p_take integer default 20)
returns table (
    delivery_id uuid, lease_id uuid, installation_id uuid, registration_id uuid, notification_id uuid,
    platform text, environment text, token text, title text, body text, notification_type text, href text, image_path text
) language sql volatile security invoker set search_path = '' as $$
    select c.*, n.image_path
    from public.claim_mobile_push_deliveries(p_platforms, p_take) c
    join public.subscriber_notifications n using (notification_id);
$$;
revoke all on function public.claim_mobile_push_deliveries_with_artwork(text[], integer) from public, anon, authenticated;
grant execute on function public.claim_mobile_push_deliveries_with_artwork(text[], integer) to service_role;
commit;
