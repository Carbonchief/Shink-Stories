using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Runtime.CompilerServices;

namespace Shink.Tests;

[TestClass]
public class StoreDeliveryFeeSourceTests
{
    [TestMethod]
    public void WinkelCheckoutAddsPudoDeliveryFeeBeforePaystackAmount()
    {
        var program = File.ReadAllText(GetRepoPath("Shink", "Program.cs"));
        var migration = File.ReadAllText(GetRepoPath("Shink", "Database", "migrations", "20260429_store_orders_allow_delivery_fee_total.sql"));
        var checkoutStart = program.IndexOf("static bool TryBuildStoreCheckoutDraft", StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, checkoutStart, "The Winkel checkout draft builder must exist.");

        var checkoutEnd = program.IndexOf("static string? GetFirstSelectedStoreProductSlugFromForm", checkoutStart, StringComparison.Ordinal);
        Assert.IsGreaterThan(checkoutStart, checkoutEnd, "The Winkel checkout draft builder block could not be isolated.");

        var checkoutBlock = program[checkoutStart..checkoutEnd];
        StringAssert.Contains(program, "const string StoreDeliveryProductSlug = \"pudo-locker-delivery\";");
        StringAssert.Contains(program, "const decimal StoreDeliveryFeeZar = 80m;");
        StringAssert.Contains(checkoutBlock, "var checkoutItems = AddStoreDeliveryLineItem(items);");
        StringAssert.Contains(checkoutBlock, "var totalPriceZar = checkoutItems.Sum(item => item.LineTotalZar);");
        StringAssert.Contains(checkoutBlock, "Items: checkoutItems");
        StringAssert.Contains(migration, "drop constraint if exists store_orders_check");
    }

    [TestMethod]
    public void StoreEmailsRenderPersistedOrderItemsIncludingDelivery()
    {
        var notificationService = File.ReadAllText(GetRepoPath("Shink", "Services", "ResendStoreOrderNotificationService.cs"));

        StringAssert.Contains(notificationService, "order.Items.Count > 0");
        StringAssert.Contains(notificationService, "items.Select(item =>");
        StringAssert.Contains(notificationService, "ORDER_ITEMS_HTML");
        StringAssert.Contains(notificationService, "ORDER_ITEMS_TEXT");
    }

    [TestMethod]
    public void WinkelPageExplainsPudoLockerDeliveryCost()
    {
        var winkel = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "Winkel.razor"));

        StringAssert.Contains(winkel, "Ons gebruik PUDO lockers vir aflewering");
        StringAssert.Contains(winkel, "private const decimal DeliveryFeeZar = 80m;");
        StringAssert.Contains(winkel, "insluitend PUDO locker aflewering");
    }

    private static string GetRepoPath(params string[] segments)
    {
        var parts = new[]
        {
            Path.GetDirectoryName(GetSourceFilePath())!,
            ".."
        }.Concat(segments).ToArray();

        return Path.GetFullPath(Path.Combine(parts));
    }

    private static string GetSourceFilePath([CallerFilePath] string path = "") => path;
}
