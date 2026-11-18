using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shink.Components.Content;

namespace Shink.Tests;

[TestClass]
public sealed class SpeellysteCarouselOrderPolicyTests
{
    [TestMethod]
    public void MissingOrEmptyCustomOrderPreservesPageOrder()
    {
        Guid[] pageOrder = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];

        CollectionAssert.AreEqual(pageOrder, SpeellysteCarouselOrderPolicy.Apply(pageOrder, null, id => id).ToArray());
        CollectionAssert.AreEqual(pageOrder, SpeellysteCarouselOrderPolicy.Apply(pageOrder, [], id => id).ToArray());
    }

    [TestMethod]
    public void CustomOrderSkipsMissingAndDuplicateIdsAndAppendsUnlistedPlaylistsInPageOrder()
    {
        var listedFirst = Guid.NewGuid();
        var listedSecond = Guid.NewGuid();
        var unlistedFirst = Guid.NewGuid();
        var unlistedSecond = Guid.NewGuid();
        Guid[] pageOrder = [unlistedFirst, listedSecond, unlistedSecond, listedFirst];
        Guid[] customOrder = [Guid.NewGuid(), listedFirst, listedSecond, listedFirst];

        var carouselOrder = SpeellysteCarouselOrderPolicy.Apply(pageOrder, customOrder, id => id);

        CollectionAssert.AreEqual(
            new[] { listedFirst, listedSecond, unlistedFirst, unlistedSecond },
            carouselOrder.ToArray());
        CollectionAssert.AreEqual(
            new[] { unlistedFirst, listedSecond, unlistedSecond, listedFirst },
            pageOrder);
    }
}
