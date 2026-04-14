using System.Net;
using System.Text.RegularExpressions;
using Ganss.Xss;
using Markdig;

namespace Shink.Services;

public sealed partial class BlogContentRenderer : IBlogContentRenderer
{
    private static readonly MarkdownPipeline MarkdownPipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    private readonly HtmlSanitizer _sanitizer = BuildSanitizer();

    public string RenderHtml(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return string.Empty;
        }

        var rendered = Markdown.ToHtml(NormalizeMarkdown(markdown), MarkdownPipeline);
        return _sanitizer.Sanitize(rendered);
    }

    public string ConvertToPlainText(string? markdown)
    {
        var sanitizedHtml = RenderHtml(markdown);
        if (string.IsNullOrWhiteSpace(sanitizedHtml))
        {
            return string.Empty;
        }

        var withoutTags = HtmlTagRegex().Replace(sanitizedHtml, " ");
        var decoded = WebUtility.HtmlDecode(withoutTags);
        return CollapseWhitespace(decoded);
    }

    public string BuildExcerpt(string? markdown, int maxLength = 220)
    {
        var plainText = ConvertToPlainText(markdown);
        if (string.IsNullOrWhiteSpace(plainText))
        {
            return string.Empty;
        }

        if (plainText.Length <= maxLength)
        {
            return plainText;
        }

        var candidate = plainText[..Math.Clamp(maxLength, 40, plainText.Length)].TrimEnd();
        var lastSpace = candidate.LastIndexOf(' ');
        if (lastSpace >= 80)
        {
            candidate = candidate[..lastSpace];
        }

        return $"{candidate.TrimEnd('.', ',', ';', ':', ' ')}...";
    }

    public int EstimateReadingTimeMinutes(string? markdown)
    {
        var plainText = ConvertToPlainText(markdown);
        if (string.IsNullOrWhiteSpace(plainText))
        {
            return 1;
        }

        var words = WordRegex().Matches(plainText).Count;
        return Math.Max(1, (int)Math.Ceiling(words / 220d));
    }

    private static HtmlSanitizer BuildSanitizer()
    {
        var sanitizer = new HtmlSanitizer();
        sanitizer.AllowedSchemes.Clear();
        sanitizer.AllowedSchemes.Add("http");
        sanitizer.AllowedSchemes.Add("https");
        sanitizer.AllowedSchemes.Add("mailto");
        sanitizer.AllowedSchemes.Add("tel");

        foreach (var tag in new[] { "figure", "figcaption" })
        {
            sanitizer.AllowedTags.Add(tag);
        }

        return sanitizer;
    }

    private static string NormalizeMarkdown(string markdown) =>
        markdown
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Trim();

    private static string CollapseWhitespace(string value) =>
        WhitespaceRegex().Replace(value, " ").Trim();

    [GeneratedRegex("<[^>]+>", RegexOptions.CultureInvariant)]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"[\p{L}\p{N}']+", RegexOptions.CultureInvariant)]
    private static partial Regex WordRegex();
}
