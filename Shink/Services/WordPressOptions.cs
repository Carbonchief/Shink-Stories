namespace Shink.Services;

public sealed class WordPressOptions
{
    public const string SectionName = "WordPress";

    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 3306;

    public string Database { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string TablePrefix { get; set; } = "wplx_";
}
