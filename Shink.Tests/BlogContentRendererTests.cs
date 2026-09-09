using AngleSharp.Html.Parser;
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
    public void RenderHtml_AllowsCloudflareStreamEmbeds()
    {
        var html = CreateRenderer().RenderHtml(
            """
            <figure class="blog-media-video blog-media-cloudflare">
                <iframe src="https://iframe.videodelivery.net/abc123" title="Ons video" allowfullscreen></iframe>
            </figure>
            """);

        StringAssert.Contains(html, "class=\"blog-media-video blog-media-cloudflare\"");
        StringAssert.Contains(html, "src=\"https://iframe.videodelivery.net/abc123\"");
        StringAssert.Contains(html, "title=\"Ons video\"");
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

    [TestMethod]
    [DataRow("text/html", "title")]
    [DataRow("application/xhtml+xml", "title")]
    [DataRow("TEXT/HTML", "style")]
    [DataRow("application/xhtml+xml", "style")]
    public void HtmlParser_ExposesMathMlIntegrationPointPayloadToSanitization(string encoding, string tag)
    {
        // GHSA-pgww-w46g-26qg: the sanitizer must see the same HTML nodes as a browser.
        using var document = new HtmlParser().ParseDocument(BuildMathMlPayload(encoding, tag));

        var image = document.QuerySelector("img");
        Assert.IsNotNull(image, "The parser must expose the injected image for sanitization.");
        Assert.AreEqual("http://www.w3.org/1999/xhtml", image.NamespaceUri);
        Assert.AreEqual("alert()", image.GetAttribute("onerror"));
    }

    [TestMethod]
    public void RenderHtml_EncodesAngleBracketsInSerializedAttributes()
    {
        var html = CreateRenderer().RenderHtml("<p title=\"a < b > c\">Teks</p>");
        using var document = new HtmlParser().ParseDocument(html);
        var paragraph = document.QuerySelector("p")!;

        Assert.AreEqual("a < b > c", paragraph.GetAttribute("title"));
        StringAssert.Contains(html, "title=\"a &lt; b &gt; c\"");
    }

    [TestMethod]
    [DataRow("text/html", "title")]
    [DataRow("application/xhtml+xml", "title")]
    [DataRow("TEXT/HTML", "style")]
    [DataRow("application/xhtml+xml", "style")]
    public void RenderHtml_RemovesMathMlPayloadAndKeepsSurroundingProse(string encoding, string tag)
    {
        var renderer = CreateRenderer();

        var html = renderer.RenderHtml(BuildMathMlPayload(encoding, tag) + "<p>Veilige storie.</p>");
        using var reparsed = new HtmlParser().ParseDocument(html);

        Assert.IsNull(reparsed.QuerySelector("math, annotation-xml, script, [onerror]"));
        StringAssert.Contains(html, "<p>Veilige storie.</p>");
        Assert.AreEqual(html, renderer.RenderHtml(html), "Sanitization must remain safe after another parse.");
    }

    [TestMethod]
    public void RenderHtml_RemovesEncodedScriptUrlsAndEventHandlers()
    {
        var html = CreateRenderer().RenderHtml(
            """
            <p onclick="alert()"><a href="jav&#x61;script:alert()">Skakel</a></p>
            <img src="/branding/Panda.webp" alt="Panda" onerror="alert()">
            <script>alert()</script>
            """);
        using var document = new HtmlParser().ParseDocument(html);

        Assert.IsNull(document.QuerySelector("script, [onclick], [onerror]"));
        Assert.IsFalse(document.QuerySelector("a")!.HasAttribute("href"));
        Assert.AreEqual("/branding/Panda.webp", document.QuerySelector("img")!.GetAttribute("src"));
    }

    [TestMethod]
    public void RenderHtml_PreservesMarkdownFormattingAndSafeLinks()
    {
        var renderer = CreateRenderer();
        const string markdown = "## Ons storie\n\n**Lees** met *vreugde*. [Luister](/luister)";

        var html = renderer.RenderHtml(markdown);

        StringAssert.Contains(html, "<strong>Lees</strong>");
        StringAssert.Contains(html, "<em>vreugde</em>");
        StringAssert.Contains(html, "href=\"/luister\"");
        Assert.AreEqual("Ons storie Lees met vreugde . Luister", renderer.ConvertToPlainText(markdown));
    }

    [TestMethod]
    public void RenderHtml_PreservesSafeInlineStyles()
    {
        var html = CreateRenderer().RenderHtml("<p style=\"text-align: center; color: red\">Gesentreer</p>");
        using var document = new HtmlParser().ParseDocument(html);

        var style = document.QuerySelector("p")!.GetAttribute("style")!;
        StringAssert.Contains(style, "text-align: center");
        StringAssert.Contains(style, "color: rgba(255, 0, 0, 1)");
    }

    private static string BuildMathMlPayload(string encoding, string tag) =>
        $"<math><annotation-xml encoding=\"{encoding}\"><{tag}><a encoding=\"</{tag}><img src=x onerror=alert()>\"></annotation-xml></math>";

    private static BlogContentRenderer CreateRenderer() =>
        new(Options.Create(new CloudflareR2Options
        {
            PublicBaseUrl = "https://media.schink.example/"
        }));
}
