update public.story_characters
set
    image_path = format('/branding/characters/%s.png', slug),
    mystery_image_path = format('/branding/characters/%s-mystery.png', slug),
    updated_at = now()
where slug in (
    'roomie',
    'suurlemoentjie',
    'benjamin-die-bulhond',
    'tiekie',
    'henry-skilpad',
    'die-kwaaibok',
    'georgie',
    'prinses-panda',
    'daantjie',
    'grootseun-die-wilde-bakkie',
    'rammetjie-uitnek',
    'floris-dinosaurus',
    'boeta-seeumeeu',
    'sussa-seeumeeu',
    'grommel',
    'stewels',
    'meneer-de-buffel',
    'oom-boom',
    'ompel',
    'snoet',
    'strompel',
    'hailey-hasie',
    'arno-arend',
    'meneer-maniere'
);
