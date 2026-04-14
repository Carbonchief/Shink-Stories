alter table public.story_characters
    add column if not exists character_category text;

update public.story_characters
set character_category = 'ander'
where character_category is null
   or btrim(character_category) = ''
   or lower(btrim(character_category)) not in ('hoofkarakter', 'ander', 'bonus', 'bonus-2');

alter table public.story_characters
    alter column character_category set default 'ander';

alter table public.story_characters
    alter column character_category set not null;

alter table public.story_characters
    drop constraint if exists story_characters_category_check;

alter table public.story_characters
    add constraint story_characters_category_check
    check (character_category in ('hoofkarakter', 'ander', 'bonus', 'bonus-2'));

comment on column public.story_characters.character_category is 'Admin-managed character grouping: hoofkarakter, ander, bonus, or bonus-2.';
