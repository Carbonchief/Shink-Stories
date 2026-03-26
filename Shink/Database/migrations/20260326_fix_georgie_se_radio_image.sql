-- Fix image mapping for "Georgie se Radio".

update public.stories
set
    cover_image_path = '/stories/imported/2024/05/Storie_06_Georgie_se_Radio.jpg',
    thumbnail_image_path = '/stories/imported/2024/05/Storie_06_Georgie_se_Radio-600x454.jpg'
where slug = 'georgie-se-radio';
