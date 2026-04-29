update public.subscriber_notifications as notification
set title = coalesce(nullif(notification.metadata ->> 'post_title', ''), post.title, notification.title),
    body = null
from public.blog_posts as post
where notification.notification_type = 'blog_published'
  and (
      post.post_id::text = notification.metadata ->> 'post_id'
      or notification.source_key = 'blog-published-' || replace(post.post_id::text, '-', '')
  );

update public.subscriber_notifications
set title = case
        when btrim(body) ~ '^"[^"]+"'
            then substring(btrim(body) from '^"([^"]+)"')
        else btrim(body)
    end,
    body = null
where notification_type = 'blog_published'
  and title = 'Nuwe blog plasing'
  and body is not null
  and btrim(body) <> '';

update public.subscriber_notifications
set body = null
where notification_type = 'blog_published';
