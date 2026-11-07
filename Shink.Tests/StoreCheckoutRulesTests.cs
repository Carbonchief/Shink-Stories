using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shink.Components.Content;
using Shink.Services;

namespace Shink.Tests;

[TestClass]
public sealed class StoreCheckoutRulesTests
{
    [TestMethod]
    [DataRow("58000", true)]
    [DataRow("50000", false)]
    [DataRow("58000.0", false)]
    [DataRow("-58000", false)]
    [DataRow("garbage", false)]
    [DataRow("", false)]
    [DataRow(null, false)]
    public void MatchesDisplayedTotal_RejectsChangedOrInvalidAmounts(string? displayed, bool expected) =>
        Assert.AreEqual(expected, StoreCheckoutRules.MatchesDisplayedTotal(displayed, 580m));

    [TestMethod]
    public void HasUnavailableProducts_RejectsDisabledAndUnknownProducts()
    {
        var disabled = StoreProductCatalog.All[0] with { IsEnabled = false };
        Assert.IsTrue(StoreCheckoutRules.HasUnavailableProducts(Form(("qty-suurlemoentjie", "1")), [disabled]));
        Assert.IsTrue(StoreCheckoutRules.HasUnavailableProducts(Form(("qty-removed", "2")), StoreProductCatalog.All));
        Assert.IsFalse(StoreCheckoutRules.HasUnavailableProducts(Form(("qty-removed", "0")), StoreProductCatalog.All));
        Assert.IsFalse(StoreCheckoutRules.HasUnavailableProducts(Form(("qty-tiekie", "2")), StoreProductCatalog.All));
    }

    [TestMethod]
    public void ReceiptAccess_RequiresAuthenticTokenForExactOrder()
    {
        var access = new StoreReceiptAccess(new EphemeralDataProtectionProvider());
        var token = access.CreateToken("W-ORDER-1");
        Assert.IsTrue(access.CanReadToken(token, "W-ORDER-1"));
        Assert.IsFalse(access.CanReadToken(token, "W-ORDER-2"));
        Assert.IsFalse(access.CanReadToken(token + "tampered", "W-ORDER-1"));
        Assert.IsFalse(access.CanReadToken(null, "W-ORDER-1"));
        Assert.IsFalse(access.CanReadToken("sukses", "W-ORDER-1"));
    }

    [TestMethod]
    public void ReceiptAccess_RejectsExpiredTokens()
    {
        var provider = new EphemeralDataProtectionProvider();
        var expired = provider.CreateProtector("Shink.Store.Receipt.v1").ToTimeLimitedDataProtector()
            .Protect("W-ORDER-1", DateTimeOffset.UtcNow.AddMinutes(-1));
        Assert.IsFalse(new StoreReceiptAccess(provider).CanReadToken(expired, "W-ORDER-1"));
    }

    [TestMethod]
    public void ReceiptAccess_StartingAnotherCheckoutPreservesBothReceipts()
    {
        var access = new StoreReceiptAccess(new EphemeralDataProtectionProvider());
        var cookies = new Dictionary<string, string>();
        foreach (var reference in new[] { "W-ORDER-1", "W-ORDER-2" })
        {
            var checkout = new DefaultHttpContext();
            checkout.Request.Scheme = "https";
            access.Grant(checkout, reference);
            foreach (var header in checkout.Response.Headers.SetCookie)
            {
                var pair = header!.Split(';')[0].Split('=', 2);
                cookies[pair[0]] = pair[1];
            }
        }

        var returningBrowser = new DefaultHttpContext();
        returningBrowser.Request.Headers.Cookie = string.Join("; ", cookies.Select(cookie => $"{cookie.Key}={cookie.Value}"));
        Assert.IsTrue(access.CanRead(returningBrowser, "W-ORDER-1"), "A later checkout must not invalidate the first receipt.");
        Assert.IsTrue(access.CanRead(returningBrowser, "W-ORDER-2"));
        Assert.IsFalse(access.CanRead(returningBrowser, "W-ORDER-3"));
    }

    [TestMethod]
    public void ReceiptAccess_CheckoutIdentityIsProtectedAndBoundToItsOrder()
    {
        const string firstId = "11111111111111111111111111111111";
        const string secondId = "22222222222222222222222222222222";
        var access = new StoreReceiptAccess(new EphemeralDataProtectionProvider());
        var first = new DefaultHttpContext();
        var second = new DefaultHttpContext();
        access.Grant(first, "W-ORDER-1", firstId);
        access.Grant(second, "W-ORDER-2", secondId);
        var browser = new DefaultHttpContext();
        browser.Request.Headers.Cookie = string.Join("; ", new[] { first, second }
            .SelectMany(context => context.Response.Headers.SetCookie)
            .Select(header => header!.Split(';')[0]));

        Assert.AreEqual(firstId, access.GetCheckoutId(browser, "W-ORDER-1"));
        Assert.AreEqual(secondId, access.GetCheckoutId(browser, "W-ORDER-2"));
        Assert.IsNull(access.GetCheckoutId(browser, "W-ORDER-3"));
        Assert.IsFalse(access.CanRead(new DefaultHttpContext(), "W-ORDER-1"));
    }

    [TestMethod]
    public void ReceiptAccess_LegacyReceiptStillWorksWithoutAuthorizingBasketRemoval()
    {
        var provider = new EphemeralDataProtectionProvider();
        var legacyToken = provider.CreateProtector("Shink.Store.Receipt.v1").ToTimeLimitedDataProtector()
            .Protect("W-LEGACY", TimeSpan.FromDays(1));
        var browser = new DefaultHttpContext();
        browser.Request.Headers.Cookie = $"shink.store.receipt={legacyToken}";
        var access = new StoreReceiptAccess(provider);

        Assert.IsTrue(access.CanRead(browser, "W-LEGACY"));
        Assert.IsFalse(access.CanRead(browser, "W-OTHER"));
        Assert.IsNull(access.GetCheckoutId(browser, "W-LEGACY"));
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("untrusted-value")]
    public void ReceiptAccess_InvalidCheckoutIdentityCannotAuthorizeBasketRemoval(string? checkoutId)
    {
        var access = new StoreReceiptAccess(new EphemeralDataProtectionProvider());
        var granted = new DefaultHttpContext();
        access.Grant(granted, "W-ORDER-1", checkoutId);
        var browser = new DefaultHttpContext();
        browser.Request.Headers.Cookie = granted.Response.Headers.SetCookie[0]!.Split(';')[0];

        Assert.IsTrue(access.CanRead(browser, "W-ORDER-1"));
        Assert.IsNull(access.GetCheckoutId(browser, "W-ORDER-1"));
    }

    private static FormCollection Form(params (string Key, string Value)[] fields) =>
        new(fields.ToDictionary(field => field.Key, field => new StringValues(field.Value)));
}
