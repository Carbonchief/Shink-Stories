namespace Shink.Components.Content;

public sealed record StoryPlaylist(
    string Slug,
    string Title,
    string? Description,
    int SortOrder,
    IReadOnlyList<StoryItem> Stories,
    bool ShowOnHome = false,
    string? LogoImagePath = null,
    string? BackdropImagePath = null)
{
    public const string DefaultLogoImagePath = "/branding/Storie_Hoekie_Logo_Banner.png";
    public const string DefaultBackdropImagePath = "/branding/Storie_Hoekie_Logo_Banner_Backdrop.png";

    public string ResolvedLogoImagePath => ResolveImagePath(LogoImagePath, DefaultLogoImagePath);

    public string ResolvedBackdropImagePath => ResolveImagePath(BackdropImagePath, DefaultBackdropImagePath);

    private static string ResolveImagePath(string? candidate, string fallback) =>
        string.IsNullOrWhiteSpace(candidate) ? fallback : candidate.Trim();
}
