namespace Shink.Services;

public interface IStoryTrackingService
{
    Task<bool> RecordStoryViewAsync(string? email, StoryViewTrackingRequest request, CancellationToken cancellationToken = default);
    Task<bool> RecordStoryListenAsync(string? email, StoryListenTrackingRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<UserStoryProgressItem>> GetUserStoryProgressAsync(string? email, CancellationToken cancellationToken = default);
}

public sealed record StoryViewTrackingRequest(
    string StorySlug,
    string StoryPath,
    string? Source,
    string? ReferrerPath);

public sealed record StoryListenTrackingRequest(
    string StorySlug,
    string StoryPath,
    Guid SessionId,
    string EventType,
    decimal ListenedSeconds,
    decimal? PositionSeconds,
    decimal? DurationSeconds,
    string? Source,
    bool IsCompleted);

public sealed record UserStoryProgressItem(
    string StorySlug,
    string StoryPath,
    string Source,
    DateTimeOffset LastListenedAtUtc,
    decimal TotalListenedSeconds,
    decimal? LastPositionSeconds,
    decimal? DurationSeconds,
    bool IsCompleted);
