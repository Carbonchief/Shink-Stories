using Microsoft.AspNetCore.WebUtilities;
using Shink.Components.Content;

namespace Shink.Services;

public sealed record ProfileAvatar(string Id, string Name)
{
    public string ImageUrl => $"/branding/profile-avatars/{Id}.png";
}

public static class ProfileAvatarCatalog
{
    public const string PagePath = "/profielprentjie";

    public static IReadOnlyList<ProfileAvatar> All { get; } = Array.AsReadOnly(
        Shink.Shared.ProfileAvatarOptions.All.Select(avatar => new ProfileAvatar(avatar.Id, avatar.Name)).ToArray());

    public static ProfileAvatar? Find(string? id) =>
        All.FirstOrDefault(avatar => string.Equals(avatar.Id, id, StringComparison.Ordinal));

    public static bool IsSelection(string? imageUrl) => All.Any(avatar => avatar.ImageUrl == imageUrl);

    // This value is used by a form POST and must remain a local navigation target.
    public static string SafeReturnPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "/luister";
        }

        var candidate = path.Trim();
        var decoded = candidate;
        for (var attempt = 0; attempt < 4; attempt++)
        {
            decoded = Uri.UnescapeDataString(decoded);
            if (!decoded.StartsWith('/') || decoded.StartsWith("//", StringComparison.Ordinal) ||
                decoded.Contains('\\') || decoded.Any(char.IsControl))
            {
                return "/luister";
            }
        }

        var pathOnly = decoded.Split('?', '#')[0];
        if (pathOnly.StartsWith(PagePath, StringComparison.OrdinalIgnoreCase) ||
            pathOnly.StartsWith("/auth", StringComparison.OrdinalIgnoreCase) ||
            pathOnly.StartsWith("/api", StringComparison.OrdinalIgnoreCase) ||
            pathOnly.StartsWith("/teken-", StringComparison.OrdinalIgnoreCase) ||
            pathOnly.StartsWith("/herstel-wagwoord", StringComparison.OrdinalIgnoreCase))
        {
            return "/luister";
        }

        return candidate;
    }

    public static string SelectionPath(string? returnPath, bool isEditing = false) =>
        QueryHelpers.AddQueryString(PagePath, new Dictionary<string, string?>
        {
            ["returnUrl"] = SafeReturnPath(returnPath),
            ["mode"] = isEditing ? "edit" : null
        });

    public static string SignupPath(PaymentPlan? plan, string? returnPath, string? discountCode)
    {
        var picker = SelectionPath(ContinueAfterSelection(returnPath, isEditing: false));
        return plan is null ? picker : QueryHelpers.AddQueryString($"/betaal/{plan.Slug}", new Dictionary<string, string?>
        {
            ["provider"] = "paystack",
            ["discountCode"] = discountCode,
            ["returnUrl"] = picker
        });
    }

    public static string AfterFirstSignIn(string target) =>
        SignupCheckoutPath(target) ?? SelectionPath(OnboardingReturnPath(target) ?? target);

    public static string? SignupCheckoutPath(string? target)
    {
        var safePath = SafeReturnPath(target);
        var parts = safePath.Split('?', 2);
        const string checkoutPrefix = "/betaal/";
        if (!parts[0].StartsWith(checkoutPrefix, StringComparison.OrdinalIgnoreCase) ||
            PaymentPlanCatalog.FindBySlug(parts[0][checkoutPrefix.Length..]) is not { IsAdminOnly: false })
        {
            return null;
        }

        var query = QueryHelpers.ParseQuery(parts.ElementAtOrDefault(1));
        var returnUrl = query.GetValueOrDefault("returnUrl").ToString();
        query["returnUrl"] = SelectionPath(OnboardingReturnPath(returnUrl) ?? returnUrl);
        return QueryHelpers.AddQueryString(parts[0], query);
    }

    public static string? OnboardingReturnPath(string? path) =>
        path?.StartsWith(PagePath + "?", StringComparison.Ordinal) == true
            ? SafeReturnPath(QueryHelpers.ParseQuery(path[PagePath.Length..]).GetValueOrDefault("returnUrl"))
            : null;

    public static string ContinueAfterSelection(string? returnPath, bool isEditing)
    {
        var safePath = SafeReturnPath(returnPath);
        return isEditing || safePath == "/skool-admin" || StoryAccessPolicy.TryParseStoryPath(safePath, out _, out _)
            ? safePath
            : "/luister";
    }
}
