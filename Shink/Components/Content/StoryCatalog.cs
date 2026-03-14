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

public sealed record StoryPreviewItem(
    string Title,
    string CoverPath,
    string LinkPath = "/opsies");

public static class StoryCatalog
{
    public static IReadOnlyList<StoryItem> All { get; } =
    [
        new(
            Slug: "suurlemoentjie",
            Title: "Suurlemoentjie",
            Description: "Wanneer suur woorde sag word, groei ware vriendskap. 🍋",
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

    public static IReadOnlyList<StoryPreviewItem> NewestTop10 { get; } =
    [
        new(
            Title: "Dankie & Wilnie Deelnie",
            CoverPath: "/stories/imported/2026/03/Schink_Stories_website_Cover_Dankie_en_Wilnie_Deelnie-600x775.jpg"),
        new(
            Title: "Rudi Renoster speel rof",
            CoverPath: "/stories/imported/2026/02/Schink_Stories_website_Cover_Rudi_Renoster_deur_Martin_Schwella-600x775.jpg"),
        new(
            Title: "Josef die dromer",
            CoverPath: "/stories/imported/2026/02/13_Josef_die_Dromer_Schink-Stories-600x775.jpg"),
        new(
            Title: "Die Fluistervarke en die hoed wat groet",
            CoverPath: "/stories/imported/2026/02/Schink_Stories_website_Die_Fluistervarke_Die_Hoed_wat_groet_deur_Martin_Schwella-600x775.jpg"),
        new(
            Title: "Dankie en die Huil-oor-alles",
            CoverPath: "/stories/imported/2026/01/Schink_Stories_website_Dankie_en_die_huil_oor_alles_Schink-Stories-600x775.jpg"),
        new(
            Title: "Slappie en Sloep",
            CoverPath: "/stories/imported/2026/01/Storie_Hoekie_Slappie_en_Sloep_Schink-600x775.jpg"),
        new(
            Title: "Maatjie die Akker-saadjie",
            CoverPath: "/stories/imported/2026/01/Storie_Hoekie_Maatjie_Die_Akker-saadjie-600x775.jpg"),
        new(
            Title: "Jan die Brandweerman",
            CoverPath: "/stories/imported/2025/11/Storie_Hoekie_Jan_die_Brandweerman-600x775.jpg"),
        new(
            Title: "Georgie se Radio",
            CoverPath: "/stories/imported/2025/11/Schink_Nuwe_Stories_Georgie_se_Radio-600x600.png"),
        new(
            Title: "Fantjie Leer Skryf",
            CoverPath: "/stories/imported/2025/10/Storie_Hoekie_Fantjie_Leer_Skryf-600x775.jpg")
    ];

    public static StoryItem? FindBySlug(string? slug) =>
        All.FirstOrDefault(story => string.Equals(story.Slug, slug, StringComparison.OrdinalIgnoreCase));
}
