create or replace function public.delete_account_personal_data(p_email text)
returns jsonb
language plpgsql
security invoker
set search_path = pg_catalog
as $$
declare
    requested_email text := lower(btrim(coalesce(p_email, '')));
    subscriber_uuid uuid;
    deleted_email text;
    avatar_object_key text;
begin
    if requested_email = '' or position('@' in requested_email) <= 1 then
        return jsonb_build_object(
            'deleted', false,
            'message', 'Kon nie jou rekening se e-posadres lees nie.'
        );
    end if;

    select
        subscriber_id,
        profile_image_object_key
    into
        subscriber_uuid,
        avatar_object_key
    from public.subscribers
    where lower(email) = requested_email
    limit 1
    for update;

    if subscriber_uuid is null then
        delete from public.auth_sessions
        where lower(email) = requested_email;

        return jsonb_build_object(
            'deleted', true,
            'profile_image_object_key', null
        );
    end if;

    deleted_email := 'deleted+' || replace(subscriber_uuid::text, '-', '') || '@example.invalid';

    delete from public.story_views where subscriber_id = subscriber_uuid;
    delete from public.story_listen_events where subscriber_id = subscriber_uuid;
    delete from public.story_favorites where subscriber_id = subscriber_uuid;
    delete from public.story_favourites where subscriber_id = subscriber_uuid;
    delete from public.subscriber_notifications where subscriber_id = subscriber_uuid;
    delete from public.character_audio_plays where subscriber_id = subscriber_uuid;
    delete from public.subscriber_character_unlock_states where subscriber_id = subscriber_uuid;
    delete from public.subscriber_admin_audit where subscriber_id = subscriber_uuid;
    delete from public.subscription_cancellation_feedback where subscriber_id = subscriber_uuid;
    delete from public.subscription_payment_pauses where subscriber_id = subscriber_uuid;
    delete from public.subscription_plan_changes where subscriber_id = subscriber_uuid;

    update public.resource_document_download_events
    set subscriber_id = null
    where subscriber_id = subscriber_uuid;

    update public.blog_visit_events
    set subscriber_id = null
    where subscriber_id = subscriber_uuid;

    update public.oortjies_click_events
    set subscriber_id = null
    where subscriber_id = subscriber_uuid;

    update public.subscription_discount_code_redemptions
    set
        subscriber_id = null,
        email = deleted_email,
        metadata = null,
        updated_at = now()
    where subscriber_id = subscriber_uuid
       or lower(email) = requested_email;

    update public.subscription_events
    set payload = '{}'::jsonb
    where subscription_id in (
        select subscription_id
        from public.subscriptions
        where subscriber_id = subscriber_uuid
    );

    update public.subscriptions
    set
        status = 'cancelled',
        cancelled_at = coalesce(cancelled_at, now()),
        provider_token = null,
        provider_email_token = null,
        updated_at = now()
    where subscriber_id = subscriber_uuid;

    delete from public.auth_sessions
    where lower(email) = requested_email;

    delete from public.abandoned_cart_recoveries
    where lower(customer_email) = requested_email;

    delete from public.paystack_checkout_sessions
    where lower(customer_email) = requested_email;

    update public.app_error_logs
    set
        user_email = null,
        metadata = metadata - 'email' - 'user_email'
    where lower(user_email) = requested_email;

    update public.store_orders
    set
        customer_name = 'Deleted customer',
        customer_email = deleted_email,
        customer_phone = '',
        delivery_address_line_1 = 'Removed after account deletion',
        delivery_address_line_2 = null,
        delivery_suburb = null,
        delivery_city = 'Removed',
        delivery_postal_code = '0000',
        notes = null,
        raw_verify_response = null,
        raw_webhook_payload = null,
        updated_at = now()
    where lower(customer_email) = requested_email;

    delete from public.school_seats
    where lower(email) = requested_email;

    update public.school_seats
    set invited_by_email = deleted_email
    where lower(invited_by_email) = requested_email;

    update public.school_accounts
    set
        admin_email = deleted_email,
        status = 'cancelled',
        updated_at = now()
    where lower(admin_email) = requested_email;

    delete from private.wordpress_membership_periods
    where lower(wordpress_membership_periods.normalized_email) = requested_email;

    delete from private.wordpress_membership_orders
    where lower(wordpress_membership_orders.normalized_email) = requested_email;

    delete from private.wordpress_subscriptions
    where lower(wordpress_subscriptions.normalized_email) = requested_email;

    delete from private.wordpress_users
    where lower(wordpress_users.normalized_email) = requested_email;

    update public.subscribers
    set
        email = deleted_email,
        first_name = null,
        last_name = null,
        display_name = null,
        mobile_number = null,
        profile_image_url = null,
        profile_image_object_key = null,
        profile_image_content_type = null,
        last_login_at = null,
        disabled_at = now(),
        disabled_by_admin_email = 'self_service_deletion',
        disabled_reason = 'Persoonlike data deur gebruiker verwyder.',
        updated_at = now()
    where subscriber_id = subscriber_uuid;

    return jsonb_build_object(
        'deleted', true,
        'profile_image_object_key', avatar_object_key
    );
end;
$$;

grant delete on table
    private.wordpress_membership_periods,
    private.wordpress_membership_orders,
    private.wordpress_subscriptions,
    private.wordpress_users
to service_role;

revoke all on function public.delete_account_personal_data(text) from public, anon, authenticated;
grant execute on function public.delete_account_personal_data(text) to service_role;
