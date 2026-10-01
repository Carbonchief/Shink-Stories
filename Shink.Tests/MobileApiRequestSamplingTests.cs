using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shink.Mobile.Services;

namespace Shink.Tests;

[TestClass]
public sealed class MobileApiRequestSamplingTests
{
    [TestMethod]
    [DataRow(false, 1d)]
    [DataRow(false, 2_500d)]
    [DataRow(true, 2_000d)]
    [DataRow(true, 10_000d)]
    public void EveryFailedOrSlowRequestSurvivesSampling(bool success, double milliseconds)
    {
        Assert.IsTrue(MobileApiRequestSampling.ShouldCapture(success, milliseconds, 0.999999));
        Assert.AreEqual(1d, MobileApiRequestSampling.SampleRate(success, milliseconds));
    }

    [TestMethod]
    public void RoutineSuccessesKeepTenPercentOfUniformDrawsWithTheCorrectWeight()
    {
        var captured = Enumerable.Range(0, 1_000).Count(index =>
            MobileApiRequestSampling.ShouldCapture(true, 100, index / 1_000d));
        Assert.AreEqual(100, captured);
        Assert.AreEqual(10d, 1 / MobileApiRequestSampling.SampleRate(true, 100));
    }

    [TestMethod]
    public void SlowThresholdIsInclusiveAndQuickRequestsRespectTheSampleBoundary()
    {
        Assert.IsFalse(MobileApiRequestSampling.ShouldCapture(true, 1_999.999, 0.99));
        Assert.IsTrue(MobileApiRequestSampling.ShouldCapture(true, 2_000, 0.99));
        Assert.IsTrue(MobileApiRequestSampling.ShouldCapture(true, 50, 0.099999));
        Assert.IsFalse(MobileApiRequestSampling.ShouldCapture(true, 50, 0.10));
    }
}
