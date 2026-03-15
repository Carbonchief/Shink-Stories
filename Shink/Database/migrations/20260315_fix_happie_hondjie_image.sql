-- Fix incorrect image mapping for "n happie vir n hondjie".

update public.stories
set
    cover_image_path = '/stories/imported/2025/08/Storie_Hoekie_3_Prent_16_Blaffie_Schink-Stories-600x775.jpg',
    thumbnail_image_path = '/stories/imported/2025/08/Storie_Hoekie_3_Prent_16_Blaffie_Schink-Stories-600x775.jpg'
where slug = 'n-happie-vir-n-hondjie';