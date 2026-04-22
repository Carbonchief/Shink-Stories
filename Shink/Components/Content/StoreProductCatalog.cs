using System.Globalization;

namespace Shink.Components.Content;

public sealed record StoreProduct(
    Guid? StoreProductId,
    string Slug,
    string Name,
    string? Description,
    string ImagePath,
    string AltText,
    string ThemeClass,
    decimal UnitPriceZar,
    int SortOrder = 0,
    bool IsEnabled = true)
{
    public string UnitPriceDisplay => UnitPriceZar.ToString("0.00", CultureInfo.InvariantCulture);
}

public static class StoreProductCatalog
{
    private const decimal TeddyPrice = 250.00m;

    public static IReadOnlyList<StoreProduct> All { get; } =
    [
        new(
            StoreProductId: null,
            Slug: "suurlemoentjie",
            Name: "Suurlemoentjie",
            Description: "Helder, vrolik en gereed vir stories vol sonskyn en moed.",
            ImagePath: "/branding/winkel/storie-tjommie-suurlemoentjie.png",
            AltText: "Suurlemoentjie StorieTjommie teddie",
            ThemeClass: "is-suurlemoentjie",
            UnitPriceZar: TeddyPrice),
        new(
            StoreProductId: null,
            Slug: "tiekie",
            Name: "Tiekie",
            Description: "Vir kinders wat hou van sagte troos en 'n bekende maatjie naby.",
            ImagePath: "/branding/winkel/storie-tjommie-tiekie.png",
            AltText: "Tiekie StorieTjommie teddie",
            ThemeClass: "is-tiekie",
            UnitPriceZar: TeddyPrice),
        new(
            StoreProductId: null,
            Slug: "lama-lama-pajama-lama",
            Name: "Lama Lama Pajama Lama",
            Description: "Speels en knus vir slaaptyd, speeltyd en elke giggel tussenin.",
            ImagePath: "/branding/winkel/storie-tjommie-lama-lama-pajama-lama.png",
            AltText: "Lama Lama Pajama Lama StorieTjommie teddie",
            ThemeClass: "is-lama",
            UnitPriceZar: TeddyPrice),
        new(
            StoreProductId: null,
            Slug: "georgie",
            Name: "Georgie",
            Description: "Rustige geselskap vir kinders wat lief is vir Georgie se warm persoonlikheid.",
            ImagePath: "/branding/winkel/storie-tjommie-georgie.png",
            AltText: "Georgie StorieTjommie teddie",
            ThemeClass: "is-georgie",
            UnitPriceZar: TeddyPrice)
    ];

    public static StoreProduct? FindBySlug(string? slug) =>
        All.FirstOrDefault(product => string.Equals(product.Slug, slug, StringComparison.OrdinalIgnoreCase));
}
