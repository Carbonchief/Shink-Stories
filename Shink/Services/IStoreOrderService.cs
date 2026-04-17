namespace Shink.Services;

public interface IStoreOrderService
{
    Task<StoreOrderCreateResult> CreatePendingOrderAsync(
        StoreOrderDraft draft,
        CancellationToken cancellationToken = default);

    Task<StoreOrderRecord?> GetOrderByReferenceAsync(
        string? orderReference,
        CancellationToken cancellationToken = default);

    Task<StoreOrderPaymentUpdateResult> ApplyPaymentUpdateAsync(
        StoreOrderPaymentUpdate update,
        CancellationToken cancellationToken = default);

    Task<StoreOrderPaymentUpdateResult> RecordPaystackWebhookAsync(
        string payloadJson,
        CancellationToken cancellationToken = default);
}

public sealed record StoreOrderDraft(
    string OrderReference,
    string ProductSlug,
    string ProductName,
    int Quantity,
    decimal UnitPriceZar,
    string CustomerName,
    string CustomerEmail,
    string CustomerPhone,
    string DeliveryAddressLine1,
    string? DeliveryAddressLine2,
    string? DeliverySuburb,
    string DeliveryCity,
    string DeliveryPostalCode,
    string? Notes);

public sealed record StoreOrderRecord(
    Guid OrderId,
    string OrderReference,
    string ProductSlug,
    string ProductName,
    int Quantity,
    decimal UnitPriceZar,
    decimal TotalPriceZar,
    string CustomerName,
    string CustomerEmail,
    string CustomerPhone,
    string DeliveryAddressLine1,
    string? DeliveryAddressLine2,
    string? DeliverySuburb,
    string DeliveryCity,
    string DeliveryPostalCode,
    string? Notes,
    string PaymentStatus,
    string Provider,
    string Currency,
    string? StatusReason,
    string? ProviderTransactionId,
    DateTimeOffset? PaidAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record StoreOrderCreateResult(
    bool IsSuccess,
    StoreOrderRecord? Order = null,
    string? ErrorMessage = null);

public sealed record StoreOrderPaymentUpdate(
    string OrderReference,
    string PaymentStatus,
    int AmountInCents,
    string Currency,
    string? CustomerEmail,
    string? ProviderTransactionId,
    DateTimeOffset? PaidAt,
    string? StatusReason,
    string RawPayload,
    string Source);

public sealed record StoreOrderPaymentUpdateResult(
    bool IsSuccess,
    StoreOrderRecord? Order = null,
    bool StatusChanged = false,
    bool WasAlreadyPaid = false,
    string? ErrorMessage = null);
