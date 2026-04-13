create policy "subscribers_select_own"
    on public.subscribers
    for select
    to authenticated
    using (
        lower(email) = lower(coalesce(auth.jwt() ->> 'email', ''))
    );

create policy "subscribers_update_own"
    on public.subscribers
    for update
    to authenticated
    using (
        lower(email) = lower(coalesce(auth.jwt() ->> 'email', ''))
    )
    with check (
        lower(email) = lower(coalesce(auth.jwt() ->> 'email', ''))
    );

create policy "subscriber_notifications_select_own"
    on public.subscriber_notifications
    for select
    to authenticated
    using (
        exists (
            select 1
            from public.subscribers
            where subscribers.subscriber_id = subscriber_notifications.subscriber_id
              and lower(subscribers.email) = lower(coalesce(auth.jwt() ->> 'email', ''))
        )
    );

create policy "subscriber_notifications_update_own"
    on public.subscriber_notifications
    for update
    to authenticated
    using (
        exists (
            select 1
            from public.subscribers
            where subscribers.subscriber_id = subscriber_notifications.subscriber_id
              and lower(subscribers.email) = lower(coalesce(auth.jwt() ->> 'email', ''))
        )
    )
    with check (
        exists (
            select 1
            from public.subscribers
            where subscribers.subscriber_id = subscriber_notifications.subscriber_id
              and lower(subscribers.email) = lower(coalesce(auth.jwt() ->> 'email', ''))
        )
    );
