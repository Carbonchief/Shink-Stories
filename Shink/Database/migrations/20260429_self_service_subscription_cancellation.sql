alter table public.subscriptions
    add column if not exists provider_email_token text;

comment on column public.subscriptions.provider_email_token is
    'Provider-specific token needed for self-service recurring subscription cancellation, e.g. Paystack email_token.';
