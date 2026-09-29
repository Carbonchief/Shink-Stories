using AngleSharp.Html.Parser;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shink.Utilities;

namespace Shink.Tests;

[TestClass]
public class BlogInlineImageUrlHelperTests
{
    [TestMethod]
    public void RewriteForBrowser_UsesExistingImageProxyForSavedInlineImages()
    {
        const string imageUrl = "https://media.prioritybit.co.za/uploaded/stories/images/blog-inline.jpg";
        var html = $"<figure class=\"blog-media-image\"><img src=\"{imageUrl}\" alt=\"Blogprent\" loading=\"lazy\"></figure>";

        var rewritten = BlogInlineImageUrlHelper.RewriteForBrowser(html);
        using var document = new HtmlParser().ParseDocument(rewritten);
        var image = document.QuerySelector("figure.blog-media-image img");

        Assert.IsNotNull(image);
        Assert.AreEqual($"/media/image?src={Uri.EscapeDataString(imageUrl)}", image.GetAttribute("src"));
        Assert.AreEqual("Blogprent", image.GetAttribute("alt"));
        Assert.AreEqual("lazy", image.GetAttribute("loading"));
    }

    [TestMethod]
    public void RewriteForBrowser_PreservesOtherImageSourcesAndEmptyContent()
    {
        const string html = "<p><img src=\"/branding/Panda.webp\" alt=\"Panda\"></p>";

        Assert.AreEqual(html, BlogInlineImageUrlHelper.RewriteForBrowser(html));
        Assert.AreEqual(string.Empty, BlogInlineImageUrlHelper.RewriteForBrowser(null));
    }
}
