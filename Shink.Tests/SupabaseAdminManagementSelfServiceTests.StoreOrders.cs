using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Shink.Tests;

public partial class SupabaseAdminManagementSelfServiceTests
{
    [TestMethod]
    public async Task StoreOrders_NonAdminCannotReadOrdersOrRecordShipment()
    {
        using var handler = new RecordingHandler(request =>
        {
            Assert.IsTrue(IsSupabaseGet(request, "/rest/v1/admin_users"), "A denied user must not reach the orders table.");
            return JsonResponse("[]");
        });
        var service = CreateService(handler);
        var orderId = Guid.NewGuid();
        const string buyerEmail = "buyer@example.com";

        await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() => service.GetStoreOrdersPageAsync(buyerEmail, 0, 25));
        await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() => service.GetStoreOrderAsync(buyerEmail, orderId));
        await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() => service.MarkStoreOrderShippedAsync(buyerEmail, orderId));
    }
}
