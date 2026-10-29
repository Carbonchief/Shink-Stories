namespace Shink.Shared;

public sealed record ProfileAvatarOption(string Id, string Name)
{
    public string ImageUrl => $"/branding/profile-avatars/{Id}.png";
    public string MobileImage => $"profile_avatar_{Id}.png";
}

public static class ProfileAvatarOptions
{
    public static IReadOnlyList<ProfileAvatarOption> All { get; } = Array.AsReadOnly<ProfileAvatarOption>(
    [
        new("suurlemoentjie", "Suurlemoentjie"), new("tiekie", "Tiekie"),
        new("roomys", "Roomys"), new("oortjies", "Oortjies"),
        new("muis", "Muis"), new("musiekmaat", "Musiekmaat"),
        new("panda", "Panda"), new("lama", "Lama"),
        new("rammetjie", "Rammetjie"), new("hasie", "Hasie"),
        new("skilpad", "Skilpad"), new("sonnetjie", "Sonnetjie"),
        new("valkparkiet", "Valkparkiet"), new("trekker", "Trekker"),
        new("prinses", "Prinses"), new("leeutjie", "Leeutjie")
    ]);

    public static string? IdFromImageUrl(string? imageUrl)
    {
        var path = Uri.TryCreate(imageUrl, UriKind.Absolute, out var uri)
            ? uri.AbsolutePath
            : imageUrl?.Split('?', '#')[0];
        return All.FirstOrDefault(avatar => string.Equals(avatar.ImageUrl, path, StringComparison.Ordinal))?.Id;
    }
}
