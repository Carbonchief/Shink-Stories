create schema if not exists private;

create or replace function private.current_jwt_email()
returns text
language sql
stable
as $$
    select lower(coalesce(auth.jwt() ->> 'email', ''))
$$;

create or replace function private.current_subscriber_id()
returns uuid
language sql
stable
security definer
set search_path = public, pg_temp
as $$
    select subscriber_id
    from public.subscribers
    where lower(email) = private.current_jwt_email()
    limit 1
$$;

create or replace function private.subscription_owned_by_current_subscriber(target_subscription_id uuid)
returns boolean
language sql
stable
security definer
set search_path = public, pg_temp
as $$
    select exists (
        select 1
        from public.subscriptions
        where subscription_id = target_subscription_id
          and subscriber_id = private.current_subscriber_id()
    )
$$;

grant usage on schema private to postgres, service_role, authenticated;
revoke all on schema private from public, anon;
grant execute on function private.current_jwt_email() to authenticated;
grant execute on function private.current_subscriber_id() to authenticated;
grant execute on function private.subscription_owned_by_current_subscriber(uuid) to authenticated;
revoke all on function private.current_jwt_email() from public, anon;
revoke all on function private.current_subscriber_id() from public, anon;
revoke all on function private.subscription_owned_by_current_subscriber(uuid) from public, anon;

create policy "auth_sessions_select_own"
    on public.auth_sessions
    for select
    to authenticated
    using (
        lower(email) = (select private.current_jwt_email())
    );

create policy "auth_sessions_update_own"
    on public.auth_sessions
    for update
    to authenticated
    using (
        lower(email) = (select private.current_jwt_email())
    )
    with check (
        lower(email) = (select private.current_jwt_email())
    );

create policy "story_favorites_select_own"
    on public.story_favorites
    for select
    to authenticated
    using (
        subscriber_id = (select private.current_subscriber_id())
    );

create policy "story_favorites_insert_own"
    on public.story_favorites
    for insert
    to authenticated
    with check (
        subscriber_id = (select private.current_subscriber_id())
    );

create policy "story_favorites_update_own"
    on public.story_favorites
    for update
    to authenticated
    using (
        subscriber_id = (select private.current_subscriber_id())
    )
    with check (
        subscriber_id = (select private.current_subscriber_id())
    );

create policy "story_favorites_delete_own"
    on public.story_favorites
    for delete
    to authenticated
    using (
        subscriber_id = (select private.current_subscriber_id())
    );

create policy "story_favourites_select_own"
    on public.story_favourites
    for select
    to authenticated
    using (
        subscriber_id = (select private.current_subscriber_id())
    );

create policy "story_favourites_insert_own"
    on public.story_favourites
    for insert
    to authenticated
    with check (
        subscriber_id = (select private.current_subscriber_id())
    );

create policy "story_favourites_delete_own"
    on public.story_favourites
    for delete
    to authenticated
    using (
        subscriber_id = (select private.current_subscriber_id())
    );

create policy "story_views_select_own"
    on public.story_views
    for select
    to authenticated
    using (
        subscriber_id = (select private.current_subscriber_id())
    );

create policy "story_views_insert_own"
    on public.story_views
    for insert
    to authenticated
    with check (
        subscriber_id = (select private.current_subscriber_id())
    );

create policy "story_listen_events_select_own"
    on public.story_listen_events
    for select
    to authenticated
    using (
        subscriber_id = (select private.current_subscriber_id())
    );

create policy "story_listen_events_insert_own"
    on public.story_listen_events
    for insert
    to authenticated
    with check (
        subscriber_id = (select private.current_subscriber_id())
    );

create policy "subscriptions_select_own"
    on public.subscriptions
    for select
    to authenticated
    using (
        subscriber_id = (select private.current_subscriber_id())
    );

create policy "subscription_events_select_own"
    on public.subscription_events
    for select
    to authenticated
    using (
        private.subscription_owned_by_current_subscriber(subscription_id)
    );
