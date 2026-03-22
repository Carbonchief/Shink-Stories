namespace Shink.Services;

public sealed class AuthSessionOptions
{
    public const string SectionName = "AuthSessions";

    public int DefaultMaxConcurrentSessions { get; set; } = 2;
    public int SessionLifetimeDays { get; set; } = 14;
    public Dictionary<string, int> TierSessionLimits { get; set; } = new();
}
