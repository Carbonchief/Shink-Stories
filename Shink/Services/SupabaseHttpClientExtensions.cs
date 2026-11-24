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

    internal static bool TryParseContentRangeCount(this HttpResponseMessage response, out int count)
    {
        count = 0;
        if (!response.Headers.TryGetValues("Content-Range", out var values) &&
            !response.Content.Headers.TryGetValues("Content-Range", out values))
        {
            return false;
        }

        var contentRange = values.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(contentRange))
        {
            return false;
        }

        var slashIndex = contentRange.LastIndexOf('/');
        if (slashIndex < 0 || slashIndex >= contentRange.Length - 1)
        {
            return false;
        }

        return int.TryParse(contentRange[(slashIndex + 1)..], out count);
    }
}
