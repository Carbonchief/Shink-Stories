create table if not exists public.subscriber_character_unlock_states (
    subscriber_id uuid not null references public.subscribers(subscriber_id) on delete cascade,
    character_id uuid not null references public.story_characters(character_id) on delete cascade,
    is_unlocked boolean not null default false,
    unlock_count integer not null default 0,
    updated_at timestamptz not null default now(),
    constraint subscriber_character_unlock_states_pkey primary key (subscriber_id, character_id),
    constraint subscriber_character_unlock_states_unlock_count_check check (unlock_count >= 0)
);

create index if not exists idx_subscriber_character_unlock_states_updated_at
    on public.subscriber_character_unlock_states(subscriber_id, updated_at desc);

comment on table public.subscriber_character_unlock_states is 'Tracks the latest evaluated unlock state per subscriber and character so unlock notifications fire only on locked-to-unlocked transitions.';
comment on column public.subscriber_character_unlock_states.unlock_count is 'Number of distinct unlock transitions recorded for this subscriber and character.';

alter table public.subscriber_character_unlock_states enable row level security;
