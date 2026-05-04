using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Shink.Tests;

[TestClass]
public class BlogContentRendererTests
{
    [TestMethod]
    public void RenderHtml_NormalizesNonBreakingSpacesInRegularProse()
    {
        var renderer = new Shink.Services.BlogContentRenderer();

        var html = renderer.RenderHtml("<p>Moenie&nbsp;bekommer&nbsp;nie</p>");

        StringAssert.Contains(html, "<p>Moenie bekommer nie</p>");
        Assert.IsFalse(html.Contains("&nbsp;", StringComparison.OrdinalIgnoreCase));
    }
}
