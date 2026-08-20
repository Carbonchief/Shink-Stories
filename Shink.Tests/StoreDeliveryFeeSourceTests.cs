using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Runtime.CompilerServices;

namespace Shink.Tests;

[TestClass]
public class StoreDeliveryFeeSourceTests
{
    [TestMethod]
    public void WinkelCheckoutAddsPudoDeliveryFeeUnlessAnySelectedProductOverridesIt()
    {
        var program = File.ReadAllText(GetRepoPath("Shink", "Program.cs"));
        var storeProductMigration = File.ReadAllText(GetRepoPath("Shink", "Database", "migrations", "20260820_store_product_delivery_fee_override.sql"));
        var storeProductCatalog = File.ReadAllText(GetRepoPath("Shink", "Services", "SupabaseStoreProductCatalogService.cs"));
        var adminStorePanel = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "AdminStorePanel.razor"));
        var storeOrderService = File.ReadAllText(GetRepoPath("Shink", "Services", "SupabaseStoreOrderService.cs"));
        var checkoutStart = program.IndexOf("static bool TryBuildStoreCheckoutDraft", StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, checkoutStart, "The Winkel checkout draft builder must exist.");

        var checkoutEnd = program.IndexOf("static string? GetFirstSelectedStoreProductSlugFromForm", checkoutStart, StringComparison.Ordinal);
        Assert.IsGreaterThan(checkoutStart, checkoutEnd, "The Winkel checkout draft builder block could not be isolated.");

        var checkoutBlock = program[checkoutStart..checkoutEnd];
        StringAssert.Contains(program, "const string StoreDeliveryProductSlug = \"pudo-locker-delivery\";");
        StringAssert.Contains(program, "const decimal StoreDeliveryFeeZar = 80m;");
        StringAssert.Contains(checkoutBlock, "deliveryFeeWaived |= product.WaivesDeliveryFee;");
        StringAssert.Contains(checkoutBlock, "var checkoutItems = AddStoreDeliveryLineItem(items, deliveryFeeWaived);");
        StringAssert.Contains(checkoutBlock, "var totalPriceZar = checkoutItems.Sum(item => item.LineTotalZar);");
        StringAssert.Contains(checkoutBlock, "Items: checkoutItems");
        StringAssert.Contains(checkoutBlock, "DeliveryFeeWaived: deliveryFeeWaived");
        StringAssert.Contains(program, "deliveryFeeWaived ||");
        StringAssert.Contains(program, "order.DeliveryFeeWaived");
        StringAssert.Contains(storeProductMigration, "waives_delivery_fee boolean not null default false");
        StringAssert.Contains(storeProductMigration, "delivery_fee_waived boolean not null default false");
        StringAssert.Contains(storeProductCatalog, "waives_delivery_fee");
        StringAssert.Contains(adminStorePanel, "Afleweringsfooi oorskryf");
        StringAssert.Contains(adminStorePanel, "Delivery fee override");
        StringAssert.Contains(storeOrderService, "delivery_fee_waived = draft.DeliveryFeeWaived");
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
