namespace Shink.Services;

public interface IAuthSessionService
{
    Task<AuthSessionIssueResult> IssueSessionAsync(
        string? email,
        string? userAgent,
        string? ipAddress,
        CancellationToken cancellationToken = default);

    Task<AuthSessionValidationState> ValidateSessionAsync(
        string? email,
        Guid sessionId,
        CancellationToken cancellationToken = default);

    Task RevokeSessionAsync(
        string? email,
        Guid sessionId,
        CancellationToken cancellationToken = default);
}

public enum AuthSessionValidationState
{
    Active = 0,
    Inactive = 1,
    Unknown = 2
}

public sealed record AuthSessionIssueResult(
    bool IsSuccess,
    Guid SessionId,
    int MaxConcurrentSessions,
    int SessionLifetimeDays,
    string? ErrorMessage = null);
