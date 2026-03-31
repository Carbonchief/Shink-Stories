-- Flatten story cover/thumbnail image paths to the new /stories root.
-- This migration leaves external (http/https) images untouched and only
-- rewrites local assets that still point to nested imported/misc/thumbs paths.

update public.stories
set
    cover_image_path = '/stories/' ||
        split_part(
            cover_image_path,
            '/',
            array_length(regexp_split_to_array(cover_image_path, '/'), 1)
        ),
    thumbnail_image_path = case
        when thumbnail_image_path is null then null
        when thumbnail_image_path ~* '^https?://' then thumbnail_image_path
        else '/stories/' ||
            split_part(
                thumbnail_image_path,
                '/',
                array_length(regexp_split_to_array(thumbnail_image_path, '/'), 1)
            )
    end
where
    cover_image_path is not null
    and cover_image_path !~* '^https?://'
    and (
        cover_image_path like '/stories/%/%' -- nested under /stories/...
        or cover_image_path like '/media/imported/misc/%'
    );

-- Optional: ensure thumbnail follows cover when missing.
update public.stories
set thumbnail_image_path = cover_image_path
where thumbnail_image_path is null and cover_image_path is not null;
