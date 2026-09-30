using Shink.Components.Content;

namespace Shink.Services;

public static class LandingPageButtonLinks
{
    public static IReadOnlyList<PaymentPlan> SubscriptionOptions { get; } =
        PaymentPlanCatalog.All.Where(plan => plan.IsSubscription && !plan.IsAdminOnly).ToArray();

    public static string BuildSubscriptionUrl(string slug)
    {
        var plan = SubscriptionOptions.FirstOrDefault(option => option.Slug == slug)
            ?? throw new ArgumentException("Choose a public subscription option.", nameof(slug));
        return $"/teken-op?plan={Uri.EscapeDataString(plan.Slug)}&returnUrl={Uri.EscapeDataString($"/betaal/{plan.Slug}")}";
    }

    public static string? GetSubscriptionSlug(string? url) => SubscriptionOptions
        .FirstOrDefault(plan => string.Equals(url, BuildSubscriptionUrl(plan.Slug), StringComparison.Ordinal) ||
            string.Equals(url, $"/opsies?plan={plan.Slug}", StringComparison.Ordinal))?.Slug;
}
