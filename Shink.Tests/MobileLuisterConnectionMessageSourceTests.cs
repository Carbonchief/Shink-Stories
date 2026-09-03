using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Shink.Tests;

[TestClass]
public sealed class MobileLuisterConnectionMessageSourceTests
{
    [TestMethod]
    public void LuisterConnectionFailureUsesFriendlyAfrikaansOfflineGuidance()
    {
        var source = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "LuisterPage.cs"));

        StringAssert.Contains(
            source,
            "Jou internet is af. Gaan na \\\"Aflaai\\\".");
        StringAssert.Contains(source, "RenderNoticeState(OfflineFeedMessage);");
        StringAssert.Contains(source, "_loadErrorMessage = OfflineFeedMessage;");
        Assert.DoesNotContain("_loadErrorMessage = ex.Message;", source, StringComparison.Ordinal);
    }

    private static string GetRepoPath(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate the mobile Luister page.");
    }
}
