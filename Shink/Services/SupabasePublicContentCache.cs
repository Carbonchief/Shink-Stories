using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Shink.Services;

public interface IPublicContentVersions
{
    Task<long?> GetRevisionAsync(string category, CancellationToken cancellationToken = default);
}

public static class PublicContentCacheKeys
{
    public static async Task<string?> ResolveAsync(
        IPublicContentVersions? versions, string category, string key, CancellationToken cancellationToken)
    {
        if (versions is null)
        {
            return key;
        }

        var revision = await versions.GetRevisionAsync(category, cancellationToken);
        return revision is long value ? $"{key}:revision:{value}" : null;
    }
}

public sealed class SupabasePublicContentVersions(
    IHttpClientFactory clients,
    IOptions<SupabaseOptions> options,
    ILogger<SupabasePublicContentVersions> logger) : IPublicContentVersions
{
    public async Task<long?> GetRevisionAsync(string category, CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        var apiKey = string.IsNullOrWhiteSpace(settings.PublishableKey) ? settings.SecretKey : settings.PublishableKey;
        if (!Uri.TryCreate(settings.Url.TrimEnd('/') + "/", UriKind.Absolute, out var baseUri) ||
            string.IsNullOrWhiteSpace(apiKey))
        {
            return null;
        }

        try
        {
            // No TTL or shared in-flight result: a read starting after publish
            // must see its committed revision, including on another app instance.
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(baseUri,
                "rest/v1/public_content_cache_revisions?select=revision&cache_key=eq." +
                Uri.EscapeDataString(category) + "&limit=1"));
            request.Headers.TryAddWithoutValidation("apikey", apiKey);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            using var client = clients.CreateClient("supabase-content-versions");
            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null; // Missing migration/outage: fetch content fresh; never serve a stale long cache.
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            return json.RootElement.ValueKind == JsonValueKind.Array && json.RootElement.GetArrayLength() == 1 &&
                   json.RootElement[0].TryGetProperty("revision", out var revision) && revision.TryGetInt64(out var value)
                ? value : null;
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException ||
                                          exception is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogDebug("Public content revision unavailable for {Category}; reading uncached content.", category);
            return null;
        }
    }
}

/// <summary>
/// Cache authoritative public JSON responses for seven days. Existing short
/// object caches remain bounded so transient fallbacks cannot persist for days.
/// Every read verifies the database revision before reusing either cache tier.
/// Private progress, favourites, notifications and access checks never enter it.
/// </summary>
public sealed class SupabasePublicContentCacheHandler(
    IMemoryCache cache, IPublicContentVersions versions,
    IOptions<SupabaseOptions> options) : DelegatingHandler
{
    public static readonly TimeSpan ContentLifetime = TimeSpan.FromDays(7);
    private const int MaximumResponseBytes = 4 * 1024 * 1024;
    // Bounded stripes also cover missing/untrusted landing-page slugs without
    // accumulating one permanent lock per requested URL.
    private static readonly SemaphoreSlim[] RefreshLocks =
        Enumerable.Range(0, 256).Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var category = Category(request, options.Value.Url);
        if (category is null)
        {
            return await base.SendAsync(request, cancellationToken);
        }

        var revision = await versions.GetRevisionAsync(category, cancellationToken);
        if (revision is null)
        {
            return await base.SendAsync(request, cancellationToken);
        }

        var credentials = request.Headers.Authorization?.ToString() + "|" +
                          (request.Headers.TryGetValues("apikey", out var keys) ? string.Join(",", keys) : "");
        var bodyIdentity = request.Method == HttpMethod.Post && request.Content is not null
            ? await request.Content.ReadAsStringAsync(cancellationToken) : "";
        var representationHeaders = string.Join("|", new[] { "Prefer", "Range", "Accept" }
            .Select(name => request.Headers.TryGetValues(name, out var values) ? string.Join(",", values) : ""));
        var identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            request.Method + "|" + request.RequestUri!.AbsoluteUri + "|" + credentials + "|" +
            representationHeaders + "|" + bodyIdentity)));
        var key = $"public-content:http:{category}:{identity}";
        if (cache.TryGetValue(key, out CachedResponse? cached) && cached?.Revision == revision)
        {
            return cached.Create(request);
        }

        var gate = RefreshLocks[Convert.ToByte(identity[..2], 16)];
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (cache.TryGetValue(key, out cached) && cached?.Revision == revision)
            {
                return cached.Create(request);
            }

            var response = await base.SendAsync(request, cancellationToken);
            if (response.StatusCode != HttpStatusCode.OK ||
                response.Content.Headers.ContentLength > MaximumResponseBytes)
            {
                return response;
            }

            var body = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            if (body.Length > MaximumResponseBytes)
            {
                return response;
            }

            try
            {
                using var json = JsonDocument.Parse(body);
                if (json.RootElement.ValueKind != JsonValueKind.Array)
                {
                    return response;
                }
                if (json.RootElement.GetArrayLength() == 0)
                {
                    return response; // Do not retain arbitrarily many missing slugs/IDs.
                }
            }
            catch (JsonException)
            {
                return response;
            }

            cached = new CachedResponse(revision.Value, body,
                response.Headers.Where(header => !header.Key.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase))
                    .ToDictionary(header => header.Key, header => header.Value.ToArray()),
                response.Content.Headers.Where(header => !header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                    .ToDictionary(header => header.Key, header => header.Value.ToArray()));
            if (!cache.TryGetValue(key, out CachedResponse? newer) || newer is null || newer.Revision <= revision.Value)
            {
                cache.Set(key, cached, ContentLifetime);
            }
            return response;
        }
        finally
        {
            gate.Release();
        }
    }

    private static string? Category(HttpRequestMessage request, string configuredUrl)
    {
        if (request.RequestUri is not { } uri || !Uri.TryCreate(configuredUrl, UriKind.Absolute, out var configured) ||
            uri.GetLeftPart(UriPartial.Authority) != configured.GetLeftPart(UriPartial.Authority))
        {
            return null;
        }

        if (request.Method == HttpMethod.Post && uri.AbsolutePath == "/rest/v1/rpc/get_story_catalogue_listing")
        {
            return "stories";
        }

        if (request.Method != HttpMethod.Get || !uri.AbsolutePath.StartsWith("/rest/v1/", StringComparison.Ordinal))
        {
            return null;
        }

        var query = uri.Query;
        return uri.AbsolutePath["/rest/v1/".Length..] switch
        {
            "stories" when query.Contains("status=eq.published", StringComparison.Ordinal) => "stories",
            "story_playlists" or "story_playlist_items" => "stories",
            "story_characters" when query.Contains("status=eq.published", StringComparison.Ordinal) => "characters",
            // Blog visibility also changes with scheduled publication times
            // without a database write. Keep that domain's existing short cache.
            "blog_posts" or "blog_categories" or "blog_tags" or "blog_post_tags" => null,
            "store_products" => "products",
            "resource_types" or "resource_documents" => "resources",
            "landing_pages" when query.Contains("is_published=eq.true", StringComparison.Ordinal) &&
                                 !query.Contains("draft_content", StringComparison.Ordinal) => "landing_pages",
            _ => null
        };
    }

    private sealed record CachedResponse(long Revision, byte[] Body, Dictionary<string, string[]> Headers,
        Dictionary<string, string[]> ContentHeaders)
    {
        public HttpResponseMessage Create(HttpRequestMessage request)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new ByteArrayContent(Body)
            };
            foreach (var header in Headers) response.Headers.TryAddWithoutValidation(header.Key, header.Value);
            foreach (var header in ContentHeaders) response.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            return response;
        }
    }
}
