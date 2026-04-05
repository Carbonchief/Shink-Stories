using Microsoft.AspNetCore.Http;

namespace Shink.Services;

public static class SocialPreviewRequestDetector
{
    private static readonly string[] UserAgentTokens =
    {
        "facebookexternalhit",
        "facebot",
        "whatsapp",
        "twitterbot",
        "xbot",
        "linkedinbot",
        "slackbot",
        "discordbot",
        "telegrambot",
        "skypeuripreview",
        "google-read-aloud",
        "applebot",
        "vkshare",
        "linebot",
        "mastodon",
        "embedly",
        "quora link preview",
        "pinterest",
        "redditbot"
    };

    public static bool IsSocialPreviewRequest(HttpContext? httpContext)
    {
        if (httpContext is null)
        {
            return false;
        }

        var userAgent = httpContext.Request.Headers.UserAgent.ToString();
        return IsSocialPreviewUserAgent(userAgent);
    }

    public static bool IsSocialPreviewUserAgent(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            return false;
        }

        return UserAgentTokens.Any(token => userAgent.Contains(token, StringComparison.OrdinalIgnoreCase));
    }
}
