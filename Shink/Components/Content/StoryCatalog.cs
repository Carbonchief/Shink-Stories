namespace Shink.Components.Content;

public sealed record StoryItem(
    string Slug,
    string Title,
    string Description,
    string ImageFileName,
    string AudioFileName)
{
    public string ImagePath => ToAssetPath(ImageFileName);
    public string ThumbnailPath => ToAssetPath($"thumbs/{ImageFileName}");

    private static string ToAssetPath(string fileName)
    {
        var segments = fileName
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.EscapeDataString);

        return $"/stories/{string.Join('/', segments)}";
    }
}

public static class StoryCatalog
{
    public static IReadOnlyList<StoryItem> All { get; } =
    [
        new(
            Slug: "suurlemoentjie",
            Title: "Suurlemoentjie",
            Description: "Swem saam met Suurlemoentjie op 'n prettige avontuur onder die water.",
            ImageFileName: "Suurlemoentjie.jpeg",
            AudioFileName: "Suurlemoentjie.mpeg"),
        new(
            Slug: "die-kwaaibok-se-klip",
            Title: "Die Kwaaibok se Klip",
            Description: "Die kwaaibok se dag verander wanneer 'n klip en 'n groot les sy pad kruis.",
            ImageFileName: "Die Kwaaibok se Klip.jpeg",
            AudioFileName: "Die Kwaaibok se Klip.mpeg"),
        new(
            Slug: "seekoei-sluit-sy-mond-toe",
            Title: "Seekoei Sluit sy mond toe",
            Description: "Seekoei leer op 'n snaakse manier wanneer om te praat en wanneer om stil te bly.",
            ImageFileName: "Seekoei Sluit sy mond toe.jpeg",
            AudioFileName: "Seekoei Sluit sy mond toe.mpeg")
    ];

    public static StoryItem? FindBySlug(string? slug) =>
        All.FirstOrDefault(story => string.Equals(story.Slug, slug, StringComparison.OrdinalIgnoreCase));
}
