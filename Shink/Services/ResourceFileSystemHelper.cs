using System.Globalization;
using System.Text.RegularExpressions;

namespace Shink.Services;

internal static partial class ResourceFileSystemHelper
{
    public static string? ResolveSourceDirectory(string? configuredDirectory, string contentRootPath, string webRootPath)
    {
        if (string.IsNullOrWhiteSpace(configuredDirectory))
        {
            return null;
        }

        var normalized = configuredDirectory.Trim().Replace('\\', '/');

        if (Path.IsPathRooted(normalized))
        {
            return Path.GetFullPath(normalized);
        }

        if (normalized.StartsWith("~/", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }

        if (normalized.StartsWith("wwwroot/", StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetFullPath(Path.Combine(contentRootPath, normalized.Replace('/', Path.DirectorySeparatorChar)));
        }

        if (normalized.StartsWith("resources/", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("/resources/", StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetFullPath(Path.Combine(
                webRootPath,
                normalized.TrimStart('/').Replace('/', Path.DirectorySeparatorChar)));
        }

        return Path.GetFullPath(Path.Combine(contentRootPath, normalized.Replace('/', Path.DirectorySeparatorChar)));
    }

    public static IReadOnlyList<ResourceDocumentFile> GetPdfFiles(
        string? configuredDirectory,
        string contentRootPath,
        string webRootPath)
    {
        var resolvedDirectory = ResolveSourceDirectory(configuredDirectory, contentRootPath, webRootPath);
        if (string.IsNullOrWhiteSpace(resolvedDirectory) || !Directory.Exists(resolvedDirectory))
        {
            return Array.Empty<ResourceDocumentFile>();
        }

        return Directory.EnumerateFiles(resolvedDirectory, "*.pdf", SearchOption.TopDirectoryOnly)
            .Select(path => new FileInfo(path))
            .Where(file => file.Exists)
            .Select(file => new ResourceDocumentFile(
                FileName: file.Name,
                Title: BuildDocumentTitle(Path.GetFileNameWithoutExtension(file.Name)),
                FullPath: file.FullName,
                SizeBytes: file.Length,
                LastModified: new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero),
                SequenceNumber: TryExtractSequenceNumber(file.Name)))
            .OrderBy(file => file.SequenceNumber ?? int.MaxValue)
            .ThenBy(file => file.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public static ResourceDocumentFile? FindPdfFile(
        string? configuredDirectory,
        string requestedFileName,
        string contentRootPath,
        string webRootPath)
    {
        var files = GetPdfFiles(configuredDirectory, contentRootPath, webRootPath);
        return files.FirstOrDefault(file =>
            string.Equals(file.FileName, requestedFileName, StringComparison.OrdinalIgnoreCase));
    }

    public static string BuildDocumentTitle(string? fileNameWithoutExtension)
    {
        if (string.IsNullOrWhiteSpace(fileNameWithoutExtension))
        {
            return "PDF";
        }

        var title = fileNameWithoutExtension
            .Replace('_', ' ')
            .Trim();

        title = Regex.Replace(title, @"\s-\s*", " - ", RegexOptions.CultureInvariant);
        title = Regex.Replace(title, @"\s+-\s+", " - ", RegexOptions.CultureInvariant);
        title = Regex.Replace(title, @"\s{2,}", " ", RegexOptions.CultureInvariant);
        title = BrandPrefixRegex().Replace(title, string.Empty).Trim();

        return string.IsNullOrWhiteSpace(title) ? fileNameWithoutExtension.Trim() : title;
    }

    private static int? TryExtractSequenceNumber(string value)
    {
        var match = SequenceNumberRegex().Match(value);
        if (!match.Success)
        {
            return null;
        }

        return int.TryParse(match.Groups["number"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            ? number
            : null;
    }

    [GeneratedRegex(@"^(?:schink(?:\s+stories)?[\s_-]*)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BrandPrefixRegex();

    [GeneratedRegex(@"(?<number>\d{1,4})", RegexOptions.CultureInvariant)]
    private static partial Regex SequenceNumberRegex();
}

internal sealed record ResourceDocumentFile(
    string FileName,
    string Title,
    string FullPath,
    long SizeBytes,
    DateTimeOffset LastModified,
    int? SequenceNumber);
