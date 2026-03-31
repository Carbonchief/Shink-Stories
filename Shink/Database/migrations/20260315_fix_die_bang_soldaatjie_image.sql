-- Fix incorrect image mapping for "Die Bang Soldaatjie".

update public.stories
set
    cover_image_path = '/stories/Storie_Hoekie_3_Prent_13-600x775.jpg',
    thumbnail_image_path = '/stories/Storie_Hoekie_3_Prent_13-600x775.jpg'
where slug = 'die-bang-soldaatjie';
