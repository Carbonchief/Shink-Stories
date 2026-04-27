namespace Shink.Services;

public interface ISubscriptionNotificationEmailService
{
    Task SendSubscriptionConfirmationAsync(
        SubscriptionConfirmationEmailRequest request,
        CancellationToken cancellationToken = default);

    Task SendSubscriptionEndedAsync(
        SubscriptionEndedEmailRequest request,
        CancellationToken cancellationToken = default);

    Task SendAdminOpsAlertAsync(
        AdminOpsAlertEmailRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record SubscriptionConfirmationEmailRequest(
    string SubscriptionId,
    string Email,
    string? FirstName,
    string? DisplayName,
    string PlanName,
    decimal? AmountZar,
    int BillingPeriodMonths,
    string Provider,
    string? PaymentReference,
    DateTimeOffset? NextRenewalAtUtc);

public sealed record SubscriptionEndedEmailRequest(
    string SubscriptionId,
    string Email,
    string? FirstName,
    string? DisplayName,
    string PlanName,
    string StatusLabel,
    string AccessMessage,
    DateTimeOffset EndedAtUtc,
    string IdempotencySuffix);

public sealed record AdminOpsAlertEmailRequest(
    string AlertKey,
    string Severity,
    string Title,
    string Summary,
    string Details,
    string EventReference,
    DateTimeOffset OccurredAtUtc,
    string? ActionUrl = null);
