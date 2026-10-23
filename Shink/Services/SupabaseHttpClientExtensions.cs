using System.Net;

namespace Shink.Services;

public static class SupabaseHttpClientExtensions
{
    public static IHttpClientBuilder WithSupabaseResponseCompression(this IHttpClientBuilder builder) =>
        builder.ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
            PooledConnectionLifetime = TimeSpan.FromMinutes(2)
        });
}
