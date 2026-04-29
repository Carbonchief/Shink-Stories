using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Shink.Components.Content;

public sealed record StoryItem(
    string Slug,
    string Title,
    string Description,
    string ImageFileName,
    string AudioFileName,
    string? ThumbnailFileName = null,
    string AudioProvider = "local",
    string? AudioBucket = null,
    string? AudioContentType = null,
    string AccessLevel = "subscriber",
    string? Summary = null,
    IReadOnlyList<string>? Lessons = null,
    IReadOnlyList<string>? ValueTags = null,
    IReadOnlyList<string>? ConversationQuestions = null,
    IReadOnlyList<string>? Characters = null,
    string? YouTubeUrl = null,
    decimal? DurationSeconds = null)
{
    public string ImagePath => ResolveAssetPath(ImageFileName);
    public string ThumbnailPath => string.IsNullOrWhiteSpace(ThumbnailFileName)
        ? ResolveAssetPath($"thumbs/{ImageFileName}")
        : ResolveAssetPath(ThumbnailFileName);

    private static string ResolveAssetPath(string fileName)
    {
        if (fileName.StartsWith("/", StringComparison.Ordinal))
        {
            return EncodeLocalPath(fileName);
        }

        if (Uri.TryCreate(fileName, UriKind.Absolute, out var absoluteUri) &&
            (string.Equals(absoluteUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(absoluteUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            return RewriteAbsoluteAssetUri(absoluteUri);
        }

        return ToAssetPath(fileName);
    }

    public static string RewriteImagePathForBrowser(string imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            return imagePath;
        }

        var normalized = imagePath.Trim();
        if (normalized.StartsWith("/", StringComparison.Ordinal))
        {
            return EncodeLocalPath(normalized);
        }

        if (Uri.TryCreate(normalized, UriKind.Absolute, out var absoluteUri) &&
            (string.Equals(absoluteUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(absoluteUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            return RewriteAbsoluteAssetUri(absoluteUri);
        }

        return normalized;
    }

    private static string RewriteAbsoluteAssetUri(Uri absoluteUri)
    {
        if (string.Equals(absoluteUri.Host, "media.prioritybit.co.za", StringComparison.OrdinalIgnoreCase))
        {
            return $"/media/image?src={Uri.EscapeDataString(absoluteUri.ToString())}";
        }

        return absoluteUri.ToString();
    }

    private static string EncodeLocalPath(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var normalized = value.Replace('\\', '/');
        var suffixStart = normalized.IndexOfAny(['?', '#']);
        var pathPart = suffixStart >= 0 ? normalized[..suffixStart] : normalized;
        var suffixPart = suffixStart >= 0 ? normalized[suffixStart..] : string.Empty;

        var encodedPath = "/" + string.Join(
            '/',
            pathPart
                .Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Select(Uri.EscapeDataString));

        return encodedPath + suffixPart;
    }

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
    private static readonly string[] SupportedAudioExtensions =
    [".mpeg", ".mp3", ".m4a", ".wav", ".ogg"];

    private static readonly string[] SupportedImageExtensions =
    [".jpg", ".jpeg", ".png", ".webp"];

    private static readonly StringComparer SlugComparer = StringComparer.OrdinalIgnoreCase;

    private static readonly HashSet<string> MatchingStopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "afrikaanse", "cover", "deur", "die", "en", "for", "kinders", "kinderstories",
        "kom", "luister", "martin", "met", "n", "op", "schink", "schwella", "se", "stories",
        "story", "storie", "storiehoekie", "the", "van", "vir", "website"
    };

    private static readonly HashSet<string> NonStoryImageTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "afsluiting", "banner", "catagory", "category", "geskenke", "grouping", "home",
        "join", "logo", "menu", "missie", "news", "reeks", "reekse", "slider", "uitdaging",
        "wallpaper"
    };

    private sealed record ImportedImageRoot(
        string PhysicalPath,
        string AssetPrefix);

    private sealed record ImportedImageCandidate(
        string AssetPath,
        string YearMonthKey,
        string CanonicalName,
        HashSet<string> Tokens,
        int? StoryNumber,
        int VariantQuality,
        bool IsLikelyStoryCover);

    public static IReadOnlyList<StoryItem> All { get; } =
    [
        new(
            Slug: "suurlemoentjie",
            Title: "Suurlemoentjie",
            Description: "Wanneer suur woorde sag word, groei ware vriendskap.",
            ImageFileName: "Suurlemoentjie.jpeg",
            AudioFileName: "Suurlemoentjie.mpeg",
            ThumbnailFileName: "Suurlemoentjie.jpeg"),
        new(
            Slug: "die-kwaaibok-se-klip",
            Title: "Die Kwaaibok se Klip",
            Description: "Die kwaaibok se dag verander wanneer 'n klip en 'n groot les sy pad kruis.",
            ImageFileName: "Die Kwaaibok se Klip.jpeg",
            AudioFileName: "Die Kwaaibok se Klip.mpeg",
            ThumbnailFileName: "Die Kwaaibok se Klip.jpeg"),
        new(
            Slug: "seekoei-sluit-sy-mond-toe",
            Title: "Seekoei Sluit sy mond toe",
            Description: "Seekoei leer op 'n snaakse manier wanneer om te praat en wanneer om stil te bly.",
            ImageFileName: "Seekoei Sluit sy mond toe.jpeg",
            AudioFileName: "Seekoei Sluit sy mond toe.mpeg",
            ThumbnailFileName: "Seekoei Sluit sy mond toe.jpeg")
    ];

    private static IReadOnlyList<StoryItem> NewestTop10Stories { get; } =
    [
        new(
            Slug: "dankie-en-wilnie-deelnie",
            Title: "Dankie & Wilnie Deelnie",
            Description: "Luister na Dankie en Wilnie se nuutste avontuur op Schink Stories.",
            ImageFileName: "Schink_Stories_website_Cover_Dankie_en_Wilnie_Deelnie-600x775.jpg",
            AudioFileName: "imported/stories/2026/03/Storie_Hoekie_04_04_Dankie_en_Wilnie_Deelnie.mp3",
            ThumbnailFileName: "Schink_Stories_website_Cover_Dankie_en_Wilnie_Deelnie-600x775.jpg"),
        new(
            Slug: "rudi-renoster-speel-rof",
            Title: "Rudi Renoster speel rof",
            Description: "Rudi leer hoe om sterk te wees sonder om ander seer te maak.",
            ImageFileName: "Schink_Stories_website_Cover_Rudi_Renoster_deur_Martin_Schwella-600x775.jpg",
            AudioFileName: "imported/stories/2026/02/Storie_Hoekie_04_03_Rudi_Renoster_Speel_Rof.mp3",
            ThumbnailFileName: "Schink_Stories_website_Cover_Rudi_Renoster_deur_Martin_Schwella-600x775.jpg"),
        new(
            Slug: "josef-die-dromer",
            Title: "Josef die dromer",
            Description: "Ontdek Josef se geloofsverhaal in hierdie Bybelstorie vir kinders.",
            ImageFileName: "13_Josef_die_Dromer_Schink-Stories-600x775.jpg",
            AudioFileName: "imported/stories/2026/02/Schink-_Bybel_Stories_13_Josef_die_Dromer.mp3",
            ThumbnailFileName: "13_Josef_die_Dromer_Schink-Stories-600x775.jpg"),
        new(
            Slug: "die-fluistervarke-en-die-hoed-wat-groet",
            Title: "Die Fluistervarke en die hoed wat groet",
            Description: "Fluistervarke wys hoe klein keuses groot verskil kan maak.",
            ImageFileName: "Schink_Stories_website_Die_Fluistervarke_Die_Hoed_wat_groet_deur_Martin_Schwella-600x775.jpg",
            AudioFileName: "imported/stories/2026/02/Storie_Hoekie_04_02_Die_Fluistervarke_Die_Hoed_wat_groet.mp3",
            ThumbnailFileName: "Schink_Stories_website_Die_Fluistervarke_Die_Hoed_wat_groet_deur_Martin_Schwella-600x775.jpg"),
        new(
            Slug: "dankie-en-die-huil-oor-alles",
            Title: "Dankie en die Huil-oor-alles",
            Description: "Dankie ontdek nuwe moed wanneer emosies oorborrel.",
            ImageFileName: "Schink_Stories_website_Dankie_en_die_huil_oor_alles_Schink-Stories-600x775.jpg",
            AudioFileName: "imported/stories/2026/01/Storie_Hoekie_04_01_Dankie_en_die_Huil_oor_alles.mp3",
            ThumbnailFileName: "Schink_Stories_website_Dankie_en_die_huil_oor_alles_Schink-Stories-600x775.jpg"),
        new(
            Slug: "slappie-en-sloep",
            Title: "Slappie en Sloep",
            Description: "Kom luister na Slappie en Sloep se prettige storie vol karakterlesse.",
            ImageFileName: "Storie_Hoekie_Slappie_en_Sloep_Schink-600x775.jpg",
            AudioFileName: "imported/stories/2026/01/Schink-_Stories_28_Slappie_En_Sloep.mp3",
            ThumbnailFileName: "Storie_Hoekie_Slappie_en_Sloep_Schink-600x775.jpg"),
        new(
            Slug: "maatjie-die-akker-saadjie",
            Title: "Maatjie die Akker-saadjie",
            Description: "Maatjie se reis wys dat groei tyd en geduld vra.",
            ImageFileName: "Storie_Hoekie_Maatjie_Die_Akker-saadjie-600x775.jpg",
            AudioFileName: "imported/stories/2026/01/Schink-_Stories_27_Maatjie_Die_Akker_saadjie.mp3",
            ThumbnailFileName: "Storie_Hoekie_Maatjie_Die_Akker-saadjie-600x775.jpg"),
        new(
            Slug: "jan-die-brandweerman",
            Title: "Jan die Brandweerman",
            Description: "Jan leer kinders oor moed, kalmte en verantwoordelikheid.",
            ImageFileName: "Storie_Hoekie_Jan_die_Brandweerman-600x775.jpg",
            AudioFileName: "imported/stories/2025/11/Schink-_Stories_26_Jan_Die_Brandweerman.mp3",
            ThumbnailFileName: "Storie_Hoekie_Jan_die_Brandweerman-600x775.jpg"),
        new(
            Slug: "georgie-se-radio",
            Title: "Georgie se Radio",
            Description: "Georgie se Radio bring pret, ritme en verrassings na storietyd.",
            ImageFileName: "Storie_06_Georgie_se_Radio.jpg",
            AudioFileName: "imported/stories/2024/05/Schink-_Stories_06_Georgie_Se_Radio.mp3",
            ThumbnailFileName: "Storie_06_Georgie_se_Radio-600x454.jpg"),
        new(
            Slug: "fantjie-leer-skryf",
            Title: "Fantjie Leer Skryf",
            Description: "Fantjie ontdek hoe oefening en aanhou uiteindelik vrugte dra.",
            ImageFileName: "Storie_Hoekie_Fantjie_Leer_Skryf-600x775.jpg",
            AudioFileName: "imported/stories/2025/10/Schink-_Stories_24_Fantjie_leer_skryf.mp3",
            ThumbnailFileName: "Storie_Hoekie_Fantjie_Leer_Skryf-600x775.jpg")
    ];

    private static IReadOnlyList<ImportedImageCandidate> ImportedImageCandidates { get; } = LoadImportedImageCandidates();

    public static IReadOnlyList<StoryItem> LuisterStories { get; } = BuildLuisterStories();

    public static IReadOnlyList<StoryPreviewItem> NewestTop10 { get; } = NewestTop10Stories
        .Select(story => new StoryPreviewItem(
            Title: story.Title,
            CoverPath: story.ImagePath,
            LinkPath: $"/luister/{Uri.EscapeDataString(story.Slug)}"))
        .ToArray();

    public static StoryItem? FindBySlug(string? slug) =>
        All.FirstOrDefault(story => string.Equals(story.Slug, slug, StringComparison.OrdinalIgnoreCase));

    public static StoryItem? FindLuisterStoryBySlug(string? slug) =>
        LuisterStories.FirstOrDefault(story => string.Equals(story.Slug, slug, StringComparison.OrdinalIgnoreCase));

    public static StoryItem? FindAnyBySlug(string? slug) =>
        FindBySlug(slug) ?? FindLuisterStoryBySlug(slug);

    private static IReadOnlyList<StoryItem> BuildLuisterStories()
    {
        var combinedStories = new List<StoryItem>(NewestTop10Stories);
        var knownSlugs = new HashSet<string>(
            NewestTop10Stories.Select(story => story.Slug)
                .Concat(All.Select(story => story.Slug)),
            SlugComparer);
        var knownAudioFiles = new HashSet<string>(
            NewestTop10Stories.Select(story => NormalizeCatalogPath(story.AudioFileName))
                .Concat(All.Select(story => NormalizeCatalogPath(story.AudioFileName))),
            StringComparer.OrdinalIgnoreCase);

        foreach (var story in DiscoverImportedStories())
        {
            var normalizedAudioFileName = NormalizeCatalogPath(story.AudioFileName);
            if (!knownSlugs.Add(story.Slug) || !knownAudioFiles.Add(normalizedAudioFileName))
            {
                continue;
            }

            combinedStories.Add(story);
        }

        return combinedStories
            .GroupBy(story => NormalizeCatalogPath(story.AudioFileName), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
    }

    private static string NormalizeCatalogPath(string value) =>
        value.Replace('\\', '/').Trim();

    private static IEnumerable<StoryItem> DiscoverImportedStories()
    {
        var importedStoriesRoot = ResolveImportedStoriesRoot();
        if (string.IsNullOrWhiteSpace(importedStoriesRoot))
        {
            return Array.Empty<StoryItem>();
        }

        var storiesRoot = Path.GetFullPath(Path.Combine(importedStoriesRoot, "..", ".."));

        var discoveredBySlug = new Dictionary<string, StoryItem>(SlugComparer);
        var audioFiles = Directory
            .EnumerateFiles(importedStoriesRoot, "*.*", SearchOption.AllDirectories)
            .Where(path => SupportedAudioExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase);

        foreach (var audioFilePath in audioFiles)
        {
            var story = TryCreateImportedStory(audioFilePath, storiesRoot);
            if (story is null || discoveredBySlug.ContainsKey(story.Slug))
            {
                continue;
            }

            discoveredBySlug[story.Slug] = story;
        }

        return discoveredBySlug.Values;
    }

    private static StoryItem? TryCreateImportedStory(string audioFilePath, string storiesRoot)
    {
        var audioStem = Path.GetFileNameWithoutExtension(audioFilePath);
        var title = NormalizeTitleFromAudioStem(audioStem);
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var slug = CreateSlug(title);
        if (string.IsNullOrWhiteSpace(slug))
        {
            return null;
        }

        var audioFileName = Path.GetRelativePath(storiesRoot, audioFilePath)
            .Replace('\\', '/');
        var coverAssetPath = ResolveImportedCoverAssetPath(audioFileName, title, audioStem);

        return new StoryItem(
            Slug: slug,
            Title: title,
            Description: $"Luister na {title} op Schink Stories.",
            ImageFileName: coverAssetPath,
            AudioFileName: audioFileName,
            ThumbnailFileName: coverAssetPath);
    }

    private static string ResolveImportedCoverAssetPath(string audioFileName, string title, string audioStem)
    {
        if (ImportedImageCandidates.Count == 0)
        {
            return NewestTop10Stories[0].ImagePath;
        }

        var yearMonth = TryExtractAudioYearMonth(audioFileName);
        var year = !string.IsNullOrWhiteSpace(yearMonth) && yearMonth.Length >= 4
            ? yearMonth[..4]
            : null;
        var titleTokens = BuildMatchingTokenSet($"{title} {audioStem}");
        var storyNumber = TryExtractStoryNumber(audioStem);

        var bestMatch = ImportedImageCandidates
            .Select(candidate =>
            {
                var sharedTokenCount = CountSharedTokens(titleTokens, candidate.Tokens);
                var hasNumberMatch = storyNumber is not null && candidate.StoryNumber == storyNumber;
                var isSameMonth = !string.IsNullOrWhiteSpace(yearMonth) &&
                                  string.Equals(candidate.YearMonthKey, yearMonth, StringComparison.Ordinal);
                var isSameYear = !isSameMonth &&
                                 !string.IsNullOrWhiteSpace(year) &&
                                 candidate.YearMonthKey.StartsWith($"{year}/", StringComparison.Ordinal);

                return new
                {
                    Candidate = candidate,
                    SharedTokenCount = sharedTokenCount,
                    HasNumberMatch = hasNumberMatch,
                    IsSameMonth = isSameMonth,
                    IsSameYear = isSameYear
                };
            })
            .Where(match => match.SharedTokenCount > 0 || (match.HasNumberMatch && (match.IsSameMonth || match.IsSameYear)))
            .Select(match =>
            {
                var score = (match.SharedTokenCount * 14) +
                            (match.HasNumberMatch ? 18 : 0) +
                            (match.SharedTokenCount >= 2 ? 8 : 0) +
                            (match.IsSameMonth ? 18 : 0) +
                            (match.IsSameYear ? 7 : 0) +
                            match.Candidate.VariantQuality +
                            (match.Candidate.IsLikelyStoryCover ? 8 : -24);

                if (match.HasNumberMatch && match.SharedTokenCount == 0)
                {
                    score -= 12;
                }

                return new
                {
                    match.Candidate,
                    Score = score
                };
            })
            .OrderByDescending(match => match.Score)
            .ThenByDescending(match => match.Candidate.VariantQuality)
            .FirstOrDefault();

        if (bestMatch is not null)
        {
            return bestMatch.Candidate.AssetPath;
        }

        var fallback = ImportedImageCandidates
            .Select(candidate => new
            {
                Candidate = candidate,
                IsSameMonth = !string.IsNullOrWhiteSpace(yearMonth) &&
                              string.Equals(candidate.YearMonthKey, yearMonth, StringComparison.Ordinal),
                IsSameYear = !string.IsNullOrWhiteSpace(year) &&
                             candidate.YearMonthKey.StartsWith($"{year}/", StringComparison.Ordinal)
            })
            .OrderByDescending(match => match.IsSameMonth)
            .ThenByDescending(match => match.IsSameYear)
            .ThenByDescending(match => match.Candidate.IsLikelyStoryCover)
            .ThenByDescending(match => match.Candidate.VariantQuality)
            .Select(match => match.Candidate)
            .FirstOrDefault();

        return fallback?.AssetPath ?? ImportedImageCandidates
            .OrderByDescending(candidate => candidate.IsLikelyStoryCover)
            .ThenByDescending(candidate => candidate.VariantQuality)
            .Select(candidate => candidate.AssetPath)
            .First();
    }

    private static IReadOnlyList<ImportedImageCandidate> LoadImportedImageCandidates()
    {
        var importedImageRoots = ResolveImportedImageRoots();
        if (importedImageRoots.Count == 0)
        {
            return Array.Empty<ImportedImageCandidate>();
        }

        var bestVariantByKey = new Dictionary<string, ImportedImageCandidate>(StringComparer.OrdinalIgnoreCase);
        foreach (var imageRoot in importedImageRoots)
        {
            var imageFiles = Directory
                .EnumerateFiles(imageRoot.PhysicalPath, "*.*", SearchOption.AllDirectories)
                .Where(path => SupportedImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase));

            foreach (var imageFilePath in imageFiles)
            {
                var relativePath = Path.GetRelativePath(imageRoot.PhysicalPath, imageFilePath)
                    .Replace('\\', '/');
                var pathSegments = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (pathSegments.Length < 3)
                {
                    continue;
                }

                if (!int.TryParse(pathSegments[0], out var year) ||
                    !int.TryParse(pathSegments[1], out var month) ||
                    year < 2000 ||
                    month is < 1 or > 12)
                {
                    continue;
                }

                var yearMonthKey = $"{pathSegments[0]}/{pathSegments[1]}";
                var imageStem = Path.GetFileNameWithoutExtension(relativePath);
                var canonicalName = GetImageCanonicalName(imageStem);
                var tokens = BuildMatchingTokenSet(canonicalName);
                if (tokens.Count == 0)
                {
                    continue;
                }

                var isLikelyStoryCover = !tokens.Overlaps(NonStoryImageTokens);
                var candidate = new ImportedImageCandidate(
                    AssetPath: ToAssetPath(imageRoot.AssetPrefix, relativePath),
                    YearMonthKey: yearMonthKey,
                    CanonicalName: canonicalName,
                    Tokens: tokens,
                    StoryNumber: TryExtractStoryNumber(canonicalName),
                    VariantQuality: GetImageVariantQuality(imageStem),
                    IsLikelyStoryCover: isLikelyStoryCover);

                var key = $"{imageRoot.AssetPrefix}|{yearMonthKey}|{canonicalName}";
                if (!bestVariantByKey.TryGetValue(key, out var existing) ||
                    candidate.VariantQuality > existing.VariantQuality)
                {
                    bestVariantByKey[key] = candidate;
                }
            }
        }

        return bestVariantByKey.Values.ToArray();
    }

    private static IReadOnlyList<ImportedImageRoot> ResolveImportedImageRoots()
    {
        var candidates = new[]
        {
            new ImportedImageRoot(
                PhysicalPath: Path.Combine(AppContext.BaseDirectory, "wwwroot", "stories", "imported"),
                AssetPrefix: "/stories/imported"),
            new ImportedImageRoot(
                PhysicalPath: Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "stories", "imported"),
                AssetPrefix: "/stories/imported"),
            new ImportedImageRoot(
                PhysicalPath: Path.Combine(Directory.GetCurrentDirectory(), "Shink", "wwwroot", "stories", "imported"),
                AssetPrefix: "/stories/imported"),
            new ImportedImageRoot(
                PhysicalPath: Path.Combine(AppContext.BaseDirectory, "wwwroot", "media", "imported", "misc"),
                AssetPrefix: "/media/imported/misc"),
            new ImportedImageRoot(
                PhysicalPath: Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "media", "imported", "misc"),
                AssetPrefix: "/media/imported/misc"),
            new ImportedImageRoot(
                PhysicalPath: Path.Combine(Directory.GetCurrentDirectory(), "Shink", "wwwroot", "media", "imported", "misc"),
                AssetPrefix: "/media/imported/misc")
        };

        return candidates
            .Select(candidate => new ImportedImageRoot(
                PhysicalPath: Path.GetFullPath(candidate.PhysicalPath),
                AssetPrefix: candidate.AssetPrefix))
            .Where(candidate => Directory.Exists(candidate.PhysicalPath))
            .GroupBy(candidate => $"{candidate.AssetPrefix}|{candidate.PhysicalPath}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
    }

    private static string? ResolveImportedStoriesRoot()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Stories", "imported", "stories"),
            Path.Combine(Directory.GetCurrentDirectory(), "Stories", "imported", "stories"),
            Path.Combine(Directory.GetCurrentDirectory(), "Shink", "Stories", "imported", "stories")
        };

        return candidates.FirstOrDefault(Directory.Exists);
    }

    private static string? TryExtractAudioYearMonth(string audioFileName)
    {
        var segments = audioFileName
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length < 4 ||
            !string.Equals(segments[0], "imported", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(segments[1], "stories", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return $"{segments[2]}/{segments[3]}";
    }

    private static HashSet<string> BuildMatchingTokenSet(string value)
    {
        var normalized = RemoveDiacritics(value).ToLowerInvariant();
        normalized = normalized.Replace('_', ' ').Replace('-', ' ');
        normalized = Regex.Replace(normalized, @"[^a-z0-9\s]", " ", RegexOptions.CultureInvariant);

        return normalized
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(token => token.Length >= 2 &&
                            !token.All(char.IsDigit) &&
                            !MatchingStopWords.Contains(token))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static string RemoveDiacritics(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);

        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(ch);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private static int CountSharedTokens(HashSet<string> first, HashSet<string> second)
    {
        if (first.Count == 0 || second.Count == 0)
        {
            return 0;
        }

        if (first.Count > second.Count)
        {
            (first, second) = (second, first);
        }

        var count = 0;
        foreach (var token in first)
        {
            if (second.Contains(token))
            {
                count++;
            }
        }

        return count;
    }

    private static int? TryExtractStoryNumber(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var match = Regex.Match(
            value,
            @"(?:stories|story|storiehoekie|storie_hoekie|storie|bybel_stories|bybel_storie|sbs|dah)[-_ ]*(\d{1,3})",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        if (!match.Success || !int.TryParse(match.Groups[1].Value, out var number))
        {
            return null;
        }

        return number;
    }

    private static string GetImageCanonicalName(string imageStem)
    {
        var canonical = Regex.Replace(imageStem, @"-\d{2,4}x\d{2,4}$", string.Empty, RegexOptions.CultureInvariant);
        canonical = Regex.Replace(canonical, @"-scaled$", string.Empty, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return canonical;
    }

    private static int GetImageVariantQuality(string imageStem)
    {
        if (TryParseVariantSize(imageStem, out var width, out var height))
        {
            if (width == 600 && height == 775)
            {
                return 140;
            }

            if (width == 600 && height == 600)
            {
                return 132;
            }

            if (width == 560 && height == 560)
            {
                return 128;
            }

            if (width >= 700 && height >= 500)
            {
                return 122;
            }

            if (width >= 500 && height >= 500)
            {
                return 118;
            }

            if (width >= 300 && height >= 200)
            {
                return 108;
            }

            return 90;
        }

        return 120;
    }

    private static bool TryParseVariantSize(string value, out int width, out int height)
    {
        var match = Regex.Match(value, @"-(\d{2,4})x(\d{2,4})$", RegexOptions.CultureInvariant);
        if (match.Success &&
            int.TryParse(match.Groups[1].Value, out width) &&
            int.TryParse(match.Groups[2].Value, out height))
        {
            return true;
        }

        width = 0;
        height = 0;
        return false;
    }

    private static string ToAssetPath(string assetPrefix, string relativePathUnderPrefix)
    {
        var segments = relativePathUnderPrefix
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.EscapeDataString);

        return $"{assetPrefix.TrimEnd('/')}/{string.Join('/', segments)}";
    }

    private static string NormalizeTitleFromAudioStem(string stem)
    {
        if (string.IsNullOrWhiteSpace(stem))
        {
            return string.Empty;
        }

        var normalized = stem.Trim();

        normalized = Regex.Replace(normalized, @"([_-]\d+)$", string.Empty, RegexOptions.CultureInvariant);
        normalized = Regex.Replace(normalized, @"^StorieHoekie-\d{1,3}-", string.Empty, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        normalized = Regex.Replace(normalized, @"^Storie_Hoekie_\d{2}_\d{2}_", string.Empty, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        normalized = Regex.Replace(normalized, @"^Schink-_Stories_\d{1,3}_", string.Empty, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        normalized = Regex.Replace(normalized, @"^Schink-_Bybel_Stories_\d{1,3}_", string.Empty, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        normalized = Regex.Replace(normalized, @"^Schink-_Bybel_Storie_", string.Empty, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        normalized = Regex.Replace(normalized, @"^DAH_\d{1,3}_", string.Empty, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        normalized = Regex.Replace(normalized, @"^DAH_", string.Empty, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        normalized = Regex.Replace(normalized, @"-Afrikaanse-kinderstories-Luister-storie(?:-\d+)?$", string.Empty, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        normalized = normalized.Replace('_', ' ').Replace('-', ' ');
        normalized = Regex.Replace(normalized, @"\s+", " ", RegexOptions.CultureInvariant).Trim();
        normalized = Regex.Replace(normalized, @"\bN\b", "'n", RegexOptions.CultureInvariant);

        return normalized;
    }

    private static string CreateSlug(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var slugBuilder = new StringBuilder();
        var previousWasDash = false;

        foreach (var ch in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            var lower = char.ToLowerInvariant(ch);
            if (char.IsLetterOrDigit(lower))
            {
                slugBuilder.Append(lower);
                previousWasDash = false;
                continue;
            }

            if (previousWasDash)
            {
                continue;
            }

            slugBuilder.Append('-');
            previousWasDash = true;
        }

        return slugBuilder.ToString().Trim('-');
    }
}
