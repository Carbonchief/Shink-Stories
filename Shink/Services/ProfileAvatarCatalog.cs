using Microsoft.AspNetCore.WebUtilities;

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
}
