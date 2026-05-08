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
    int BillingFrequency,
    int? SchoolSlotLimit = null)
{
    public string AmountDisplay => Amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
    public bool IsSchoolPlan => SchoolSlotLimit.HasValue;
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
            BillingFrequency: 6),
        new(
            Slug: "skool-klein-jaarliks",
            Name: "Skool Klein",
            TierCode: "school_small_yearly",
            ItemName: "Schink Stories Skool Klein",
            ItemDescription: "Jaarlikse skooltoegang vir 4 klaskamers.",
            Amount: 6250.00m,
            IsSubscription: false,
            BillingPeriodMonths: 12,
            BillingFrequency: 6,
            SchoolSlotLimit: 4),
        new(
            Slug: "skool-medium-jaarliks",
            Name: "Skool Medium",
            TierCode: "school_medium_yearly",
            ItemName: "Schink Stories Skool Medium",
            ItemDescription: "Jaarlikse skooltoegang vir 6 klaskamers.",
            Amount: 8640.00m,
            IsSubscription: false,
            BillingPeriodMonths: 12,
            BillingFrequency: 6,
            SchoolSlotLimit: 6),
        new(
            Slug: "skool-groot-jaarliks",
            Name: "Skool Groot",
            TierCode: "school_large_yearly",
            ItemName: "Schink Stories Skool Groot",
            ItemDescription: "Jaarlikse skooltoegang vir 8 klaskamers.",
            Amount: 11520.00m,
            IsSubscription: false,
            BillingPeriodMonths: 12,
            BillingFrequency: 6,
            SchoolSlotLimit: 8)
    ];

    public static IReadOnlyList<PaymentPlan> SchoolPlans { get; } =
        All.Where(plan => plan.IsSchoolPlan).ToArray();

    public static PaymentPlan? FindBySlug(string? slug) =>
        All.FirstOrDefault(plan => string.Equals(plan.Slug, slug, StringComparison.OrdinalIgnoreCase));

    public static PaymentPlan? FindByTierCode(string? tierCode) =>
        All.FirstOrDefault(plan => string.Equals(plan.TierCode, tierCode, StringComparison.OrdinalIgnoreCase));
}
