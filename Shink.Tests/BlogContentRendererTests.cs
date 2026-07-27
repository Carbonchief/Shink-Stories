using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Extensions.Options;
using Shink.Services;

namespace Shink.Tests;

[TestClass]
public class BlogContentRendererTests
{
    [TestMethod]
    public void RenderHtml_NormalizesNonBreakingSpacesInRegularProse()
    {
        var renderer = CreateRenderer();

        var html = renderer.RenderHtml("<p>Moenie&nbsp;bekommer&nbsp;nie</p>");

        StringAssert.Contains(html, "<p>Moenie bekommer nie</p>");
        Assert.IsFalse(html.Contains("&nbsp;", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void RenderHtml_AllowsNormalizedYouTubeEmbeds()
    {
        var renderer = CreateRenderer();

        var html = renderer.RenderHtml(
            """
            <figure class="blog-media-video blog-media-youtube">
                <iframe src="https://www.youtube.com/watch?v=dQw4w9WgXcQ" title="Ons video" allowfullscreen></iframe>
            </figure>
            """);

        StringAssert.Contains(html, "class=\"blog-media-video blog-media-youtube\"");
        StringAssert.Contains(html, "src=\"https://www.youtube-nocookie.com/embed/dQw4w9WgXcQ?rel=0\"");
        StringAssert.Contains(html, "title=\"Ons video\"");
        Assert.IsFalse(html.Contains("youtube.com/watch", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void RenderHtml_AllowsDirectVideoFromConfiguredCloudflareBase()
    {
        var renderer = CreateRenderer();

        var html = renderer.RenderHtml(
            """
            <figure class="blog-media-video blog-media-cloudflare">
                <video src="https://media.schink.example/blog/video.mp4" title="Cloudflare video" controls playsinline preload="metadata"></video>
            </figure>
            """);

        StringAssert.Contains(html, "<video");
        StringAssert.Contains(html, "src=\"https://media.schink.example/blog/video.mp4\"");
        StringAssert.Contains(html, "controls");
        StringAssert.Contains(html, "playsinline");
        StringAssert.Contains(html, "preload=\"metadata\"");
    }

    [TestMethod]
    public void RenderHtml_RemovesUnapprovedVideoSources()
    {
        var renderer = CreateRenderer();

        var html = renderer.RenderHtml(
            """
            <iframe src="https://attacker.example/embed"></iframe>
            <video src="https://attacker.example/video.mp4" controls></video>
            <iframe src="https://notyoutube.com/embed/dQw4w9WgXcQ"></iframe>
            """);

        Assert.IsFalse(html.Contains("<iframe", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(html.Contains("<video", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(html.Contains("attacker.example", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(html.Contains("notyoutube.com", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void RenderHtml_KeepsInlineBlogImages()
    {
        var renderer = CreateRenderer();

        var html = renderer.RenderHtml(
            """
            <figure class="blog-media-image">
                <img src="https://media.schink.example/blog/prent.webp" alt="Panda" loading="lazy" decoding="async">
            </figure>
            """);

        StringAssert.Contains(html, "class=\"blog-media-image\"");
        StringAssert.Contains(html, "src=\"https://media.schink.example/blog/prent.webp\"");
        StringAssert.Contains(html, "alt=\"Panda\"");
    }

    private static BlogContentRenderer CreateRenderer() =>
        new(Options.Create(new CloudflareR2Options
        {
            PublicBaseUrl = "https://media.schink.example/"
        }));
}
