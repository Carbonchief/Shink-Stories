-- Supabase Data API compatibility for the 2026 explicit-grants rollout.
-- RLS policies still control row access; these grants only make intended tables
-- visible to PostgREST/GraphQL for each role.

grant select, insert, update, delete on table
    public.abandoned_cart_recoveries,
    public.admin_users,
    public.app_error_logs,
    public.auth_sessions,
    public.blog_categories,
    public.blog_post_tags,
    public.blog_posts,
    public.blog_tags,
    public.blog_visit_events,
    public.character_audio_plays,
    public.payment_webhook_failures,
    public.paystack_checkout_sessions,
    public.resource_document_download_events,
    public.resource_documents,
    public.resource_types,
    public.school_accounts,
    public.school_seats,
    public.site_settings,
    public.store_orders,
    public.store_products,
    public.stories,
    public.story_characters,
    public.story_favorites,
    public.story_favourites,
    public.story_listen_events,
    public.story_playlist_items,
    public.story_playlists,
    public.story_views,
    public.subscriber_admin_audit,
    public.subscriber_character_unlock_states,
    public.subscriber_notifications,
    public.subscribers,
    public.subscription_discount_code_redemptions,
    public.subscription_discount_code_tiers,
    public.subscription_discount_codes,
    public.subscription_events,
    public.subscription_payment_recoveries,
    public.subscription_recurring_charge_attempts,
    public.subscription_tiers,
    public.subscriptions
to service_role;

grant usage, select on all sequences in schema public to service_role;

grant select on table
    public.blog_categories,
    public.blog_post_tags,
    public.blog_posts,
    public.blog_tags,
    public.resource_documents,
    public.resource_types,
    public.store_products,
    public.stories,
    public.story_characters,
    public.story_playlist_items,
    public.story_playlists,
    public.subscription_tiers
to anon, authenticated;

grant select, update on table
    public.auth_sessions,
    public.subscriber_notifications,
    public.subscribers
to authenticated;

grant select on table
    public.store_orders,
    public.subscription_events,
    public.subscriptions
to authenticated;

grant select, insert, update, delete on table
    public.story_favorites
to authenticated;

grant select, insert, delete on table
    public.story_favourites
to authenticated;

grant select, insert on table
    public.story_listen_events,
    public.story_views
to authenticated;

grant usage, select on sequence
    public.story_favorites_story_favorite_id_seq,
    public.story_listen_events_story_listen_event_id_seq,
    public.story_views_story_view_id_seq
to authenticated;
