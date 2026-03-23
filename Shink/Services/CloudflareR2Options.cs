namespace Shink.Services;

public sealed class CloudflareR2Options
{
    public const string SectionName = "CloudflareR2";

    public string PublicBaseUrl { get; set; } = string.Empty;
}
