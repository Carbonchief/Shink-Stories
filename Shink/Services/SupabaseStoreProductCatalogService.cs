using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Shink.Components.Content;

namespace Shink.Services;

public sealed class SupabaseStoreProductCatalogService(
    HttpClient httpClient,
    IOptions<SupabaseOptions> supabaseOptions,
    IMemoryCache memoryCache,
    ILogger<SupabaseStoreProductCatalogService> logger) : IStoreProductCatalogService
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(2);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient = httpClient;
    private readonly SupabaseOptions _options = supabaseOptions.Value;
    private readonly IMemoryCache _memoryCache = memoryCache;
    private readonly ILogger<SupabaseStoreProductCatalogService> _logger = logger;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public async Task<IReadOnlyList<StoreProduct>> GetEnabledProductsAsync(CancellationToken cancellationToken = default)
    {
        var products = await GetCatalogAsync(cancellationToken);
        return products
            .Where(product => product.IsEnabled)
            .OrderBy(product => product.SortOrder)
            .ThenBy(product => product.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<IReadOnlyList<StoreProduct>> GetAllProductsAsync(CancellationToken cancellationToken = default)
    {
        var products = await GetCatalogAsync(cancellationToken);
        return products
            .OrderBy(product => product.SortOrder)
            .ThenBy(product => product.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<StoreProduct?> FindEnabledBySlugAsync(string? slug, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return null;
        }

        var normalizedSlug = slug.Trim();
        var products = await GetEnabledProductsAsync(cancellationToken);
        return products.FirstOrDefault(product =>
            string.Equals(product.Slug, normalizedSlug, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<IReadOnlyList<StoreProduct>> GetCatalogAsync(CancellationToken cancellationToken)
    {
        if (_memoryCache.TryGetValue(StoreProductCatalogCacheKeys.Catalog, out IReadOnlyList<StoreProduct>? cached) &&
            cached is not null)
        {
            return cached;
        }

        await _refreshLock.WaitAsync(CancellationToken.None);
        try
        {
            if (_memoryCache.TryGetValue(StoreProductCatalogCacheKeys.Catalog, out cached) &&
                cached is not null)
            {
                return cached;
            }

            var products = await FetchCatalogAsync(CancellationToken.None);
            _memoryCache.Set(StoreProductCatalogCacheKeys.Catalog, products, CacheDuration);
            return products;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private async Task<IReadOnlyList<StoreProduct>> FetchCatalogAsync(CancellationToken cancellationToken)
    {
        var fallbackProducts = BuildFallbackProducts();

        if (!TryBuildSupabaseBaseUri(out var baseUri))
        {
            _logger.LogWarning("Supabase store product lookup skipped: URL is not configured.");
            return fallbackProducts;
        }

        var apiKey = ResolveReadApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("Supabase store product lookup skipped: AnonKey is not configured.");
            return fallbackProducts;
        }

        var requestUri = new Uri(
            baseUri,
            "rest/v1/store_products" +
            "?select=store_product_id,slug,name,description,image_path,alt_text,theme_class,unit_price_zar,sort_order,is_enabled,updated_at" +
            "&order=sort_order.asc" +
            "&order=name.asc" +
            "&limit=500");

        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.TryAddWithoutValidation("apikey", apiKey);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                _logger.LogInformation("Supabase store product lookup skipped: table store_products is not available yet.");
                return fallbackProducts;
            }

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "Supabase store product lookup failed. Status={StatusCode} Body={Body}",
                    (int)response.StatusCode,
                    body);
                return fallbackProducts;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var rows = await JsonSerializer.DeserializeAsync<List<StoreProductRow>>(stream, JsonOptions, cancellationToken)
                ?? [];

            return rows
                .Where(IsUsableRow)
                .Select(MapRow)
                .OrderBy(product => product.SortOrder)
                .ThenBy(product => product.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(exception, "Supabase store product lookup failed unexpectedly. Falling back to in-memory catalog.");
            return fallbackProducts;
        }
    }

    private bool TryBuildSupabaseBaseUri(out Uri baseUri)
    {
        baseUri = default!;
        if (string.IsNullOrWhiteSpace(_options.Url))
        {
            return false;
        }

        if (!Uri.TryCreate(_options.Url, UriKind.Absolute, out var parsedUri))
        {
            return false;
        }

        baseUri = parsedUri;
        return true;
    }

    private string ResolveReadApiKey() => _options.AnonKey;

    private static IReadOnlyList<StoreProduct> BuildFallbackProducts() =>
        StoreProductCatalog.All
            .Select((product, index) => product with
            {
                SortOrder = (index + 1) * 10,
                IsEnabled = true
            })
            .ToArray();

    private static bool IsUsableRow(StoreProductRow row) =>
        row.StoreProductId != Guid.Empty &&
        !string.IsNullOrWhiteSpace(row.Slug) &&
        !string.IsNullOrWhiteSpace(row.Name) &&
        !string.IsNullOrWhiteSpace(row.ImagePath) &&
        row.UnitPriceZar > 0m;

    private static StoreProduct MapRow(StoreProductRow row)
    {
        var normalizedSlug = row.Slug.Trim().ToLowerInvariant();
        var normalizedName = row.Name.Trim();
        var normalizedDescription = NormalizeOptionalText(row.Description, 600);
        var normalizedImagePath = NormalizeImagePath(row.ImagePath);
        var normalizedAltText = NormalizeOptionalText(row.AltText, 220) ?? $"{normalizedName} produk";
        var normalizedThemeClass = NormalizeOptionalText(row.ThemeClass, 80) ?? string.Empty;
        var normalizedSortOrder = Math.Clamp(row.SortOrder, -500_000, 500_000);

        return new StoreProduct(
            StoreProductId: row.StoreProductId,
            Slug: normalizedSlug,
            Name: normalizedName,
            Description: normalizedDescription,
            ImagePath: normalizedImagePath,
            AltText: normalizedAltText,
            ThemeClass: normalizedThemeClass,
            UnitPriceZar: row.UnitPriceZar,
            SortOrder: normalizedSortOrder,
            IsEnabled: row.IsEnabled);
    }

    private static string? NormalizeOptionalText(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength
            ? trimmed
            : trimmed[..maxLength];
    }

    private static string NormalizeImagePath(string? value)
    {
        var normalized = NormalizeOptionalText(value, 1024) ?? string.Empty;
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        if (Uri.TryCreate(normalized, UriKind.Absolute, out var absoluteUri))
        {
            if (string.Equals(absoluteUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(absoluteUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                return absoluteUri.ToString();
            }

            return string.Empty;
        }

        if (normalized.StartsWith("//", StringComparison.Ordinal))
        {
            return string.Empty;
        }

        if (normalized.StartsWith("~/", StringComparison.Ordinal))
        {
            normalized = $"/{normalized[2..]}";
        }

        normalized = normalized.Replace('\\', '/');
        return normalized.StartsWith("/", StringComparison.Ordinal)
            ? normalized
            : $"/{normalized.TrimStart('/')}";
    }

    private sealed class StoreProductRow
    {
        [JsonPropertyName("store_product_id")]
        public Guid StoreProductId { get; set; }

        [JsonPropertyName("slug")]
        public string Slug { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("image_path")]
        public string? ImagePath { get; set; }

        [JsonPropertyName("alt_text")]
        public string? AltText { get; set; }

        [JsonPropertyName("theme_class")]
        public string? ThemeClass { get; set; }

        [JsonPropertyName("unit_price_zar")]
        public decimal UnitPriceZar { get; set; }

        [JsonPropertyName("sort_order")]
        public int SortOrder { get; set; }

        [JsonPropertyName("is_enabled")]
        public bool IsEnabled { get; set; }
    }
}
