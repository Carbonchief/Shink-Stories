-- Use dedicated cover for "God se ooreekoms met Abraham" (Story 10).

update public.stories
set
    cover_image_path = '/media/imported/misc/2025/02/10_Abram_Glo_vir_God-600x600.jpg',
    thumbnail_image_path = '/media/imported/misc/2025/02/10_Abram_Glo_vir_God-600x600.jpg'
where slug = 'god-se-ooreekoms-met-abraham';