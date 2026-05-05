using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shink.Services;

namespace Shink.Tests;

[TestClass]
public class NotificationImagePathTests
{
    [TestMethod]
    public void PriorityBitMediaUrlsAreServedThroughLocalProxy()
    {
        const string source = "https://media.prioritybit.co.za/uploaded/stories/images/2026/05/kameelperd-en-olifant-thumbnail.jpg";

        var rewritten = InvokeNormalizeNotificationImagePath(source);

        Assert.AreEqual(
            $"/media/image?src={Uri.EscapeDataString(source)}",
            rewritten);
    }

    [TestMethod]
    public void LocalStoryAssetPathsStayLocal()
    {
        const string source = "/stories/thumbs/kameelperd-en-olifant.jpg";

        var rewritten = InvokeNormalizeNotificationImagePath(source);

        Assert.AreEqual(source, rewritten);
    }

    [TestMethod]
    public void EmptyNotificationImagesFallBackToBrandLogo()
    {
        var rewritten = InvokeNormalizeNotificationImagePath("   ");

        Assert.AreEqual("/branding/schink-logo-green.png", rewritten);
    }

    private static string InvokeNormalizeNotificationImagePath(string? imagePath)
    {
        var method = typeof(SupabaseUserNotificationService).GetMethod(
            "NormalizeNotificationImagePath",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.IsNotNull(method);
        var result = method.Invoke(null, [imagePath]);
        Assert.IsNotNull(result);
        return (string)result;
    }
}
