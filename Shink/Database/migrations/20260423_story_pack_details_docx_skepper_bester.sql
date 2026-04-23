-- Import story pack details from Skepper/Bester DOCX sources.
with updates (
    slug,
    source_file,
    source_title,
    synopsis,
    lessons,
    values_display,
    value_tags,
    questions,
    characters
) as (
    values
    ($q$schink-stories-die-skepper-se-tuin-deel$q$, $q$Storie Pakkie Inligting_Die_Skepper_Se_Tuin.docx$q$, $q$Daisy en Willempie$q$, $q$In die middel van die Skepper se groot tuin ontmoet ons vir Daisy, ’n klein blommetjie wat haar lewe binne-in ’n blompotjie deurbring. Sy gesels met haar maatjie Willempie Wurm, wat droom oor dinge wat hy nie heeltemal verstaan nie, terwyl hulle albei wonder oor hul plek in die groot tuin.$q$, array[$q$Alles in die Skepper se tuin het ’n spesiale tyd en ’n doel$q$, $q$Jou drome is belangrik, selfs al verstaan jy dit nog nie heeltemal nie$q$, $q$Die Skepper se handewerk is fyn beplan vir elkeen van ons$q$]::text[], array[$q$Geduld$q$, $q$Liefde$q$, $q$Selfwaardering$q$]::text[], array[$q$geduld$q$, $q$liefde$q$, $q$selfwaardering$q$]::text[], array[$q$Waarom dink jy voel Daisy so veilig in haar blompotjie?$q$, $q$Wat dink jy beteken dit dat alles "op sy tyd" gebeur?$q$, $q$As jy ’n droom gehad het soos Willempie, wat sou dit wees?$q$]::text[], array[$q$Daisy (die daisy-blom)$q$, $q$Willempie Wurm$q$]::text[]),
    ($q$schink-stories-die-skepper-se-tuin-deel-02$q$, $q$Storie Pakkie Inligting_Die_Skepper_Se_Tuin.docx$q$, $q$Dis ‘n kraak sê Oom Tor!$q$, $q$Oom Tor die kewer kom kuier en maak ’n ontdekking wat Daisy se hele wêreld skud. Skielik moet Daisy en Willempie vrese hanteer waaraan hulle nog nooit vantevore gedink het nie. Terwyl Daisy bekommerd is oor haar veiligheid, probeer Willempie sin maak van hoe dinge soms moet verander.$q$, array[$q$Verandering kan bangmaak, maar dit is dikwels nodig vir groei$q$, $q$Moenie dat bekommernis jou moed laat sak nie$q$, $q$Wat ons as "stukkend" sien, is dalk deel van ’n groter plan$q$]::text[], array[$q$Moed$q$, $q$Vertroue$q$, $q$Sorgsaamheid$q$]::text[], array[$q$moed$q$, $q$vertroue$q$, $q$sorgsaamheid$q$]::text[], array[$q$Hoe sou jy vir Daisy probeer opbeur as sy hartseer is oor haar pot?$q$, $q$Waarom is Oom Tor so besorg oor sy vriende?$q$, $q$Dink jy verandering is altyd ’n slegte ding?$q$]::text[], array[$q$Daisy$q$, $q$Willempie Wurm$q$, $q$Oom Tor (die kewer)$q$]::text[]),
    ($q$schink-stories-die-skepper-se-tuin-deel-03$q$, $q$Storie Pakkie Inligting_Die_Skepper_Se_Tuin.docx$q$, $q$Lieben val uit die lug!$q$, $q$Net toe Daisy op haar droewigste voel, daag daar ’n onverwagse besoeker uit die lug op wat alles kom deurmekaarkrap. Lieben die liewenheersbesie bring nuwe inligting oor die tuin en oor wie Daisy en Willempie eintlik is – inligting wat hulle dwing om heeltemal anders na hulself te kyk.$q$, array[$q$Jy is baie meer as die "pot" waarin jy jouself bevind$q$, $q$Jou storie is nie net wat jy nou kan sien nie; daar is ’n groter prentjie$q$, $q$’n Kraak is nie altyd ’n einde nie, maar kan ’n nuwe opening wees$q$]::text[], array[$q$Identiteit$q$, $q$Hoop$q$, $q$Openhartigheid$q$]::text[], array[$q$identiteit$q$, $q$hoop$q$, $q$openhartigheid$q$]::text[], array[$q$Wat het Lieben vir Daisy vertel oor ander blomme in die tuin?$q$, $q$Waarom dink jy is Daisy skaam oor haar gekraakte pot?$q$, $q$Hoe help Lieben vir Willempie om sy eie drome te verstaan?$q$]::text[], array[$q$Daisy$q$, $q$Willempie$q$, $q$Lieben (die liewenheersbesie)$q$]::text[]),
    ($q$n-opening$q$, $q$Storie Pakkie Inligting_Die_Skepper_Se_Tuin.docx$q$, $q$‘n Opening$q$, $q$Die tyd vir wonderwerke het aangebreek. Terwyl Willempie besig is met sy eie groot verandering, moet Daisy besluit of sy gaan vashou aan die verlede of gaan vertrou op ’n nuwe begin. Die nag bring antwoorde en ’n transformasie wat die hele tuin gaan verander.$q$, array[$q$Om vry te word, moet mens soms die dinge wat jou terughou, laat gaan$q$, $q$Die Skepper se glorie word sigbaar wanneer ons word wie ons gemaak is om te wees$q$, $q$Daar is ’n seisoen vir alles, en alles werk uit op die regte tyd$q$]::text[], array[$q$Vryheid$q$, $q$Oorgawe$q$, $q$Dankbaarheid$q$]::text[], array[$q$vryheid$q$, $q$oorgawe$q$, $q$dankbaarheid$q$]::text[], array[$q$Wat het Daisy gedoen om uiteindelik "vry" te voel?$q$, $q$Hoe het Willempie gelyk toe sy metamorfose klaar was?$q$, $q$Hoe wys die tuin aan die einde die Skepper se goedheid?$q$]::text[], array[$q$Daisy$q$, $q$Willempie$q$, $q$Oom Tor$q$, $q$Lieben$q$]::text[]),
    ($q$schink-stories-die-bester-biltong-ooit$q$, $q$Storie Pakkie Inligting_Bester.docx$q$, $q$Wouter Bester het ‘n droom$q$, $q$Wouter Bester het ’n droom om sy eie slaghuis oop te maak, maar die pad is nie altyd maklik nie. Hy moet leer om sy sokkies op te trek en sy droom te bou, selfs wanneer ander mense twyfel of daar plek is vir nog ’n slaghuis in Bloemfontein.$q$, array[$q$Moenie dat ander mense se twyfel jou droom steel nie$q$, $q$As jy glo en hard werk, kan jy ’n plan maak om jou drome waar te maak$q$, $q$Sukses begin by die besluit om selfs in moeilike tye aan te hou probeer$q$]::text[], array[$q$Geloof$q$, $q$Harde werk$q$, $q$Deursettingsvermoë$q$]::text[], array[$q$geloof$q$, $q$harde werk$q$, $q$deursettingsvermoë$q$]::text[], array[$q$Waarom dink jy het die tannie vir Wouter se plan gelag?$q$, $q$Wat beteken dit om "jou bester" te doen?$q$, $q$Hoe het Marie vir Wouter gehelp om die slaghuis uniek te maak?$q$]::text[], array[$q$Wouter Bester$q$, $q$Marie Bester$q$, $q$Die tannie$q$]::text[]),
    ($q$schink-stories-diebester-biltong-ooit-storie$q$, $q$Storie Pakkie Inligting_Bester.docx$q$, $q$Wouter Bester het ‘n droom$q$, $q$Wouter Bester het ’n droom om sy eie slaghuis oop te maak, maar die pad is nie altyd maklik nie. Hy moet leer om sy sokkies op te trek en sy droom te bou, selfs wanneer ander mense twyfel of daar plek is vir nog ’n slaghuis in Bloemfontein.$q$, array[$q$Moenie dat ander mense se twyfel jou droom steel nie$q$, $q$As jy glo en hard werk, kan jy ’n plan maak om jou drome waar te maak$q$, $q$Sukses begin by die besluit om selfs in moeilike tye aan te hou probeer$q$]::text[], array[$q$Geloof$q$, $q$Harde werk$q$, $q$Deursettingsvermoë$q$]::text[], array[$q$geloof$q$, $q$harde werk$q$, $q$deursettingsvermoë$q$]::text[], array[$q$Waarom dink jy het die tannie vir Wouter se plan gelag?$q$, $q$Wat beteken dit om "jou bester" te doen?$q$, $q$Hoe het Marie vir Wouter gehelp om die slaghuis uniek te maak?$q$]::text[], array[$q$Wouter Bester$q$, $q$Marie Bester$q$, $q$Die tannie$q$]::text[]),
    ($q$die-slaghuis-se-deure-is-oop$q$, $q$Storie Pakkie Inligting_Bester.docx$q$, $q$Die Slaghuis se deure is oop!$q$, $q$Wouter se droom is nou ’n werklikheid, maar hy ontdek vinnig dat sukses ook "groeipyne" het. Hy probeer om alles alleen te doen en sukkel met hulp wat nie wil saamwerk nie. Wouter moet leer dat hy nie alleen kan vlieg nie en dat die regte span die sleutel is tot ’n gesonde droom.$q$, array[$q$Jy kan nie alles alleen doen nie; die regte mense om jou laat jou droom seëvier$q$, $q$’n Lui gesindheid help niemand nie, maar ’n "can-do" houding verander alles$q$, $q$Moenie moed opgee as die droom waarmee jy begin het, skielik teen jou begin "baklei" nie$q$]::text[], array[$q$Spanwerk$q$, $q$Leierskap$q$, $q$Positiwiteit$q$]::text[], array[$q$spanwerk$q$, $q$leierskap$q$, $q$positiwiteit$q$]::text[], array[$q$Hoekom het die kliënte vir Wouter "The Flying Butcher" genoem?$q$, $q$Wat was die verskil tussen die eerste helper en die nuwe span?$q$, $q$Hoe het Wouter se vroutjie hom gehelp toe hy moedeloos op die vloer gesit het?$q$]::text[], array[$q$Wouter Bester$q$, $q$Marie Bester$q$, $q$Die lui werker$q$, $q$Die nuwe span$q$]::text[]),
    ($q$beter-as-n-droom$q$, $q$Storie Pakkie Inligting_Bester.docx$q$, $q$Groter as ‘n droom$q$, $q$Wouter se slaghuis is ’n sukses, maar iets onverwags gebeur wat sy droom heeltemal oortref. Mense van regoor die wêreld begin hoor van sy spesiale biltong. Wouter ontdek dat as jy getrou bly aan jou beste, die resultate veel groter kan wees as wat jy ooit voor gehoop of beplan het.$q$, array[$q$Wanneer jy passievol is oor wat jy doen, sal mense dit van ver af raaksien$q$, $q$Jou drome kan groter word as wat jy ooit beplan het as jy aanhou om jou beste te gee$q$, $q$As jy veg vir jou drome, sal jou drome eendag vir jou veg$q$]::text[], array[$q$Uitnemendheid$q$, $q$Dankbaarheid$q$, $q$Getrouheid$q$]::text[], array[$q$uitnemendheid$q$, $q$dankbaarheid$q$, $q$getrouheid$q$]::text[], array[$q$Van waar af het mense oral gekom om biltong te koop?$q$, $q$Wat dink jy is die geheim van Bester se geelvet-biltong?$q$, $q$Hoe voel Wouter wanneer hy aan die einde na sy besige slaghuis kyk?$q$]::text[], array[$q$Wouter Bester$q$, $q$Die getroue span$q$, $q$Kliënte van oral oor (tot van Australië!)$q$]::text[])
)
, updated as (
    update public.stories as story
    set
        summary = updates.synopsis,
        description = updates.synopsis,
        tags = (
            select coalesce(array_agg(tag order by tag), array[]::text[])
            from (
                select distinct lower(btrim(tag)) as tag
                from unnest(coalesce(story.tags, array[]::text[]) || coalesce(updates.value_tags, array[]::text[])) as tag
                where tag is not null
                  and btrim(tag) <> ''
            ) as merged_tags
        ),
        metadata = jsonb_set(
            coalesce(story.metadata, '{}'::jsonb),
            '{story_details}',
            jsonb_build_object(
                'source', updates.source_file,
                'source_title', updates.source_title,
                'synopsis', updates.synopsis,
                'lessons', to_jsonb(updates.lessons),
                'values', to_jsonb(updates.values_display),
                'value_tags', to_jsonb(updates.value_tags),
                'conversation_questions', to_jsonb(updates.questions),
                'characters', to_jsonb(updates.characters),
                'imported_at_utc', to_char(timezone('utc', now()), 'YYYY-MM-DD"T"HH24:MI:SS"Z"')
            ),
            true
        )
    from updates
    where story.slug = updates.slug
    returning story.slug
)
select
    (select count(*) from updates) as requested_updates,
    (select count(*) from updated) as applied_updates,
    (select array_agg(slug order by slug) from updated) as updated_slugs;
