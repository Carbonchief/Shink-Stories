-- Use a distinct Skepper cover so it no longer duplicates Deel 03.

update public.stories
set
    cover_image_path = '/stories/imported/2025/11/Schink-Stories_Die_Skepper_se_tuin-600x600.png',
    thumbnail_image_path = '/stories/imported/2025/11/Schink-Stories_Die_Skepper_se_tuin-600x600.png'
where slug = 'schink-stories-skepper';