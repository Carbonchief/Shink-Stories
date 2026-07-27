using System.Net;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using Ganss.Xss;
using Markdig;
using Microsoft.Extensions.Options;
using Shink.Utilities;

namespace Shink.Services;

public sealed partial class BlogContentRenderer : IBlogContentRenderer
{
    private static readonly MarkdownPipeline MarkdownPipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    private readonly HtmlSanitizer _sanitizer;

    public BlogContentRenderer(IOptions<CloudflareR2Options> cloudflareR2Options)
    {
        _sanitizer = BuildSanitizer(cloudflareR2Options.Value.PublicBaseUrl);
    }

    public BlogContentRenderer()
        : this(Options.Create(new CloudflareR2Options()))
    {
    }

    public string RenderHtml(string? markdown)
    {
        var normalizedContent = NormalizeContent(markdown);
        if (string.IsNullOrWhiteSpace(normalizedContent))
        {
            return string.Empty;
        }

        var rendered = LooksLikeHtml(normalizedContent)
            ? normalizedContent
            : Markdown.ToHtml(normalizedContent, MarkdownPipeline);

        return NormalizeRenderedHtmlWhitespace(_sanitizer.Sanitize(rendered));
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

    private static HtmlSanitizer BuildSanitizer(string? cloudflarePublicBaseUrl)
    {
        var sanitizer = new HtmlSanitizer();
        sanitizer.AllowedSchemes.Clear();
        sanitizer.AllowedSchemes.Add("http");
        sanitizer.AllowedSchemes.Add("https");
        sanitizer.AllowedSchemes.Add("mailto");
        sanitizer.AllowedSchemes.Add("tel");

        foreach (var tag in new[] { "figure", "figcaption", "iframe", "video", "u", "s" })
        {
            sanitizer.AllowedTags.Add(tag);
        }

        foreach (var attribute in new[]
                 {
                     "allow",
                     "allowfullscreen",
                     "class",
                     "controls",
                     "decoding",
                     "frameborder",
                     "loading",
                     "playsinline",
                     "preload",
                     "referrerpolicy"
                 })
        {
            sanitizer.AllowedAttributes.Add(attribute);
        }

        foreach (var className in new[]
                 {
                     "blog-media-cloudflare",
                     "blog-media-image",
                     "blog-media-video",
                     "blog-media-youtube"
                 })
        {
            sanitizer.AllowedClasses.Add(className);
        }

        sanitizer.FilterUrl += (_, args) =>
        {
            if (string.Equals(args.Tag.LocalName, "iframe", StringComparison.OrdinalIgnoreCase))
            {
                var youtubeEmbedUrl = YouTubeUrlHelper.BuildEmbedUrl(args.OriginalUrl);
                if (!string.IsNullOrWhiteSpace(youtubeEmbedUrl))
                {
                    args.SanitizedUrl = youtubeEmbedUrl;
                    return;
                }

                var cloudflareEmbed = BlogVideoUrlHelper.ResolveCloudflareVideo(
                    args.OriginalUrl,
                    cloudflarePublicBaseUrl);
                args.SanitizedUrl = cloudflareEmbed?.Kind == BlogVideoEmbedKind.Iframe
                    ? cloudflareEmbed.Url
                    : null;
                return;
            }

            if (string.Equals(args.Tag.LocalName, "video", StringComparison.OrdinalIgnoreCase))
            {
                var cloudflareVideo = BlogVideoUrlHelper.ResolveCloudflareVideo(
                    args.OriginalUrl,
                    cloudflarePublicBaseUrl);
                args.SanitizedUrl = cloudflareVideo?.Kind == BlogVideoEmbedKind.DirectVideo
                    ? cloudflareVideo.Url
                    : null;
            }
        };

        sanitizer.PostProcessDom += (_, args) =>
        {
            foreach (var mediaElement in args.Document.QuerySelectorAll("iframe, video").ToArray())
            {
                if (!mediaElement.HasAttribute("src"))
                {
                    mediaElement.Remove();
                }
            }
        };

        return sanitizer;
    }

    private static bool LooksLikeHtml(string content) =>
        HtmlContentRegex().IsMatch(content);

    private static string NormalizeContent(string? content) =>
        content?
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Trim()
        ?? string.Empty;

    private static string NormalizeRenderedHtmlWhitespace(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        return html
            .Replace("&nbsp;", " ", StringComparison.OrdinalIgnoreCase)
            .Replace("&#160;", " ", StringComparison.OrdinalIgnoreCase)
            .Replace("&#xA0;", " ", StringComparison.OrdinalIgnoreCase)
            .Replace('\u00A0', ' ');
    }

    private static string CollapseWhitespace(string value) =>
        WhitespaceRegex().Replace(value, " ").Trim();

    [GeneratedRegex(@"<\s*(p|div|h[1-6]|ul|ol|li|blockquote|pre|code|strong|em|u|s|a|br|figure|figcaption|img|iframe|video)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HtmlContentRegex();

    [GeneratedRegex("<[^>]+>", RegexOptions.CultureInvariant)]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"[\p{L}\p{N}']+", RegexOptions.CultureInvariant)]
    private static partial Regex WordRegex();
}
