namespace Shink.Services;

public interface ISubscriptionLedgerService
{
    Task<SubscriptionPersistResult> RecordPayFastEventAsync(IFormCollection formCollection, CancellationToken cancellationToken = default);
    Task<SubscriptionPersistResult> RecordPaystackEventAsync(string payloadJson, CancellationToken cancellationToken = default);
    Task<bool> HasActivePaidSubscriptionAsync(string? email, CancellationToken cancellationToken = default);
    Task<bool> HasActiveSubscriptionForTierAsync(string? email, string? tierCode, CancellationToken cancellationToken = default);
    Task<bool> UpsertSubscriberProfileAsync(
        string? email,
        string? firstName,
        string? lastName,
        string? displayName,
        string? mobileNumber,
        CancellationToken cancellationToken = default);
}

public sealed record SubscriptionPersistResult(bool IsSuccess, string? ErrorMessage = null, string? SubscriptionId = null);
