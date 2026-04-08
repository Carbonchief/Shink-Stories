using Microsoft.Extensions.Configuration;

namespace Shink.Services;

internal sealed record PostHogSettings(string? ProjectApiKey, string? HostUrl)
{
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ProjectApiKey) &&
        !string.IsNullOrWhiteSpace(HostUrl);

    public bool HasAnyValue =>
        !string.IsNullOrWhiteSpace(ProjectApiKey) ||
        !string.IsNullOrWhiteSpace(HostUrl);

    public static PostHogSettings FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return new PostHogSettings(
            ResolveValue(configuration, "PostHog:ProjectApiKey", "POSTHOG_PROJECT_API_KEY", "POSTHOG_API_KEY"),
            ResolveValue(configuration, "PostHog:HostUrl", "POSTHOG_HOST_URL", "POSTHOG_HOST"));
    }

    private static string? ResolveValue(IConfiguration configuration, params string[] keys)
    {
        foreach (var key in keys)
        {
            var value = configuration[key];
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }
}
