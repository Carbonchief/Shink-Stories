namespace Shink.Services;

public interface IAbandonedCartRecoveryService
{
    Task StartSequenceAsync(
        AbandonedCartRecoveryStartRequest request,
        CancellationToken cancellationToken = default);

    Task ResolveByCheckoutReferenceAsync(
        string sourceType,
        string checkoutReference,
        string resolution,
        CancellationToken cancellationToken = default);

    Task ResolveSubscriptionRecoveriesAsync(
        string? customerEmail,
        string? tierCode,
        string resolution,
        CancellationToken cancellationToken = default);

    Task<bool> OptOutAsync(
        string recoveryId,
        string token,
        CancellationToken cancellationToken = default);

    Task<AbandonedCartRecoveryRecord?> GetActiveRecoveryAsync(
        string recoveryId,
        string token,
        CancellationToken cancellationToken = default);
}

public sealed record AbandonedCartRecoveryStartRequest(
    string SourceType,
    string SourceKey,
    string CheckoutReference,
    string Provider,
    string CustomerEmail,
    string? CustomerName,
    string ItemName,
    string ItemSummary,
    decimal? CartTotalZar,
    string CheckoutUrl,
    string OptOutBaseUrl);

public sealed record AbandonedCartRecoveryRecord(
    string RecoveryId,
    string SourceType,
    string SourceKey,
    string CheckoutReference,
    string Provider,
    string CustomerEmail,
    string? CustomerName,
    string ItemName,
    string ItemSummary,
    decimal? CartTotalZar);
