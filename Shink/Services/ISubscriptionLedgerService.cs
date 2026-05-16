namespace Shink.Services;

public interface ISubscriptionLedgerService
{
    Task<SubscriptionPersistResult> RecordPayFastEventAsync(IFormCollection formCollection, CancellationToken cancellationToken = default);
    Task RecordPayFastWebhookFailureAsync(IFormCollection? formCollection, string failureStage, string errorMessage, CancellationToken cancellationToken = default);
    Task<SubscriptionPersistResult> RecordPaystackEventAsync(string payloadJson, CancellationToken cancellationToken = default);
    Task RecordPaystackWebhookFailureAsync(string? payloadJson, string failureStage, string errorMessage, CancellationToken cancellationToken = default);
    Task ProcessExpiredPaymentRecoveriesAsync(CancellationToken cancellationToken = default);
    Task<bool> HasActivePaidSubscriptionAsync(string? email, CancellationToken cancellationToken = default);
    Task<bool> HasActiveSubscriptionForTierAsync(string? email, string? tierCode, CancellationToken cancellationToken = default);
    Task<bool> HasPendingPaystackRepairForTierAsync(string? email, string? tierCode, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetActiveTierCodesAsync(string? email, CancellationToken cancellationToken = default);
    Task<CurrentPaidSubscription?> GetCurrentPaidSubscriptionAsync(string? email, CancellationToken cancellationToken = default);
    Task<PaidSubscriptionAttention> GetPaidSubscriptionAttentionAsync(string? email, CancellationToken cancellationToken = default);
    Task<SubscriptionPlanChangeResult> ChangePaidSubscriptionPlanAsync(
        string? email,
        string? targetPlanSlug,
        CancellationToken cancellationToken = default);
    Task<SubscriptionRepairResult> TryRepairPaidSubscriptionAsync(string? email, CancellationToken cancellationToken = default);
    Task<SubscriptionFreeTierTransferResult> TransferPaidSubscriptionToGratisAsync(string? email, CancellationToken cancellationToken = default);
    Task<SubscriptionCancelResult> CancelPaidSubscriptionAsync(string? email, CancellationToken cancellationToken = default);
    Task<AccountClosureResult> CloseAccountAsync(string? email, CancellationToken cancellationToken = default);
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
    Task<SubscriptionCodeSignupPreviewResult> PreviewSignupDiscountCodeAsync(
        string? code,
        string? selectedTierCode,
        CancellationToken cancellationToken = default);
    Task<SubscriptionCodeApplicationResult> ApplySignupDiscountCodeAsync(
        string? email,
        string? code,
        string? selectedTierCode,
        CancellationToken cancellationToken = default);
    Task<SubscriberEmailChangeResult> ChangeSubscriberEmailAsync(
        string? currentEmail,
        string? newEmail,
        CancellationToken cancellationToken = default);
}

public sealed record SubscriptionPersistResult(bool IsSuccess, string? ErrorMessage = null, string? SubscriptionId = null);
public sealed record CurrentPaidSubscription(
    string SubscriptionId,
    string TierCode,
    string? Provider,
    DateTimeOffset? NextRenewalAtUtc,
    DateTimeOffset? CancelledAtUtc,
    bool IsCancellationScheduled);
public sealed record PaidSubscriptionAttention(
    bool RequiresAttention,
    string? Reason = null,
    string? SubscriptionId = null,
    string? TierCode = null,
    string? PlanSlug = null,
    string? Provider = null,
    bool CanAttemptAutomaticRetry = false);
public sealed record SubscriptionPlanChangeResult(
    bool IsSuccess,
    string? PlanSlug = null,
    string? ChangeType = null,
    DateTimeOffset? EffectiveAtUtc = null,
    decimal? ChargedAmountZar = null,
    string? ErrorMessage = null);
public sealed record SubscriptionRepairResult(
    bool IsRecovered,
    string? PlanSlug = null,
    string? ErrorMessage = null,
    bool IsPending = false);
public sealed record SubscriptionFreeTierTransferResult(
    bool IsSuccess,
    string? ErrorMessage = null,
    int CancelledPaidSubscriptions = 0);
public sealed record SubscriptionCancelResult(
    bool IsSuccess,
    string? ErrorMessage = null,
    DateTimeOffset? AccessEndsAtUtc = null,
    int CancelledSubscriptions = 0);
public sealed record AccountClosureResult(bool IsSuccess, string? ErrorMessage = null);
public sealed record SubscriberProfile(
    string Email,
    string? FirstName,
    string? LastName,
    string? DisplayName,
    string? MobileNumber,
    string? ProfileImageUrl,
    string? ProfileImageObjectKey,
    string? ProfileImageContentType);
public sealed record SubscriptionCodeSignupPreviewResult(
    bool IsValid,
    string? ErrorMessage = null,
    string? ResolvedTierCode = null,
    string? ResolvedTierName = null,
    DateTimeOffset? AccessEndsAtUtc = null,
    DateTimeOffset? CodeExpiresAtUtc = null,
    bool BypassesPayment = false,
    IReadOnlyList<SubscriptionCodeTierOption>? TierOptions = null);
public sealed record SubscriptionCodeTierOption(
    string TierCode,
    string DisplayName);
public sealed record SubscriptionCodeApplicationResult(
    bool IsSuccess,
    string? ErrorMessage = null,
    string? TierCode = null,
    DateTimeOffset? AccessEndsAtUtc = null);
public sealed record SubscriberEmailChangeResult(bool IsSuccess, string? ErrorMessage = null);
