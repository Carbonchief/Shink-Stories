alter table public.story_characters
    add column if not exists unlock_rules jsonb not null default '[]'::jsonb;

alter table public.story_characters
    drop constraint if exists story_characters_unlock_rules_array_check;

alter table public.story_characters
    add constraint story_characters_unlock_rules_array_check
    check (jsonb_typeof(unlock_rules) = 'array');

comment on column public.story_characters.unlock_rules is 'Rule-based unlock configuration for characters. Each entry supports story listen seconds, repeat counts, story counts, unlocked character counts, and profile listen counts.';

update public.story_characters as character
set unlock_rules = jsonb_build_array(
    jsonb_build_object(
        'rule_type', 'story_listen_seconds',
        'target_slugs', to_jsonb(targets.target_slugs),
        'target_match_mode', 'any',
        'minimum_count', 0,
        'minimum_seconds', greatest(1, coalesce(character.unlock_threshold_seconds, 30))
    )
)
from (
    select
        current_character.character_id,
        coalesce(
            array_agg(distinct target_slug) filter (where target_slug is not null and btrim(target_slug) <> ''),
            '{}'::text[]
        ) as target_slugs
    from public.story_characters as current_character
    left join lateral (
        select nullif(btrim(current_character.unlock_story_slug), '') as target_slug
        union all
        select nullif(btrim(related_slug), '')
        from unnest(coalesce(current_character.related_story_slugs, '{}'::text[])) as related_slug
    ) as targets on true
    group by current_character.character_id
) as targets
where character.character_id = targets.character_id
  and coalesce(jsonb_array_length(character.unlock_rules), 0) = 0
  and cardinality(targets.target_slugs) > 0;

create table if not exists public.character_audio_plays (
    play_id bigint generated always as identity primary key,
    subscriber_id uuid not null references public.subscribers(subscriber_id) on delete cascade,
    character_id uuid not null references public.story_characters(character_id) on delete cascade,
    character_slug text not null,
    stream_slug text not null,
    occurred_at timestamptz not null default now(),
    metadata jsonb not null default '{}'::jsonb,
    constraint character_audio_plays_character_slug_check check (character_slug ~ '^[a-z0-9]+(?:-[a-z0-9]+)*$'),
    constraint character_audio_plays_stream_slug_check check (stream_slug ~ '^[a-z0-9]+(?:-[a-z0-9]+)*$'),
    constraint character_audio_plays_metadata_object_check check (jsonb_typeof(metadata) = 'object')
);

comment on table public.character_audio_plays is 'Profile audio play events used for character unlock rules based on profile listens.';

create index if not exists idx_character_audio_plays_subscriber_character_occurred
    on public.character_audio_plays(subscriber_id, character_id, occurred_at desc);

create index if not exists idx_character_audio_plays_subscriber_slug_occurred
    on public.character_audio_plays(subscriber_id, character_slug, occurred_at desc);

alter table public.character_audio_plays enable row level security;

drop policy if exists character_audio_plays_service_role_all on public.character_audio_plays;
create policy character_audio_plays_service_role_all
on public.character_audio_plays
for all
to service_role
using (true)
with check (true);
