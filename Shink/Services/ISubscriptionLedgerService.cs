namespace Shink.Services;

public interface ISubscriptionLedgerService
{
    Task<SubscriptionPersistResult> RecordPayFastEventAsync(IFormCollection formCollection, CancellationToken cancellationToken = default);
    Task<SubscriptionPersistResult> RecordPaystackEventAsync(string payloadJson, CancellationToken cancellationToken = default);
    Task ProcessExpiredPaymentRecoveriesAsync(CancellationToken cancellationToken = default);
    Task<bool> HasActivePaidSubscriptionAsync(string? email, CancellationToken cancellationToken = default);
    Task<bool> HasActiveSubscriptionForTierAsync(string? email, string? tierCode, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetActiveTierCodesAsync(string? email, CancellationToken cancellationToken = default);
    Task<SubscriberProfile?> GetSubscriberProfileAsync(string? email, CancellationToken cancellationToken = default);
    Task<bool> UpsertSubscriberProfileAsync(
        string? email,
        string? firstName,
        string? lastName,
        string? displayName,
        string? mobileNumber,
        string? profileImageUrl = null,
        string? profileImageObjectKey = null,
        string? profileImageContentType = null,
        CancellationToken cancellationToken = default);
    Task<bool> EnsureGratisAccessAsync(
        string? email,
        string? firstName,
        string? lastName,
        string? displayName,
        string? mobileNumber,
        string? profileImageUrl = null,
        string? profileImageObjectKey = null,
        string? profileImageContentType = null,
        CancellationToken cancellationToken = default);
    Task<bool> UpdateSubscriberLastLoginAsync(
        string? email,
        DateTimeOffset lastLoginAtUtc,
        CancellationToken cancellationToken = default);
    Task<SubscriberEmailChangeResult> ChangeSubscriberEmailAsync(
        string? currentEmail,
        string? newEmail,
        CancellationToken cancellationToken = default);
}

public sealed record SubscriptionPersistResult(bool IsSuccess, string? ErrorMessage = null, string? SubscriptionId = null);
public sealed record SubscriberProfile(
    string Email,
    string? FirstName,
    string? LastName,
    string? DisplayName,
    string? MobileNumber,
    string? ProfileImageUrl,
    string? ProfileImageObjectKey,
    string? ProfileImageContentType);
public sealed record SubscriberEmailChangeResult(bool IsSuccess, string? ErrorMessage = null);
