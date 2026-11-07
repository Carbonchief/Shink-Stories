using System.Globalization;
using Microsoft.AspNetCore.Http;

namespace Shink.Components.Content;

public static class StoreCheckoutRules
{
    public const decimal DeliveryFeeZar = 80m;
    public const int MaxPerProduct = 10;
    public const int MaxItems = 20;

    public static string FormatZar(decimal amount) => $"R {amount.ToString("0.00", CultureInfo.InvariantCulture)}";
    public static int ToCents(decimal amount) => decimal.ToInt32(decimal.Round(amount * 100m, 0, MidpointRounding.AwayFromZero));

    // Never silently drop a disabled/deleted product from a submitted basket.
    public static bool HasUnavailableProducts(IFormCollection form, IReadOnlyList<StoreProduct> products)
    {
        var knownFields = products.Where(product => product.IsEnabled)
            .Select(product => $"qty-{product.Slug}").ToHashSet(StringComparer.Ordinal);
        return form.Any(field => field.Key.StartsWith("qty-", StringComparison.Ordinal) &&
            !knownFields.Contains(field.Key) && field.Value.ToString() != "0");
    }

    public static bool MatchesDisplayedTotal(string? displayedTotalCents, decimal actualTotal) =>
        int.TryParse(displayedTotalCents, NumberStyles.None, CultureInfo.InvariantCulture, out var displayed) &&
        displayed > 0 && displayed == ToCents(actualTotal);
}
