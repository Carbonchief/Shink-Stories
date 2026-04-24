namespace Shink.Services;

public interface ISubscriptionPaymentRecoveryEmailService
{
    Task<SubscriptionPaymentRecoveryEmailSequence?> ScheduleSequenceAsync(
        SubscriptionPaymentRecoveryEmailRequest request,
        CancellationToken cancellationToken = default);

    Task CancelSequenceAsync(
        SubscriptionPaymentRecoveryEmailSequence sequence,
        CancellationToken cancellationToken = default);
}

public sealed record SubscriptionPaymentRecoveryEmailRequest(
    string RecoveryId,
    string SubscriptionId,
    string Email,
    string? FirstName,
    string? DisplayName,
    string PlanName,
    string Provider,
    DateTimeOffset FirstFailedAtUtc,
    DateTimeOffset SuspensionAtUtc);

public sealed record SubscriptionPaymentRecoveryEmailSequence(
    string? ImmediateEmailId,
    string? WarningEmailId,
    string? SuspensionEmailId);
