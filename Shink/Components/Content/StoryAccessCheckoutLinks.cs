using Microsoft.AspNetCore.WebUtilities;

namespace Shink.Components.Content;

public static class StoryAccessCheckoutLinks
{
    public const string PagePath = "/kry-toegang";

    public static string BuildPageHref(string? returnUrl)
    {
        var safeReturnUrl = GetSafeStoryReturnUrl(returnUrl);
        return safeReturnUrl is null
            ? PagePath
            : QueryHelpers.AddQueryString(PagePath, "returnUrl", safeReturnUrl);
    }

    public static string BuildCheckoutHref(PaymentPlan plan, bool isAuthenticated, string? returnUrl)
    {
        var query = new Dictionary<string, string?> { ["provider"] = "paystack" };
        var safeReturnUrl = GetSafeStoryReturnUrl(returnUrl);
        if (safeReturnUrl is not null)
        {
            query["returnUrl"] = safeReturnUrl;
        }

        var checkoutPath = QueryHelpers.AddQueryString($"/betaal/{Uri.EscapeDataString(plan.Slug)}", query);
        return isAuthenticated
            ? checkoutPath
            : QueryHelpers.AddQueryString("/teken-op", new Dictionary<string, string?>
            {
                ["plan"] = plan.Slug,
                ["returnUrl"] = checkoutPath
            });
    }

    private static string? GetSafeStoryReturnUrl(string? returnUrl)
    {
        var candidate = returnUrl?.Trim();
        return !string.IsNullOrWhiteSpace(candidate) &&
               candidate.StartsWith("/", StringComparison.Ordinal) &&
               !candidate.StartsWith("//", StringComparison.Ordinal) &&
               !candidate.Contains('\\') &&
               !candidate.Any(char.IsControl) &&
               StoryAccessPolicy.TryParseStoryPath(candidate, out _, out _)
            ? candidate
            : null;
    }
}
