using System.Net;
using System.Text.RegularExpressions;
using Ganss.Xss;
using Microsoft.Extensions.Options;

namespace Shink.Services;

public sealed partial class LandingPageContentValidator
{
    private const int MaximumBlocks = 100;
    private const int MaximumHtmlLength = 30_000;
    private const int MaximumTextLength = 5_000;

    private readonly string _trustedR2BaseUrl;

    public LandingPageContentValidator(
        IOptions<CloudflareR2Options> cloudflareR2Options,
        IOptions<SiteOptions> siteOptions)
    {
        _trustedR2BaseUrl = NormalizeHttpsBaseUrl(cloudflareR2Options.Value.PublicBaseUrl);
        _ = siteOptions.Value;
    }

    public LandingPageContentValidator()
        : this(Options.Create(new CloudflareR2Options()), Options.Create(new SiteOptions()))
    {
    }

    public LandingPageContent NormalizeAndValidate(
        LandingPageContent content,
        bool forPublishing = false)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (content.SchemaVersion != 1)
        {
            throw new ArgumentException("Unsupported landing page schema version.", nameof(content));
        }

        var title = NormalizeText(content.Title, 180);
        if (forPublishing && string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("A title is required before publishing.", nameof(content));
        }

        var inputBlocks = content.Blocks ?? [];
        if (inputBlocks.Count > MaximumBlocks)
        {
            throw new ArgumentException($"A landing page cannot contain more than {MaximumBlocks} blocks.", nameof(content));
        }

        var blocks = new List<LandingPageBlock>(inputBlocks.Count);
        var blockIds = new HashSet<Guid>();
        foreach (var inputBlock in inputBlocks)
        {
            if (inputBlock is null)
            {
                throw new ArgumentException("Landing page blocks cannot be null.", nameof(content));
            }

            var block = NormalizeBlock(inputBlock, forPublishing);
            if (!blockIds.Add(block.Id))
            {
                throw new ArgumentException("Landing page block IDs must be unique.", nameof(content));
            }

            blocks.Add(block);
        }

        if (forPublishing && blocks.Count == 0)
        {
            throw new ArgumentException("Add at least one block before publishing.", nameof(content));
        }

        return new LandingPageContent
        {
            SchemaVersion = 1,
            Title = title,
            BackgroundColor = NormalizeColor(content.BackgroundColor, "#ff7133"),
            TextColor = NormalizeColor(content.TextColor, "#ffffff"),
            SharingTitle = NormalizeText(content.SharingTitle, 180),
            SharingDescription = NormalizeText(content.SharingDescription, 320),
            Blocks = blocks
        };
    }

    public static string SanitizeRichTextHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var sanitizer = new HtmlSanitizer();
        sanitizer.AllowedTags.Clear();
        foreach (var tag in new[]
                 {
                     "a", "blockquote", "br", "div", "em", "h2", "h3", "h4", "i", "li", "ol", "p", "span", "strong", "u", "ul"
                 })
        {
            sanitizer.AllowedTags.Add(tag);
        }

        sanitizer.AllowedAttributes.Clear();
        sanitizer.AllowedAttributes.Add("href");
        sanitizer.AllowedAttributes.Add("title");
        sanitizer.AllowedSchemes.Clear();
        sanitizer.AllowedSchemes.Add("https");
        sanitizer.AllowedClasses.Clear();
        sanitizer.FilterUrl += (_, args) =>
        {
            args.SanitizedUrl = string.Equals(args.Tag.LocalName, "a", StringComparison.OrdinalIgnoreCase) &&
                                IsSafeLinkUrl(args.OriginalUrl)
                ? args.OriginalUrl.Trim()
                : null;
        };

        return sanitizer.Sanitize(html);
    }

    private LandingPageBlock NormalizeBlock(LandingPageBlock block, bool forPublishing)
    {
        var type = NormalizeText(block.Type, 16).ToLowerInvariant();
        if (type is not ("image" or "text" or "button"))
        {
            throw new ArgumentException("Block type must be image, text, or button.", nameof(block));
        }

        var imageShape = NormalizeText(block.ImageShape, 16).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(imageShape))
        {
            imageShape = "square";
        }

        if (imageShape is not ("square" or "natural"))
        {
            throw new ArgumentException("Image shape must be square or natural.", nameof(block));
        }

        var alignment = NormalizeText(block.Alignment, 16).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(alignment))
        {
            alignment = "left";
        }

        if (alignment is not ("left" or "center" or "right"))
        {
            throw new ArgumentException("Alignment must be left, center, or right.", nameof(block));
        }

        var imageUrl = NormalizeText(block.ImageUrl, 2_048);
        if (imageUrl.Length > 0 && !IsSafeImageUrl(imageUrl))
        {
            throw new ArgumentException("Image URLs must be site-relative or use the configured R2 host.", nameof(block));
        }

        var url = NormalizeText(block.Url, 2_048);
        if (url.Length > 0 && !IsSafeLinkUrl(url))
        {
            throw new ArgumentException("Links must be safe site-relative URLs or HTTPS URLs.", nameof(block));
        }

        var linkType = NormalizeText(block.LinkType, 16).ToLowerInvariant();
        if (linkType is not ("" or "custom" or "subscription"))
            throw new ArgumentException("Unknown button link type.", nameof(block));
        if (type == "button" && linkType == "subscription" && url.Length > 0 && LandingPageButtonLinks.GetSubscriptionSlug(url) is null)
            throw new ArgumentException("Choose a public subscription option.", nameof(block));

        if (type == "button" && linkType == "subscription" && LandingPageButtonLinks.GetSubscriptionSlug(url) is { } subscriptionSlug)
            url = LandingPageButtonLinks.BuildSubscriptionUrl(subscriptionSlug);

        var html = NormalizeText(block.Html, MaximumHtmlLength);
        html = SanitizeRichTextHtml(html);
        var text = NormalizeText(block.Text, MaximumTextLength);
        if (forPublishing)
        {
            if (type == "image" && string.IsNullOrWhiteSpace(imageUrl))
            {
                throw new ArgumentException("Image blocks need an image URL before publishing.", nameof(block));
            }

            if (type == "text" && string.IsNullOrWhiteSpace(ResolveVisibleText(html)) && string.IsNullOrWhiteSpace(text))
            {
                throw new ArgumentException("Text blocks need text before publishing.", nameof(block));
            }

            if (type == "button" && (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(url)))
            {
                throw new ArgumentException("Button blocks need text and a safe URL before publishing.", nameof(block));
            }
        }

        return new LandingPageBlock
        {
            Id = block.Id == Guid.Empty ? Guid.NewGuid() : block.Id,
            Type = type,
            ImageUrl = imageUrl,
            AltText = NormalizeText(block.AltText, 500),
            ImageShape = imageShape,
            Html = html,
            Alignment = alignment,
            Text = text,
            Url = url,
            LinkType = linkType,
            BackgroundColor = NormalizeOptionalColor(block.BackgroundColor),
            TextColor = NormalizeOptionalColor(block.TextColor)
        };
    }

    private bool IsSafeImageUrl(string candidate)
    {
        if (candidate.Any(char.IsControl) || candidate.Contains('\\'))
        {
            return false;
        }

        if (IsSafeSiteRelativeUrl(candidate))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(_trustedR2BaseUrl) ||
            !Uri.TryCreate(candidate, UriKind.Absolute, out var assetUri) ||
            !Uri.TryCreate(_trustedR2BaseUrl, UriKind.Absolute, out var trustedBaseUri) ||
            !string.Equals(assetUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(assetUri.Host, trustedBaseUri.Host, StringComparison.OrdinalIgnoreCase) ||
            assetUri.Port != trustedBaseUri.Port ||
            !string.IsNullOrEmpty(assetUri.UserInfo))
        {
            return false;
        }

        var basePath = trustedBaseUri.AbsolutePath.TrimEnd('/');
        return string.IsNullOrEmpty(basePath) ||
               string.Equals(assetUri.AbsolutePath, basePath, StringComparison.Ordinal) ||
               assetUri.AbsolutePath.StartsWith($"{basePath}/", StringComparison.Ordinal);
    }

    public static bool IsSafeLinkUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl))
        {
            return false;
        }

        var candidate = value.Trim();
        if (candidate.Contains('\\'))
        {
            return false;
        }

        if (IsSafeSiteRelativeUrl(candidate) || candidate.StartsWith('#') || candidate.StartsWith('?'))
        {
            return true;
        }

        return Uri.TryCreate(candidate, UriKind.Absolute, out var uri) &&
               string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
               !string.IsNullOrWhiteSpace(uri.Host) &&
               string.IsNullOrEmpty(uri.UserInfo);
    }

    private static bool IsSafeSiteRelativeUrl(string candidate)
    {
        if (candidate.Length == 0 || candidate[0] != '/' ||
            candidate.StartsWith("//", StringComparison.Ordinal) ||
            candidate.Contains('\\') ||
            candidate.Any(char.IsControl))
        {
            return false;
        }

        var path = candidate.Split(['?', '#'], 2)[0];
        string decodedPath;
        try
        {
            decodedPath = Uri.UnescapeDataString(path);
        }
        catch (UriFormatException)
        {
            return false;
        }

        return !decodedPath.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => string.Equals(segment, "..", StringComparison.Ordinal));
    }

    private static string NormalizeHttpsBaseUrl(string? value)
    {
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(uri.UserInfo))
        {
            return string.Empty;
        }

        return uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
    }

    private static string NormalizeText(string? value, int maxLength)
    {
        var normalized = value?.Replace("\r\n", "\n", StringComparison.Ordinal).Trim() ?? string.Empty;
        if (normalized.Length > maxLength)
        {
            throw new ArgumentException($"Text cannot exceed {maxLength} characters.");
        }

        return normalized;
    }

    private static string NormalizeColor(string? value, string fallback)
    {
        var candidate = NormalizeText(value, 9);
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return fallback;
        }

        if (!HexColorRegex().IsMatch(candidate))
        {
            throw new ArgumentException("Colors must use a hexadecimal value such as #ff7133.");
        }

        return candidate.ToLowerInvariant();
    }

    private static string NormalizeOptionalColor(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : NormalizeColor(value, string.Empty);

    private static string ResolveVisibleText(string sanitizedHtml)
    {
        var withoutTags = HtmlTagRegex().Replace(sanitizedHtml, " ");
        var decoded = WebUtility.HtmlDecode(withoutTags);
        return WhitespaceRegex().Replace(decoded, " ").Trim();
    }

    [GeneratedRegex("^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{4}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$", RegexOptions.CultureInvariant)]
    private static partial Regex HexColorRegex();

    [GeneratedRegex("<[^>]*>", RegexOptions.CultureInvariant)]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex("\\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();
}
