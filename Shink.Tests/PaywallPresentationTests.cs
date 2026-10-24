using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shink.Mobile.Services;

namespace Shink.Tests;

[TestClass]
public sealed class PaywallPresentationTests
{
    [TestMethod]
    [DataRow("R99,99", 1, "per maand")]
    [DataRow("€109,99", 12, "per jaar")]
    public void SelectedPriceAndBillingPeriodStayConsistent(string price, int months, string period)
    {
        var cta = PaywallPresentation.PurchaseLabel(price, months, true);
        var disclosure = PaywallPresentation.RenewalDisclosure(price, months, true);
        StringAssert.Contains(cta, price + " " + period);
        StringAssert.Contains(disclosure, price + " " + period);
        StringAssert.Contains(disclosure, "Hernu outomaties");
    }

    [TestMethod]
    public void CancellationCopyUsesTheCorrectStore()
    {
        var apple = PaywallPresentation.RenewalDisclosure("R990", 12, true);
        var google = PaywallPresentation.RenewalDisclosure("R990", 12, false);
        StringAssert.Contains(apple, "24 uur");
        StringAssert.Contains(apple, "App Store-intekeninge");
        StringAssert.Contains(google, "Google Play");
        Assert.DoesNotContain("24 uur", google);
    }

    [TestMethod]
    public void UnavailableStorePriceDoesNotAdvertiseAFallbackCharge()
    {
        Assert.AreEqual("Tans nie beskikbaar nie", PaywallPresentation.PurchaseLabel(null, 12, true));
        Assert.DoesNotContain("R", PaywallPresentation.RenewalDisclosure(null, 12, false));
        Assert.AreEqual("Teken in om voort te gaan", PaywallPresentation.PurchaseLabel("R990", 12, false));
    }

    [TestMethod]
    public void ValueBadgeRequiresComparableVerifiedPrices()
    {
        Assert.IsTrue(PaywallPresentation.IsAnnualBetterValue(99_000_000, "ZAR", 990_000_000, "ZAR"));
        Assert.IsFalse(PaywallPresentation.IsAnnualBetterValue(99_000_000, "ZAR", 990_000_000, "USD"));
        Assert.IsFalse(PaywallPresentation.IsAnnualBetterValue(99_000_000, "ZAR", 1_188_000_000, "ZAR"));
        Assert.IsFalse(PaywallPresentation.IsAnnualBetterValue(null, "ZAR", 990_000_000, "ZAR"));
        Assert.IsFalse(PaywallPresentation.IsAnnualBetterValue(99_000_000, null, 990_000_000, null));
        Assert.IsFalse(PaywallPresentation.IsAnnualBetterValue(99_000_000, "ZAR", 0, "ZAR"));
        Assert.IsFalse(PaywallPresentation.IsAnnualBetterValue(0, "ZAR", 990_000_000, "ZAR"));
    }

    [TestMethod]
    public void ParentChallengeRequiresAllFourDigitsInOrder()
    {
        var challenge = new PaywallParentChallenge(9, 3, 7, 2);
        StringAssert.Contains(challenge.Instruction, "nege · drie · sewe · twee");
        Assert.IsTrue(challenge.IsCorrect("9372"));
        Assert.IsTrue(challenge.IsCorrect(" 9372 "));
        Assert.IsFalse(challenge.IsCorrect("9327"));
        Assert.IsFalse(challenge.IsCorrect("937"));
        Assert.IsFalse(challenge.IsCorrect("nege drie sewe twee"));
        Assert.IsFalse(challenge.IsCorrect(null));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PaywallParentChallenge(10, 1, 2, 3));
    }

    [TestMethod]
    [DataRow("Shink/wwwroot/branding/characters/skillie.png", "paywall_skillie.png")]
    [DataRow("Shink.Mobile/Resources/Images/oortjies_02.png", "paywall_oortjies.png")]
    [DataRow("Shink/wwwroot/branding/characters/prinses-panda.png", "paywall_prinses_panda.png")]
    public void PackagedCharactersAreTheOriginalArtwork(string original, string packaged)
    {
        CollectionAssert.AreEqual(File.ReadAllBytes(Repo(original)),
            File.ReadAllBytes(Repo("Shink.Mobile/Resources/Images/" + packaged)));
    }

    private static string Repo(string relative, [CallerFilePath] string source = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "..", relative));
}
