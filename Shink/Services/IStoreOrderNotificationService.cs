namespace Shink.Services;

public interface IStoreOrderNotificationService
{
    Task SendPaidOrderNotificationAsync(
        StoreOrderRecord order,
        CancellationToken cancellationToken = default);

    Task SendCustomerOrderConfirmationAsync(
        StoreOrderRecord order,
        CancellationToken cancellationToken = default);
}
