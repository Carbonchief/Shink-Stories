using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shink.Mobile.Models;

namespace Shink.Tests;

[TestClass]
public sealed class StoryImageDecodeSizeTests
{
    [TestMethod]
    [DataRow(960, 1280, 460, 614, true, 614)]
    [DataRow(1280, 720, 460, 614, true, 1092)]
    [DataRow(1280, 720, 460, 614, false, 460)]
    [DataRow(1280, 720, 1280, 720, true, 1280)]
    [DataRow(300, 400, 460, 614, true, 400)]
    [DataRow(1280, 720, 0, 0, true, 1280)]
    [DataRow(0, 0, 460, 614, true, 1280)]
    public void DecodePreservesDisplayResolutionAndCrop(
        int width, int height, int targetWidth, int targetHeight, bool fill, int expected)
    {
        Assert.AreEqual(expected, StoryImageDecodeSize.Resolve(width, height, targetWidth, targetHeight, fill));
    }
}
