using Shink.Mobile.Models;

namespace Shink.Mobile.Services;

internal sealed record MobileAnalyticsIdentity(string DistinctId, string? Email)
{
    public static MobileAnalyticsIdentity FromSession(MobileSession session, string anonymousId)
    {
        var email = session.IsSignedIn ? session.Email?.Trim().ToLowerInvariant() : null;
        return string.IsNullOrWhiteSpace(email)
            ? new(anonymousId, null)
            : new(email, email); // Matches the website's identity convention.
    }

    public void ApplyTo(Dictionary<string, object> properties)
    {
        properties["$is_identified"] = Email is not null;
        // Account attribution comes only from the authenticated session/report.
        properties.Remove("email");
        properties.Remove("$set");
        if (Email is null) return;
        properties["email"] = Email;
        properties["$set"] = new Dictionary<string, object> { ["email"] = Email };
    }
}
