namespace Shink.Services;

public interface IGratisSubscriberEmailSequenceService
{
    Task<GratisSubscriberSequenceStartResult> TryStartAsync(
        string? email,
        string? firstName,
        string? lastName,
        CancellationToken cancellationToken = default);

    Task<bool> MarkPaidAsync(
        string? email,
        CancellationToken cancellationToken = default);
}

public enum GratisSubscriberSequenceStartResult
{
    Started,
    SkippedUnsubscribed,
    Failed
}
