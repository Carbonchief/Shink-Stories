namespace Shink.Services;

public interface ISubscriptionLedgerService
{
    Task<SubscriptionPersistResult> RecordPayFastEventAsync(IFormCollection formCollection, CancellationToken cancellationToken = default);
}

public sealed record SubscriptionPersistResult(bool IsSuccess, string? ErrorMessage = null, string? SubscriptionId = null);
