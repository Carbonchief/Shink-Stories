alter table public.story_characters
    add column if not exists audio_clips jsonb not null default '[]'::jsonb;

alter table public.story_characters
    drop constraint if exists story_characters_audio_clips_array_check;

alter table public.story_characters
    add constraint story_characters_audio_clips_array_check
    check (jsonb_typeof(audio_clips) = 'array');

comment on column public.story_characters.audio_clips is 'Playable character audio clips served through signed /media/audio URLs.';
