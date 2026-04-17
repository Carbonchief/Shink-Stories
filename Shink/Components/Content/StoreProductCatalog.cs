using System.Globalization;

namespace Shink.Components.Content;

public sealed record StoreProduct(
    string Slug,
    string Name,
    string ImagePath,
    string AltText,
    string ThemeClass,
    decimal UnitPriceZar)
{
    public string UnitPriceDisplay => UnitPriceZar.ToString("0.00", CultureInfo.InvariantCulture);
}

public static class StoreProductCatalog
{
    private const decimal TeddyPrice = 250.00m;

    public static IReadOnlyList<StoreProduct> All { get; } =
    [
        new(
            Slug: "suurlemoentjie",
            Name: "Suurlemoentjie",
            ImagePath: "/branding/winkel/storie-tjommie-suurlemoentjie.png",
            AltText: "Suurlemoentjie StorieTjommie teddie",
            ThemeClass: "is-suurlemoentjie",
            UnitPriceZar: TeddyPrice),
        new(
            Slug: "tiekie",
            Name: "Tiekie",
            ImagePath: "/branding/winkel/storie-tjommie-tiekie.png",
            AltText: "Tiekie StorieTjommie teddie",
            ThemeClass: "is-tiekie",
            UnitPriceZar: TeddyPrice),
        new(
            Slug: "lama-lama-pajama-lama",
            Name: "Lama Lama Pajama Lama",
            ImagePath: "/branding/winkel/storie-tjommie-lama-lama-pajama-lama.png",
            AltText: "Lama Lama Pajama Lama StorieTjommie teddie",
            ThemeClass: "is-lama",
            UnitPriceZar: TeddyPrice),
        new(
            Slug: "georgie",
            Name: "Georgie",
            ImagePath: "/branding/winkel/storie-tjommie-georgie.png",
            AltText: "Georgie StorieTjommie teddie",
            ThemeClass: "is-georgie",
            UnitPriceZar: TeddyPrice)
    ];

    public static StoreProduct? FindBySlug(string? slug) =>
        All.FirstOrDefault(product => string.Equals(product.Slug, slug, StringComparison.OrdinalIgnoreCase));
}
