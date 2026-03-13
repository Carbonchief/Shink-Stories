namespace Shink.Components.Content;

public sealed record PaymentPlan(
    string Slug,
    string Name,
    string TierCode,
    string ItemName,
    string ItemDescription,
    decimal Amount,
    bool IsSubscription,
    int BillingPeriodMonths,
    int BillingFrequency)
{
    public string AmountDisplay => Amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
}

public static class PaymentPlanCatalog
{
    public static IReadOnlyList<PaymentPlan> All { get; } =
    [
        new(
            Slug: "storie-hoekie-maandeliks",
            Name: "Storie Hoekie",
            TierCode: "story_corner_monthly",
            ItemName: "Storie Hoekie Maandeliks",
            ItemDescription: "Maandelikse toegang tot Storie Hoekie.",
            Amount: 55.00m,
            IsSubscription: true,
            BillingPeriodMonths: 1,
            BillingFrequency: 3),
        new(
            Slug: "schink-stories-maandeliks",
            Name: "Schink Stories",
            TierCode: "all_stories_monthly",
            ItemName: "Schink Stories Maandeliks",
            ItemDescription: "Maandelikse toegang tot alle Schink Stories.",
            Amount: 79.00m,
            IsSubscription: true,
            BillingPeriodMonths: 1,
            BillingFrequency: 3),
        new(
            Slug: "schink-stories-jaarliks",
            Name: "Schink Stories JAAR",
            TierCode: "all_stories_yearly",
            ItemName: "Schink Stories Jaarliks",
            ItemDescription: "Jaarlikse toegang tot alle Schink Stories.",
            Amount: 790.00m,
            IsSubscription: true,
            BillingPeriodMonths: 12,
            BillingFrequency: 6)
    ];

    public static PaymentPlan? FindBySlug(string? slug) =>
        All.FirstOrDefault(plan => string.Equals(plan.Slug, slug, StringComparison.OrdinalIgnoreCase));

    public static PaymentPlan? FindByTierCode(string? tierCode) =>
        All.FirstOrDefault(plan => string.Equals(plan.TierCode, tierCode, StringComparison.OrdinalIgnoreCase));
}
