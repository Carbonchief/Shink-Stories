-- Fix image mapping for "Die Kwaai Grommel".

update public.stories
set
    cover_image_path = 'https://i0.wp.com/www.schink.co.za/wp-content/uploads/2024/08/Schink_Stories_Storie_Hoekie_Afrikaanse_Stories_Die_Kwaai_Grommel_deur_Martin-Schwella.jpg?fit=1024%2C775&ssl=1',
    thumbnail_image_path = 'https://i0.wp.com/www.schink.co.za/wp-content/uploads/2024/08/Schink_Stories_Storie_Hoekie_Afrikaanse_Stories_Die_Kwaai_Grommel_deur_Martin-Schwella.jpg?fit=1024%2C775&ssl=1'
where slug in ('die-kwaai-grommel', 'die-kwaai-grommel-grom')
   or title ilike 'Die Kwaai Grommel%';
