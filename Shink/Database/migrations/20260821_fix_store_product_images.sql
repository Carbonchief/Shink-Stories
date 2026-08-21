-- Restore the canonical artwork for the four seeded StorieTjommie products.
-- This intentionally changes image_path only; custom products are unaffected.
update public.store_products
set image_path = case slug
    when 'suurlemoentjie' then '/branding/winkel/storie-tjommie-suurlemoentjie.png'
    when 'tiekie' then '/branding/winkel/storie-tjommie-tiekie.png'
    when 'lama-lama-pajama-lama' then '/branding/winkel/storie-tjommie-lama-lama-pajama-lama.png'
    when 'georgie' then '/branding/winkel/storie-tjommie-georgie.png'
end
where slug in ('suurlemoentjie', 'tiekie', 'lama-lama-pajama-lama', 'georgie');
