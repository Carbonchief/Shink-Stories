with seed_rows as (
    select *
    from (
        values
            (
                'toekie-toekan',
                'Toekie Toekan',
                'ander',
                'Toekie Toekan',
                'toekie-toekan',
                array['toekie-toekan']::text[],
                440,
                jsonb_strip_nulls(jsonb_build_object(
                    'seed_source', 'Karakters-20260415',
                    'source_stem', '40_Toekie_Toekan',
                    'source_filename', 'Schink_Stories_Karakter_40_Toekie_Toekan.png',
                    'notes', 'Story title confirmed from the live site.'
                ))
            ),
            (
                'rakker',
                'Rakker',
                'ander',
                'Rakker en die ruimte',
                'rakker-en-die-ruimte',
                array['rakker-en-die-ruimte']::text[],
                450,
                jsonb_strip_nulls(jsonb_build_object(
                    'seed_source', 'Karakters-20260415',
                    'source_stem', '41_Rakker',
                    'source_filename', 'Schink_Stories_Karakter_41_Rakker.png',
                    'notes', 'Story title confirmed from the live site.'
                ))
            ),
            (
                'moeg-dinosaurus',
                'Moeg Dinosaurus',
                'ander',
                'Dinosaurus is moeg',
                'dinosaurus-is-moeg',
                array['dinosaurus-is-moeg']::text[],
                460,
                jsonb_strip_nulls(jsonb_build_object(
                    'seed_source', 'Karakters-20260415',
                    'source_stem', '42_Moeg_Dinosaurus',
                    'source_filename', 'Schink_Stories_Karakter_42_Moeg_Dinosaurus.png',
                    'notes', 'Story title confirmed from the live site.'
                ))
            ),
            (
                'die-lelike-praat',
                'Die Lelike Praat',
                'ander',
                'Dankie en die Lelike Praat',
                'schink-stories-dankie-en-die-lelike-praat',
                array['schink-stories-dankie-en-die-lelike-praat']::text[],
                470,
                jsonb_strip_nulls(jsonb_build_object(
                    'seed_source', 'Karakters-20260415',
                    'source_stem', '43_Die_Lelike_Praat',
                    'source_filename', 'Schink_Stories_Karakter_43_Die_Lelike_Praat.png',
                    'notes', 'Story title confirmed from the live site and mapped to the current app slug.'
                ))
            ),
            (
                'die-bang-soldaatjie',
                'Die Bang Soldaatjie',
                'ander',
                'Die Bang Soldaatjie',
                'die-bang-soldaatjie',
                array['die-bang-soldaatjie']::text[],
                480,
                jsonb_strip_nulls(jsonb_build_object(
                    'seed_source', 'Karakters-20260415',
                    'source_stem', '44_Die_Bang_Soldaatjie',
                    'source_filename', 'Schink_Stories_Karakter_44_Die_Bang_Soldaatjie.png',
                    'notes', 'Story title confirmed from the live site.'
                ))
            ),
            (
                'modderman',
                'Modderman',
                'ander',
                'Modderman',
                'modderman',
                array['modderman']::text[],
                490,
                jsonb_strip_nulls(jsonb_build_object(
                    'seed_source', 'Karakters-20260415',
                    'source_stem', '45_Modderman',
                    'source_filename', 'Schink_Stories_Karakter_45_Modderman.png',
                    'notes', 'Story title confirmed from the live site.'
                ))
            ),
            (
                'donsie',
                'Donsie',
                'ander',
                null,
                null,
                '{}'::text[],
                500,
                jsonb_strip_nulls(jsonb_build_object(
                    'seed_source', 'Karakters-20260415',
                    'source_stem', '46_Donsie',
                    'source_filename', 'Schink_Stories_Karakter_46_Donsie.png',
                    'notes', 'Character imported from art pack; no matching story exists in the current app database yet.'
                ))
            ),
            (
                'blaffie',
                'Blaffie',
                'ander',
                '''n Happie vir ''n Hondjie',
                'n-happie-vir-n-hondjie',
                array['n-happie-vir-n-hondjie']::text[],
                510,
                jsonb_strip_nulls(jsonb_build_object(
                    'seed_source', 'Karakters-20260415',
                    'source_stem', '47_Blaffie',
                    'source_filename', 'Schink_Stories_Karakter_47_Blaffie.png',
                    'notes', 'Story title confirmed from the live site.'
                ))
            ),
            (
                'meerkruiertjie',
                'Meerkruiertjie',
                'ander',
                'Meerkruiertjie',
                'meerkruiertjie',
                array['meerkruiertjie']::text[],
                520,
                jsonb_strip_nulls(jsonb_build_object(
                    'seed_source', 'Karakters-20260415',
                    'source_stem', '48_Meerkruiertjie',
                    'source_filename', 'Schink_Stories_Karakter_48_Meerkruiertjie.png',
                    'notes', 'Story title confirmed from the live site.'
                ))
            ),
            (
                'haas',
                'Haas',
                'ander',
                'Die Ware Wenner',
                'die-ware-wenner',
                array['die-ware-wenner', 'haas-ontdek-sy-spoed']::text[],
                530,
                jsonb_strip_nulls(jsonb_build_object(
                    'seed_source', 'Karakters-20260415',
                    'source_stem', '50_Haas',
                    'source_filename', 'Schink_Stories_Karakter_50_Haas.png',
                    'notes', 'Live character post links Haas to Die Ware Wenner and Haas ontdek sy spoed.'
                ))
            ),
            (
                'skillie',
                'Skillie',
                'ander',
                'Die Ware Wenner',
                'die-ware-wenner',
                array['die-ware-wenner', 'haas-ontdek-sy-spoed']::text[],
                540,
                jsonb_strip_nulls(jsonb_build_object(
                    'seed_source', 'Karakters-20260415',
                    'source_stem', '50_Skillie',
                    'source_filename', 'Schink_Stories_Karakter_50_Skillie.png',
                    'notes', 'Mapped to the same story pair as Haas based on the live cover art.'
                ))
            ),
            (
                'nee',
                'Nee',
                'ander',
                'Nee en Dankie',
                'nee-en-dankie',
                array['nee-en-dankie']::text[],
                550,
                jsonb_strip_nulls(jsonb_build_object(
                    'seed_source', 'Karakters-20260415',
                    'source_stem', '51_Nee',
                    'source_filename', 'Schink_Stories_Karakter_51_Nee.png',
                    'notes', 'Story title confirmed from the live site.'
                ))
            ),
            (
                'gracie',
                'Gracie',
                'ander',
                'Hou My Net Vas',
                'schink-stories-hou-my-net-vas',
                array['schink-stories-hou-my-net-vas']::text[],
                560,
                jsonb_strip_nulls(jsonb_build_object(
                    'seed_source', 'Karakters-20260415',
                    'source_stem', '52_Gracie',
                    'source_filename', 'Schink_Stories_Karakter_52_Gracie.png',
                    'notes', 'Mapped visually to the Hou My Net Vas cover art.'
                ))
            ),
            (
                'bobbo',
                'Bobbo',
                'ander',
                'Bobbo se Boeremusiek',
                'bobbo-se-boeremusiek',
                array['bobbo-se-boeremusiek']::text[],
                570,
                jsonb_strip_nulls(jsonb_build_object(
                    'seed_source', 'Karakters-20260415',
                    'source_stem', '53_Bobbo',
                    'source_filename', 'Schink_Stories_Karakter_53_Bobbo.png',
                    'notes', 'Story title confirmed from the live site.'
                ))
            ),
            (
                'fantjie',
                'Fantjie',
                'ander',
                'Fantjie leer skryf',
                'fantjie-leer-skryf',
                array['fantjie-leer-skryf']::text[],
                580,
                jsonb_strip_nulls(jsonb_build_object(
                    'seed_source', 'Karakters-20260415',
                    'source_stem', '55_Fantjie',
                    'source_filename', 'Schink_Stories_Karakter_55_Fantjie.png',
                    'notes', 'Story title confirmed from the live site.'
                ))
            ),
            (
                'aarbeitjie',
                'Aarbeitjie',
                'ander',
                'Aarbeitjie',
                'aarbeitjie',
                array['aarbeitjie']::text[],
                590,
                jsonb_strip_nulls(jsonb_build_object(
                    'seed_source', 'Karakters-20260415',
                    'source_stem', '56_Aarbeitjie',
                    'source_filename', 'Schink_Stories_Karakter_56_Aarbeitjie.png',
                    'notes', 'Story title confirmed from the live site.'
                ))
            ),
            (
                'jan-die-brandweerman',
                'Jan die Brandweerman',
                'ander',
                'Jan die Brandweerman',
                'jan-die-brandweerman',
                array['jan-die-brandweerman']::text[],
                600,
                jsonb_strip_nulls(jsonb_build_object(
                    'seed_source', 'Karakters-20260415',
                    'source_stem', '57_Jan_die_brandweerman_(Vlammetjie)',
                    'source_filename', 'Schink_Stories_Karakter_57_Jan_die_brandweerman_(Vlammetjie).png',
                    'notes', 'Filename included the Vlammetjie variant label; the site uses the story title as the character name.'
                ))
            ),
            (
                'maatjie-die-akker-saadjie',
                'Maatjie die Akker-saadjie',
                'ander',
                'Maatjie die Akker-saadjie',
                'maatjie-die-akker-saadjie',
                array['maatjie-die-akker-saadjie']::text[],
                610,
                jsonb_strip_nulls(jsonb_build_object(
                    'seed_source', 'Karakters-20260415',
                    'source_stem', '58_Maatjie_die_Akker-saadjie',
                    'source_filename', 'Schink_Stories_Karakter_58_Maatjie_die_Akker-saadjie.png',
                    'notes', 'Story title confirmed from the live site.'
                ))
            ),
            (
                'slappie',
                'Slappie',
                'ander',
                'Slappie en Sloep',
                'slappie-en-sloep',
                array['slappie-en-sloep']::text[],
                620,
                jsonb_strip_nulls(jsonb_build_object(
                    'seed_source', 'Karakters-20260415',
                    'source_stem', '59_Slappie',
                    'source_filename', 'Schink_Stories_Karakter_59_Slappie.png',
                    'notes', 'Story title confirmed from the live site.'
                ))
            ),
            (
                'sloep',
                'Sloep',
                'ander',
                'Slappie en Sloep',
                'slappie-en-sloep',
                array['slappie-en-sloep']::text[],
                630,
                jsonb_strip_nulls(jsonb_build_object(
                    'seed_source', 'Karakters-20260415',
                    'source_stem', '59_Sloep',
                    'source_filename', 'Schink_Stories_Karakter_59_Sloep.png',
                    'notes', 'Story title confirmed from the live site.'
                ))
            ),
            (
                'die-huil-oor-alles',
                'Die Huil-oor-alles',
                'ander',
                'Dankie en die Huil-oor-alles',
                'dankie-en-die-huil-oor-alles',
                array['dankie-en-die-huil-oor-alles']::text[],
                640,
                jsonb_strip_nulls(jsonb_build_object(
                    'seed_source', 'Karakters-20260415',
                    'source_stem', '60_Die huil-oor-alles',
                    'source_filename', 'Schink_Stories_Karakter_60_Die huil-oor-alles.png',
                    'notes', 'Story title confirmed from the live site.'
                ))
            ),
            (
                'die-hoed-wat-groet',
                'Die Hoed wat groet',
                'ander',
                'Die Fluistervarke en die hoed wat groet',
                'die-fluistervarke-en-die-hoed-wat-groet',
                array['die-fluistervarke-en-die-hoed-wat-groet', 'die-fluistervarke']::text[],
                650,
                jsonb_strip_nulls(jsonb_build_object(
                    'seed_source', 'Karakters-20260415',
                    'source_stem', '61_Die_Hoed_wat_groet',
                    'source_filename', 'Schink_Stories_Karakter_61_Die_Hoed_wat_groet.png',
                    'notes', 'Mapped to the current Fluistervarke story set.'
                ))
            ),
            (
                'klein-boet',
                'Klein boet',
                'ander',
                'Die Fluistervarke en die hoed wat groet',
                'die-fluistervarke-en-die-hoed-wat-groet',
                array['die-fluistervarke-en-die-hoed-wat-groet', 'die-fluistervarke']::text[],
                660,
                jsonb_strip_nulls(jsonb_build_object(
                    'seed_source', 'Karakters-20260415',
                    'source_stem', '61_Klein_boet',
                    'source_filename', 'Schink_Stories_Karakter_61_Klein_boet.png',
                    'notes', 'Mapped visually to the Fluistervarke cover art.'
                ))
            ),
            (
                'rudie-renoster',
                'Rudie Renoster',
                'ander',
                'Rudi Renoster speel rof',
                'rudi-renoster-speel-rof',
                array['rudi-renoster-speel-rof']::text[],
                670,
                jsonb_strip_nulls(jsonb_build_object(
                    'seed_source', 'Karakters-20260415',
                    'source_stem', '62_Rudie_Renoster',
                    'source_filename', 'Schink_Stories_Karakter_62_Rudie_Renoster.png',
                    'notes', 'Display name follows the imported art; unlock story uses the published Rudi slug.'
                ))
            ),
            (
                'wilnie-deelnie',
                'Wilnie Deelnie',
                'ander',
                'Dankie en Wilnie Deelnie',
                'dankie-en-wilnie-deelnie',
                array['dankie-en-wilnie-deelnie']::text[],
                680,
                jsonb_strip_nulls(jsonb_build_object(
                    'seed_source', 'Karakters-20260415',
                    'source_stem', '63_Wilnie_Deelnie_(Wil_Deel)',
                    'source_filename', 'Schink_Stories_Karakter_63_Wilnie_Deelnie_(Wil_Deel).png',
                    'notes', 'Filename included the Wil Deel alias; the site uses Wilnie Deelnie as the display name.'
                ))
            ),
            (
                'muggie',
                'Muggie',
                'ander',
                'Muggie maak maats',
                'muggie-maak-maats',
                array['muggie-maak-maats']::text[],
                690,
                jsonb_strip_nulls(jsonb_build_object(
                    'seed_source', 'Karakters-20260415',
                    'source_stem', '64_Muggie',
                    'source_filename', 'Schink_Stories_Karakter_64_Muggie.png',
                    'notes', 'Story title confirmed from the live site.'
                ))
            ),
            (
                'muskiet',
                'Muskiet',
                'ander',
                'Muggie maak maats',
                'muggie-maak-maats',
                array['muggie-maak-maats']::text[],
                700,
                jsonb_strip_nulls(jsonb_build_object(
                    'seed_source', 'Karakters-20260415',
                    'source_stem', '64_Muskiet',
                    'source_filename', 'Schink_Stories_Karakter_64_Muskiet.png',
                    'notes', 'Imported from the same Muggie maak maats character bundle.'
                ))
            ),
            (
                'tom-tor',
                'Tom Tor',
                'ander',
                'Muggie maak maats',
                'muggie-maak-maats',
                array['muggie-maak-maats']::text[],
                710,
                jsonb_strip_nulls(jsonb_build_object(
                    'seed_source', 'Karakters-20260415',
                    'source_stem', '64_Tom_Tor',
                    'source_filename', 'Schink_Stories_Karakter_64_Tom_Tor.png',
                    'notes', 'Imported from the same Muggie maak maats character bundle.'
                ))
            ),
            (
                'vlieg',
                'Vlieg',
                'ander',
                'Muggie maak maats',
                'muggie-maak-maats',
                array['muggie-maak-maats']::text[],
                720,
                jsonb_strip_nulls(jsonb_build_object(
                    'seed_source', 'Karakters-20260415',
                    'source_stem', '64_Vlieg',
                    'source_filename', 'Schink_Stories_Karakter_64_Vlieg.png',
                    'notes', 'Imported from the same Muggie maak maats character bundle.'
                ))
            ),
            (
                'wim-wurmpie',
                'Wim Wurmpie',
                'ander',
                'Muggie maak maats',
                'muggie-maak-maats',
                array['muggie-maak-maats']::text[],
                730,
                jsonb_strip_nulls(jsonb_build_object(
                    'seed_source', 'Karakters-20260415',
                    'source_stem', '64_Wim_Wurmpie',
                    'source_filename', 'Schink_Stories_Karakter_64_Wim_Wurmpie.png',
                    'notes', 'Imported from the same Muggie maak maats character bundle.'
                ))
            ),
            (
                'boeloe',
                'Boeloe',
                'ander',
                null,
                null,
                '{}'::text[],
                740,
                jsonb_strip_nulls(jsonb_build_object(
                    'seed_source', 'Karakters-20260415',
                    'source_stem', '66_Boeloe',
                    'source_filename', 'Schink_Stories_Karakter_66_Boeloe.png',
                    'notes', 'Character imported from art pack; no matching story exists in the current app database yet.'
                ))
            ),
            (
                'kokkie',
                'Kokkie',
                'ander',
                'Kokketiel se kuif',
                'kokketiel-se-kuif',
                array['kokketiel-se-kuif']::text[],
                750,
                jsonb_strip_nulls(jsonb_build_object(
                    'seed_source', 'Karakters-20260415',
                    'source_stem', '67_Kokkie_Kokketiel',
                    'source_filename', 'Schink_Stories_Karakter_67_Kokkie_Kokketiel.png',
                    'notes', 'Switched the existing kokkie record to the local asset pack and aligned the ordering with the latest character drop.'
                ))
            ),
            (
                'jo-black',
                'Jo Black',
                'ander',
                'Die Alledaagse Held met Jo Black',
                null,
                array['ek-is-jojo', 'my-beste-pel-vuma', 'nooit-te-veel-om-te-deel', 'buffel', 'fist-bump', 'brawe-brak']::text[],
                760,
                jsonb_strip_nulls(jsonb_build_object(
                    'seed_source', 'Karakters-20260415',
                    'source_stem', 'DAH_01_Jo Black',
                    'source_filename', 'Schink_Stories_Karakter_DAH_01_Jo Black.png',
                    'notes', 'Series character mapped to all current Die Alledaagse Held stories.'
                ))
            ),
            (
                'jojo',
                'Jojo',
                'ander',
                'Ek is Jojo',
                'ek-is-jojo',
                array['ek-is-jojo']::text[],
                770,
                jsonb_strip_nulls(jsonb_build_object(
                    'seed_source', 'Karakters-20260415',
                    'source_stem', 'DAH_02_Jojo',
                    'source_filename', 'Schink_Stories_Karakter_DAH_02_Jojo.png',
                    'notes', 'Story title confirmed from the live site.'
                ))
            ),
            (
                'vuma',
                'Vuma',
                'ander',
                'My Beste pel, Vuma',
                'my-beste-pel-vuma',
                array['my-beste-pel-vuma']::text[],
                780,
                jsonb_strip_nulls(jsonb_build_object(
                    'seed_source', 'Karakters-20260415',
                    'source_stem', 'DAH_03_Vuma',
                    'source_filename', 'Schink_Stories_Karakter_DAH_03_Vuma.png',
                    'notes', 'Story title confirmed from the live site.'
                ))
            ),
            (
                'kat-katryn',
                'Kat (Katryn)',
                'ander',
                'Die Alledaagse Held met Jo Black',
                null,
                array['ek-is-jojo', 'my-beste-pel-vuma', 'nooit-te-veel-om-te-deel', 'buffel', 'fist-bump', 'brawe-brak']::text[],
                790,
                jsonb_strip_nulls(jsonb_build_object(
                    'seed_source', 'Karakters-20260415',
                    'source_stem', 'DAH_04_Kat_(Katryn)',
                    'source_filename', 'Schink_Stories_Karakter_DAH_04_Kat_(Katryn).png',
                    'notes', 'Series character mapped to all current Die Alledaagse Held stories pending a dedicated story match.'
                ))
            ),
            (
                'buffel',
                'Buffel',
                'ander',
                'Buffel',
                'buffel',
                array['buffel']::text[],
                800,
                jsonb_strip_nulls(jsonb_build_object(
                    'seed_source', 'Karakters-20260415',
                    'source_stem', 'DAH_05_Buffel',
                    'source_filename', 'Schink_Stories_Karakter_DAH_05_Buffel.png',
                    'notes', 'Story title confirmed from the live site.'
                ))
            ),
            (
                'brak',
                'Brak',
                'ander',
                'Brawe Brak',
                'brawe-brak',
                array['brawe-brak']::text[],
                810,
                jsonb_strip_nulls(jsonb_build_object(
                    'seed_source', 'Karakters-20260415',
                    'source_stem', 'DAH_06_Brak',
                    'source_filename', 'Schink_Stories_Karakter_DAH_06_Brak.png',
                    'notes', 'Story title confirmed from the live site.'
                ))
            ),
            (
                'lara',
                'Lara',
                'ander',
                'Die Alledaagse Held met Jo Black',
                null,
                array['ek-is-jojo', 'my-beste-pel-vuma', 'nooit-te-veel-om-te-deel', 'buffel', 'fist-bump', 'brawe-brak']::text[],
                820,
                jsonb_strip_nulls(jsonb_build_object(
                    'seed_source', 'Karakters-20260415',
                    'source_stem', 'DAH_07_Lara',
                    'source_filename', 'Schink_Stories_Karakter_DAH_07_Lara.png',
                    'notes', 'Series character mapped to all current Die Alledaagse Held stories pending a dedicated story match.'
                ))
            ),
            (
                'skillie-graad-1',
                'Skillie Graad 1',
                'ander',
                'Die Alledaagse Held met Jo Black',
                null,
                array['ek-is-jojo', 'my-beste-pel-vuma', 'nooit-te-veel-om-te-deel', 'buffel', 'fist-bump', 'brawe-brak']::text[],
                830,
                jsonb_strip_nulls(jsonb_build_object(
                    'seed_source', 'Karakters-20260415',
                    'source_stem', 'DAH_08_Skillie_Graad_1',
                    'source_filename', 'Schink_Stories_Karakter_DAH_08_Skillie_Graad_1.png',
                    'notes', 'Series character mapped to all current Die Alledaagse Held stories pending a dedicated story match.'
                ))
            ),
            (
                'wouter-bester',
                'Wouter Bester',
                'ander',
                'Wouter Bester se slaghuis droom',
                null,
                array['schink-stories-die-bester-biltong-ooit']::text[],
                840,
                jsonb_strip_nulls(jsonb_build_object(
                    'seed_source', 'Karakters-20260415',
                    'source_stem', 'DBBO_Wouter_Bester',
                    'source_filename', 'Schink_Stories_Karakter_DBBO_Wouter_Bester.png',
                    'notes', 'The original Wouter Bester story is not in the current app database, so the live Biltong story is used as the related story.'
                ))
            ),
            (
                'daisy',
                'Daisy',
                'ander',
                'Daisy en Willempie',
                null,
                '{}'::text[],
                850,
                jsonb_strip_nulls(jsonb_build_object(
                    'seed_source', 'Karakters-20260415',
                    'source_stem', 'SKP_01_Daisy',
                    'source_filename', 'Schink_Stories_Karakter_SKP_01_Daisy.png',
                    'notes', 'The live Daisy en Willempie story is not present in the current app database yet.'
                ))
            ),
            (
                'willempie',
                'Willempie',
                'ander',
                'Daisy en Willempie',
                null,
                '{}'::text[],
                860,
                jsonb_strip_nulls(jsonb_build_object(
                    'seed_source', 'Karakters-20260415',
                    'source_stem', 'SKP_02_Willempie',
                    'source_filename', 'Schink_Stories_Karakter_SKP_02_Willempie.png',
                    'notes', 'The live Daisy en Willempie story is not present in the current app database yet.'
                ))
            ),
            (
                'oom-tor',
                'Oom Tor',
                'ander',
                'Dis ''n kraak se Oom Tor',
                null,
                '{}'::text[],
                870,
                jsonb_strip_nulls(jsonb_build_object(
                    'seed_source', 'Karakters-20260415',
                    'source_stem', 'SKP_03_Oom_Tor',
                    'source_filename', 'Schink_Stories_Karakter_SKP_03_Oom_Tor.png',
                    'notes', 'The live Oom Tor story is not present in the current app database yet.'
                ))
            ),
            (
                'lieben',
                'Lieben',
                'ander',
                'Lieben val uit die lug',
                null,
                '{}'::text[],
                880,
                jsonb_strip_nulls(jsonb_build_object(
                    'seed_source', 'Karakters-20260415',
                    'source_stem', 'SKP_04_Lieben',
                    'source_filename', 'Schink_Stories_Karakter_SKP_04_Lieben.png',
                    'notes', 'The live Lieben story is not present in the current app database yet.'
                ))
            )
    ) as seed (
        slug,
        display_name,
        character_category,
        first_appearance,
        unlock_story_slug,
        related_story_slugs,
        sort_order,
        metadata
    )
)
insert into public.story_characters (
    slug,
    display_name,
    character_category,
    first_appearance,
    image_path,
    mystery_image_path,
    unlock_story_slug,
    related_story_slugs,
    status,
    sort_order,
    metadata
)
select
    seed.slug,
    seed.display_name,
    seed.character_category,
    seed.first_appearance,
    format('/branding/characters/%s.png', seed.slug),
    format('/branding/characters/%s-mystery.png', seed.slug),
    seed.unlock_story_slug,
    seed.related_story_slugs,
    'published',
    seed.sort_order,
    seed.metadata
from seed_rows as seed
on conflict (slug) do update
set
    display_name = excluded.display_name,
    character_category = excluded.character_category,
    first_appearance = excluded.first_appearance,
    image_path = excluded.image_path,
    mystery_image_path = excluded.mystery_image_path,
    unlock_story_slug = excluded.unlock_story_slug,
    related_story_slugs = excluded.related_story_slugs,
    status = excluded.status,
    sort_order = excluded.sort_order,
    metadata = coalesce(public.story_characters.metadata, '{}'::jsonb) || excluded.metadata;
