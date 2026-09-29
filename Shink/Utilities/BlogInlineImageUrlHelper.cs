using AngleSharp.Html.Parser;
using Shink.Components.Content;

namespace Shink.Utilities;

public static class BlogInlineImageUrlHelper
{
    public static string RewriteForBrowser(string? html)
    {
        if (string.IsNullOrWhiteSpace(html) ||
            !html.Contains("media.prioritybit.co.za", StringComparison.OrdinalIgnoreCase))
        {
            return html ?? string.Empty;
        }

        using var document = new HtmlParser().ParseDocument(html);
        if (document.Body is null)
        {
            return html;
        }

        var changed = false;
        foreach (var image in document.Body.QuerySelectorAll("img[src]"))
        {
            var source = image.GetAttribute("src");
            if (string.IsNullOrWhiteSpace(source))
            {
                continue;
            }

            var browserUrl = StoryItem.RewriteImagePathForBrowser(source);
            if (string.Equals(browserUrl, source, StringComparison.Ordinal))
            {
                continue;
            }

            image.SetAttribute("src", browserUrl);
            changed = true;
        }

        return changed ? document.Body.InnerHtml : html;
    }
}
