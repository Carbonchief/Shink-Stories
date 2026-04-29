using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shink.Components.Content;

namespace Shink.Tests;

[TestClass]
public class StoryImagePathTests
{
    [TestMethod]
    public void PriorityBitMediaImageUrlsAreServedThroughLocalProxy()
    {
        const string source = "https://media.prioritybit.co.za/uploaded/stories/images/2026/04/storie-hoekie-cover.jpg";

        var rewritten = StoryItem.RewriteImagePathForBrowser(source);

        Assert.AreEqual(
            $"/media/image?src={Uri.EscapeDataString(source)}",
            rewritten);
    }

    [TestMethod]
    public void OtherAbsoluteImageUrlsStayAbsolute()
    {
        const string source = "https://example.com/image.jpg";

        var rewritten = StoryItem.RewriteImagePathForBrowser(source);

        Assert.AreEqual(source, rewritten);
    }
}
