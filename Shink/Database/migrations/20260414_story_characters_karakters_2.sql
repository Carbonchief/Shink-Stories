with seed_rows as (
    select *
    from (
        values
            (
                5,
                'oom-pynie',
                'Oom Pynie',
                'Suurlemoentjie',
                'suurlemoentjie',
                array['suurlemoentjie']::text[],
                jsonb_build_object(
                    'seed_source', 'Karakters-2',
                    'seed_date', '2026-04-14',
                    'notes', 'Unlock story inferred from shared character batch.'
                )
            ),
            (
                240,
                'lama-lama-pajama-lama',
                'Lama Lama',
                'Llama Llama Pajama Llama',
                'llama-llama-pajama-llama',
                array['llama-llama-pajama-llama']::text[],
                jsonb_build_object(
                    'seed_source', 'Karakters-2',
                    'seed_date', '2026-04-14',
                    'notes', 'Character art renamed from latest asset drop while keeping the existing slug.'
                )
            ),
            (
                250,
                'sjimpie',
                'Sjimpie',
                'Sjimpie se Simfonie',
                'sjimpie-se-simfonie',
                array['sjimpie-se-simfonie']::text[],
                jsonb_build_object('seed_source', 'Karakters-2', 'seed_date', '2026-04-14')
            ),
            (
                260,
                'sweis-seekoei',
                'Sweis Seekoei',
                'Seekoei Sluit sy mond toe',
                'seekoei-sluit-sy-mond-toe',
                array['seekoei-sluit-sy-mond-toe']::text[],
                jsonb_build_object('seed_source', 'Karakters-2', 'seed_date', '2026-04-14')
            ),
            (
                270,
                'grotman-grootman',
                'Grotman Grootman',
                'Grotman Grootman',
                'grotman-grootman',
                array['grotman-grootman']::text[],
                jsonb_build_object(
                    'seed_source', 'Karakters-2',
                    'seed_date', '2026-04-14',
                    'notes', 'Filename did not include a character name; mapped from the matching story slug.'
                )
            ),
            (
                280,
                'blommetjie-blom-en-astertjie-vaal',
                'Blommetjie Blom en Astertjie Vaal',
                'Blommetjie Blom Astertjie Vaal',
                'blommetjie-blom-astertjie-vaal',
                array['blommetjie-blom-astertjie-vaal', 'schink-stories-blommetjie-blom-astertjie-vaal']::text[],
                jsonb_build_object('seed_source', 'Karakters-2', 'seed_date', '2026-04-14')
            ),
            (
                290,
                'cool-krokodil',
                'Cool Krokodil',
                'Die Cool Krokodil',
                'die-cool-krokodil',
                array['die-cool-krokodil', 'schink-stories-die-cool-krokodil']::text[],
                jsonb_build_object('seed_source', 'Karakters-2', 'seed_date', '2026-04-14')
            ),
            (
                300,
                'wouter-koalabeertjie',
                'Wouter Koalabeertjie',
                'Koalbeertjie Klou',
                'koalbeertjie-klou',
                array['koalbeertjie-klou']::text[],
                jsonb_build_object('seed_source', 'Karakters-2', 'seed_date', '2026-04-14')
            ),
            (
                310,
                'ompeplompie-obbelop',
                'Ompeplompie Obbelop',
                'Ompeplompie Obbelop',
                'ompeplompie-obbelop',
                array['ompeplompie-obbelop']::text[],
                jsonb_build_object('seed_source', 'Karakters-2', 'seed_date', '2026-04-14')
            ),
            (
                320,
                'kantwieletjies',
                'Kantwieletjies',
                'Kantwieletjies',
                'kantwieletjies',
                array['kantwieletjies']::text[],
                jsonb_build_object('seed_source', 'Karakters-2', 'seed_date', '2026-04-14')
            ),
            (
                330,
                'braam-brombeer',
                'Braam Brombeer',
                'Ek Hou nie van Partytjies nie',
                'ek-hou-nie-van-partytjies-nie',
                array['ek-hou-nie-van-partytjies-nie']::text[],
                jsonb_build_object(
                    'seed_source', 'Karakters-2',
                    'seed_date', '2026-04-14',
                    'notes', 'Unlock story inferred from the matching character batch.'
                )
            ),
            (
                340,
                'jean-pou',
                'Jean Pou',
                'Hey Pou',
                'hey-pou',
                array['hey-pou']::text[],
                jsonb_build_object('seed_source', 'Karakters-2', 'seed_date', '2026-04-14')
            ),
            (
                350,
                'bip-robot',
                'Bip Robot',
                'Robot doen Reg',
                'robot-doen-reg',
                array['robot-doen-reg']::text[],
                jsonb_build_object('seed_source', 'Karakters-2', 'seed_date', '2026-04-14')
            ),
            (
                360,
                'babbelbessie',
                'Babbelbessie',
                'BabbelBessie',
                'schink-stories-babbelbessie',
                array['schink-stories-babbelbessie']::text[],
                jsonb_build_object('seed_source', 'Karakters-2', 'seed_date', '2026-04-14')
            ),
            (
                370,
                'piesang',
                'Piesang',
                'Ai Piesang',
                'schink-stories-02-ai-piesang',
                array['schink-stories-02-ai-piesang']::text[],
                jsonb_build_object('seed_source', 'Karakters-2', 'seed_date', '2026-04-14')
            ),
            (
                380,
                'kieter',
                'Kieter',
                'Die Kat Wat Alles Vat',
                'die-kat-wat-alles-vat',
                array['die-kat-wat-alles-vat']::text[],
                jsonb_build_object(
                    'seed_source', 'Karakters-2',
                    'seed_date', '2026-04-14',
                    'notes', 'Unlock story inferred from the matching character batch.'
                )
            ),
            (
                390,
                'dankie',
                'Dankie',
                'Dankie en die mislukke skree',
                'dankie-en-die-mislukke-skree',
                array[
                    'dankie-en-die-mislukke-skree',
                    'schink-stories-dankie-en-die-mislukke-skree',
                    'dankie-en-die-huil-oor-alles',
                    'schink-stories-dankie-en-die-lelike-praat',
                    'dankie-en-wilnie-deelnie'
                ]::text[],
                jsonb_build_object('seed_source', 'Karakters-2', 'seed_date', '2026-04-14')
            ),
            (
                400,
                'die-mislukke-skree',
                'Die Mislukke Skree',
                'Dankie en die mislukke skree',
                'dankie-en-die-mislukke-skree',
                array['dankie-en-die-mislukke-skree', 'schink-stories-dankie-en-die-mislukke-skree']::text[],
                jsonb_build_object('seed_source', 'Karakters-2', 'seed_date', '2026-04-14')
            ),
            (
                410,
                'vlakkie-die-bosbakkie',
                'Vlakkie die Bosbakkie',
                'Vlakkie die Bosbakkie',
                'vlakkie-die-bosbakkie',
                array['vlakkie-die-bosbakkie']::text[],
                jsonb_build_object('seed_source', 'Karakters-2', 'seed_date', '2026-04-14')
            ),
            (
                420,
                'klippie-de-klop',
                'Klippie de Klop',
                'Klippie De Klop',
                'klippie-de-klop',
                array['klippie-de-klop']::text[],
                jsonb_build_object('seed_source', 'Karakters-2', 'seed_date', '2026-04-14')
            ),
            (
                430,
                'reenboog-robot',
                'Reenboog Robot',
                'Reenboog Robot',
                'reenboog-robot',
                array['reenboog-robot']::text[],
                jsonb_build_object('seed_source', 'Karakters-2', 'seed_date', '2026-04-14')
            )
    ) as seed (
        sort_order,
        slug,
        display_name,
        first_appearance,
        unlock_story_slug,
        related_story_slugs,
        metadata
    )
)
insert into public.story_characters (
    slug,
    display_name,
    first_appearance,
    image_path,
    mystery_image_path,
    unlock_story_slug,
    related_story_slugs,
    unlock_threshold_seconds,
    status,
    sort_order,
    metadata
)
select
    seed.slug,
    seed.display_name,
    seed.first_appearance,
    format('/branding/characters/%s.png', seed.slug),
    format('/branding/characters/%s-mystery.png', seed.slug),
    seed.unlock_story_slug,
    seed.related_story_slugs,
    30,
    'published',
    seed.sort_order,
    seed.metadata
from seed_rows as seed
on conflict (slug) do update
set
    display_name = excluded.display_name,
    first_appearance = excluded.first_appearance,
    image_path = excluded.image_path,
    mystery_image_path = excluded.mystery_image_path,
    unlock_story_slug = excluded.unlock_story_slug,
    related_story_slugs = excluded.related_story_slugs,
    unlock_threshold_seconds = excluded.unlock_threshold_seconds,
    status = excluded.status,
    sort_order = excluded.sort_order,
    metadata = coalesce(public.story_characters.metadata, '{}'::jsonb) || excluded.metadata;
