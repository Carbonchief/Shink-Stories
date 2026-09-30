using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Options;

namespace Shink.Services;

public sealed partial class SupabaseLandingPageService(
    HttpClient httpClient,
    IOptions<SupabaseOptions> supabaseOptions,
    AuthenticationStateProvider authenticationStateProvider,
    LandingPageContentValidator contentValidator,
    ILogger<SupabaseLandingPageService> logger) : ILandingPageAdminService, ILandingPageCatalogService
{
    private const string AdminSelect = "page_id,slug,draft_content,published_content,is_published,has_ever_published,revision,updated_at";
    private const string PublicSelect = "page_id,slug,published_content";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient = httpClient;
    private readonly SupabaseOptions _options = supabaseOptions.Value;
    private readonly AuthenticationStateProvider _authenticationStateProvider = authenticationStateProvider;
    private readonly LandingPageContentValidator _contentValidator = contentValidator;
    private readonly ILogger<SupabaseLandingPageService> _logger = logger;

    public async Task<IReadOnlyList<LandingPageRecord>> GetPagesAsync(CancellationToken cancellationToken = default)
    {
        var context = await ResolveAdminContextAsync(cancellationToken);
        if (context.ErrorCode is not null)
        {
            throw new LandingPageServiceException(context.ErrorCode);
        }

        var uri = BuildLandingPagesUri(context.BaseUri!, $"?select={AdminSelect}&deleted_at=is.null&order=updated_at.desc");
        var response = await SendAsync(CreateRequest(HttpMethod.Get, uri, context.ApiKey!), cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new LandingPageServiceException(MapApiError(response.StatusCode, response.Body));
        }

        try
        {
            return DeserializeRows(response.Body)
                .Select(MapAdminRow)
                .OrderByDescending(page => page.UpdatedAt)
                .ToArray();
        }
        catch (JsonException exception)
        {
            _logger.LogWarning(exception, "Landing page list response could not be read.");
            throw new LandingPageServiceException(LandingPageErrorCodes.RequestFailed);
        }
        catch (ArgumentException exception)
        {
            _logger.LogWarning(exception, "Landing page list contained invalid content.");
            throw new LandingPageServiceException(LandingPageErrorCodes.RequestFailed);
        }
    }

    public async Task<LandingPageOperationResult> SaveDraftAsync(
        LandingPageSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        var context = await ResolveAdminContextAsync(cancellationToken);
        if (context.ErrorCode is not null)
        {
            return Failure(context.ErrorCode);
        }

        if (request is null || request.Content is null)
        {
            return Failure(LandingPageErrorCodes.ContentInvalid);
        }

        var slug = NormalizeSlug(request.Slug);
        if (slug is null)
        {
            return Failure(LandingPageErrorCodes.SlugInvalid);
        }

        LandingPageContent normalizedContent;
        try
        {
            normalizedContent = _contentValidator.NormalizeAndValidate(request.Content);
        }
        catch (ArgumentException exception)
        {
            _logger.LogInformation(exception, "Landing page draft failed content validation.");
            return Failure(LandingPageErrorCodes.ContentInvalid);
        }

        if (request.PageId is null)
        {
            if (request.ExpectedRevision is not null)
            {
                return Failure(LandingPageErrorCodes.RevisionRequired);
            }

            return await CreateDraftAsync(context, slug, normalizedContent, cancellationToken);
        }

        if (request.ExpectedRevision is not long expectedRevision || expectedRevision < 1)
        {
            return Failure(LandingPageErrorCodes.RevisionRequired);
        }

        try
        {
            var existing = await FetchAdminRowByIdAsync(context, request.PageId.Value, cancellationToken);
            if (existing is null)
            {
                return Failure(LandingPageErrorCodes.PageNotFound);
            }

            if (existing.Revision != expectedRevision)
            {
                return Failure(LandingPageErrorCodes.RevisionConflict);
            }

            if (existing.HasEverPublished && !string.Equals(existing.Slug, slug, StringComparison.Ordinal))
            {
                return Failure(LandingPageErrorCodes.SlugImmutable);
            }

            var payload = new Dictionary<string, object?>
            {
                ["draft_content"] = normalizedContent,
                ["revision"] = expectedRevision + 1,
                ["updated_at"] = DateTimeOffset.UtcNow
            };
            if (!existing.HasEverPublished)
            {
                payload["slug"] = slug;
            }

            var uri = BuildLandingPagesUri(
                context.BaseUri!,
                $"?page_id=eq.{request.PageId.Value:D}&deleted_at=is.null&revision=eq.{expectedRevision}&select={AdminSelect}");
            var response = await SendAsync(
                CreateJsonRequest(new HttpMethod("PATCH"), uri, context.ApiKey!, payload, "return=representation"),
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return Failure(MapApiError(response.StatusCode, response.Body));
            }

            var savedRows = DeserializeRows(response.Body);
            if (savedRows.Count == 0)
            {
                return await ResolveMissingConditionalUpdateAsync(context, request.PageId.Value, cancellationToken);
            }

            return Success(MapAdminRow(savedRows[0]));
        }
        catch (LandingPageServiceException exception)
        {
            return Failure(exception.ErrorCode);
        }
        catch (ArgumentException exception)
        {
            _logger.LogWarning(exception, "Landing page draft response contained invalid data.");
            return Failure(LandingPageErrorCodes.RequestFailed);
        }
        catch (Exception exception) when (IsRequestException(exception, cancellationToken))
        {
            _logger.LogWarning(exception, "Landing page draft save failed.");
            return Failure(LandingPageErrorCodes.RequestFailed);
        }
    }

    public async Task<LandingPageOperationResult> PublishAsync(
        Guid pageId,
        long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        var context = await ResolveAdminContextAsync(cancellationToken);
        if (context.ErrorCode is not null)
        {
            return Failure(context.ErrorCode);
        }

        if (pageId == Guid.Empty)
        {
            return Failure(LandingPageErrorCodes.PageNotFound);
        }

        if (expectedRevision < 1)
        {
            return Failure(LandingPageErrorCodes.RevisionRequired);
        }

        try
        {
            var existing = await FetchAdminRowByIdAsync(context, pageId, cancellationToken);
            if (existing is null)
            {
                return Failure(LandingPageErrorCodes.PageNotFound);
            }

            if (existing.Revision != expectedRevision)
            {
                return Failure(LandingPageErrorCodes.RevisionConflict);
            }

            if (existing.DraftContent is null)
            {
                return Failure(LandingPageErrorCodes.PublishValidationFailed);
            }

            LandingPageContent publishSnapshot;
            try
            {
                publishSnapshot = _contentValidator.NormalizeAndValidate(existing.DraftContent, forPublishing: true);
            }
            catch (ArgumentException exception)
            {
                _logger.LogInformation(exception, "Landing page draft failed publish validation. page_id={PageId}", pageId);
                return Failure(LandingPageErrorCodes.PublishValidationFailed);
            }

            var payload = new
            {
                published_content = publishSnapshot,
                is_published = true,
                has_ever_published = true,
                revision = expectedRevision + 1,
                updated_at = DateTimeOffset.UtcNow
            };
            var uri = BuildLandingPagesUri(
                context.BaseUri!,
                $"?page_id=eq.{pageId:D}&deleted_at=is.null&revision=eq.{expectedRevision}&select={AdminSelect}");
            var response = await SendAsync(
                CreateJsonRequest(new HttpMethod("PATCH"), uri, context.ApiKey!, payload, "return=representation"),
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return Failure(MapApiError(response.StatusCode, response.Body));
            }

            var savedRows = DeserializeRows(response.Body);
            if (savedRows.Count == 0)
            {
                return await ResolveMissingConditionalUpdateAsync(context, pageId, cancellationToken);
            }

            return Success(MapAdminRow(savedRows[0]));
        }
        catch (LandingPageServiceException exception)
        {
            return Failure(exception.ErrorCode);
        }
        catch (ArgumentException exception)
        {
            _logger.LogWarning(exception, "Landing page publish response contained invalid data. page_id={PageId}", pageId);
            return Failure(LandingPageErrorCodes.RequestFailed);
        }
        catch (Exception exception) when (IsRequestException(exception, cancellationToken))
        {
            _logger.LogWarning(exception, "Landing page publish failed. page_id={PageId}", pageId);
            return Failure(LandingPageErrorCodes.RequestFailed);
        }
    }

    public async Task<LandingPageOperationResult> UnpublishAsync(
        Guid pageId,
        long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        var context = await ResolveAdminContextAsync(cancellationToken);
        if (context.ErrorCode is not null)
        {
            return Failure(context.ErrorCode);
        }

        if (pageId == Guid.Empty)
        {
            return Failure(LandingPageErrorCodes.PageNotFound);
        }

        if (expectedRevision < 1)
        {
            return Failure(LandingPageErrorCodes.RevisionRequired);
        }

        try
        {
            var existing = await FetchAdminRowByIdAsync(context, pageId, cancellationToken);
            if (existing is null)
            {
                return Failure(LandingPageErrorCodes.PageNotFound);
            }

            if (existing.Revision != expectedRevision)
            {
                return Failure(LandingPageErrorCodes.RevisionConflict);
            }

            if (!existing.IsPublished)
            {
                return Failure(LandingPageErrorCodes.PageNotPublished);
            }

            var payload = new
            {
                is_published = false,
                revision = expectedRevision + 1,
                updated_at = DateTimeOffset.UtcNow
            };
            var uri = BuildLandingPagesUri(
                context.BaseUri!,
                $"?page_id=eq.{pageId:D}&deleted_at=is.null&revision=eq.{expectedRevision}&select={AdminSelect}");
            var response = await SendAsync(
                CreateJsonRequest(new HttpMethod("PATCH"), uri, context.ApiKey!, payload, "return=representation"),
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return Failure(MapApiError(response.StatusCode, response.Body));
            }

            var savedRows = DeserializeRows(response.Body);
            if (savedRows.Count == 0)
            {
                return await ResolveMissingConditionalUpdateAsync(context, pageId, cancellationToken);
            }

            return Success(MapAdminRow(savedRows[0]));
        }
        catch (LandingPageServiceException exception)
        {
            return Failure(exception.ErrorCode);
        }
        catch (ArgumentException exception)
        {
            _logger.LogWarning(exception, "Landing page unpublish response contained invalid data. page_id={PageId}", pageId);
            return Failure(LandingPageErrorCodes.RequestFailed);
        }
        catch (Exception exception) when (IsRequestException(exception, cancellationToken))
        {
            _logger.LogWarning(exception, "Landing page unpublish failed. page_id={PageId}", pageId);
            return Failure(LandingPageErrorCodes.RequestFailed);
        }
    }

    public async Task<LandingPageOperationResult> DeleteAsync(
        Guid pageId,
        long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        var context = await ResolveAdminContextAsync(cancellationToken);
        if (context.ErrorCode is not null)
        {
            return Failure(context.ErrorCode);
        }

        if (pageId == Guid.Empty)
        {
            return Failure(LandingPageErrorCodes.PageNotFound);
        }

        if (expectedRevision < 1)
        {
            return Failure(LandingPageErrorCodes.RevisionRequired);
        }

        try
        {
            var existing = await FetchAdminRowByIdAsync(context, pageId, cancellationToken);
            if (existing is null)
            {
                return Failure(LandingPageErrorCodes.PageNotFound);
            }

            if (existing.Revision != expectedRevision)
            {
                return Failure(LandingPageErrorCodes.RevisionConflict);
            }

            var payload = new
            {
                deleted_at = DateTimeOffset.UtcNow,
                is_published = false,
                revision = expectedRevision + 1,
                updated_at = DateTimeOffset.UtcNow
            };
            var uri = BuildLandingPagesUri(
                context.BaseUri!,
                $"?page_id=eq.{pageId:D}&deleted_at=is.null&revision=eq.{expectedRevision}&select={AdminSelect}");
            var response = await SendAsync(
                CreateJsonRequest(new HttpMethod("PATCH"), uri, context.ApiKey!, payload, "return=representation"),
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return Failure(MapApiError(response.StatusCode, response.Body));
            }

            var savedRows = DeserializeRows(response.Body);
            if (savedRows.Count == 0)
            {
                return await ResolveMissingConditionalUpdateAsync(context, pageId, cancellationToken);
            }

            return Success(MapAdminRow(savedRows[0]));
        }
        catch (LandingPageServiceException exception)
        {
            return Failure(exception.ErrorCode);
        }
        catch (ArgumentException exception)
        {
            _logger.LogWarning(exception, "Landing page delete response contained invalid data. page_id={PageId}", pageId);
            return Failure(LandingPageErrorCodes.RequestFailed);
        }
        catch (Exception exception) when (IsRequestException(exception, cancellationToken))
        {
            _logger.LogWarning(exception, "Landing page delete failed. page_id={PageId}", pageId);
            return Failure(LandingPageErrorCodes.RequestFailed);
        }
    }

    public async Task<bool> EnsureAdminAsync(CancellationToken cancellationToken = default)
    {
        var context = await ResolveAdminContextAsync(cancellationToken);
        if (context.ErrorCode is null)
        {
            return true;
        }

        if (string.Equals(context.ErrorCode, LandingPageErrorCodes.AdminRequired, StringComparison.Ordinal))
        {
            return false;
        }

        throw new LandingPageServiceException(context.ErrorCode);
    }

    public async Task<PublishedLandingPage?> FindPublishedBySlugAsync(
        string? slug,
        CancellationToken cancellationToken = default)
    {
        var normalizedSlug = NormalizeSlug(slug);
        if (normalizedSlug is null)
        {
            return null;
        }

        if (!TryBuildSupabaseBaseUri(out var baseUri) || string.IsNullOrWhiteSpace(ResolveSecretKey()))
        {
            throw new LandingPageServiceException(LandingPageErrorCodes.SupabaseNotConfigured);
        }

        var apiKey = ResolveSecretKey()!;
        var uri = BuildLandingPagesUri(
            baseUri,
            $"?select={PublicSelect}&slug=eq.{Uri.EscapeDataString(normalizedSlug)}&deleted_at=is.null&is_published=eq.true&limit=1");
        var response = await SendAsync(CreateRequest(HttpMethod.Get, uri, apiKey), cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Published landing page lookup failed. Status={StatusCode}",
                (int)response.StatusCode);
            throw new LandingPageServiceException(MapApiError(response.StatusCode, response.Body));
        }

        try
        {
            var row = DeserializeRows(response.Body).FirstOrDefault();
            if (row is null)
            {
                return null;
            }

            if (row.PageId == Guid.Empty || row.PublishedContent is null)
            {
                throw new LandingPageServiceException(LandingPageErrorCodes.RequestFailed);
            }

            var content = _contentValidator.NormalizeAndValidate(row.PublishedContent, forPublishing: true);
            return new PublishedLandingPage(row.PageId, row.Slug, content);
        }
        catch (JsonException exception)
        {
            _logger.LogWarning(exception, "Published landing page response could not be read.");
            throw new LandingPageServiceException(LandingPageErrorCodes.RequestFailed);
        }
        catch (ArgumentException exception)
        {
            _logger.LogWarning(exception, "Published landing page contains invalid content.");
            throw new LandingPageServiceException(LandingPageErrorCodes.RequestFailed);
        }
    }

    private async Task<LandingPageOperationResult> CreateDraftAsync(
        AdminContext context,
        string slug,
        LandingPageContent content,
        CancellationToken cancellationToken)
    {
        try
        {
            var payload = new
            {
                slug,
                draft_content = content,
                published_content = (LandingPageContent?)null,
                is_published = false,
                has_ever_published = false,
                revision = 1,
                updated_at = DateTimeOffset.UtcNow
            };
            var uri = BuildLandingPagesUri(context.BaseUri!, $"?select={AdminSelect}");
            var response = await SendAsync(
                CreateJsonRequest(HttpMethod.Post, uri, context.ApiKey!, payload, "return=representation"),
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return Failure(MapApiError(response.StatusCode, response.Body));
            }

            var rows = DeserializeRows(response.Body);
            return rows.Count == 0
                ? Failure(LandingPageErrorCodes.RequestFailed)
                : Success(MapAdminRow(rows[0]));
        }
        catch (Exception exception) when (IsRequestException(exception, cancellationToken))
        {
            _logger.LogWarning(exception, "Landing page draft creation failed.");
            return Failure(LandingPageErrorCodes.RequestFailed);
        }
        catch (ArgumentException exception)
        {
            _logger.LogWarning(exception, "Landing page creation response contained invalid data.");
            return Failure(LandingPageErrorCodes.RequestFailed);
        }
    }

    private async Task<LandingPageOperationResult> ResolveMissingConditionalUpdateAsync(
        AdminContext context,
        Guid pageId,
        CancellationToken cancellationToken)
    {
        var current = await FetchAdminRowByIdAsync(context, pageId, cancellationToken);
        return current is null
            ? Failure(LandingPageErrorCodes.PageNotFound)
            : Failure(LandingPageErrorCodes.RevisionConflict);
    }

    private async Task<LandingPageRow?> FetchAdminRowByIdAsync(
        AdminContext context,
        Guid pageId,
        CancellationToken cancellationToken)
    {
        var uri = BuildLandingPagesUri(
            context.BaseUri!,
            $"?select={AdminSelect}&page_id=eq.{pageId:D}&deleted_at=is.null&limit=1");
        var response = await SendAsync(CreateRequest(HttpMethod.Get, uri, context.ApiKey!), cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new LandingPageServiceException(MapApiError(response.StatusCode, response.Body));
        }

        return DeserializeRows(response.Body).FirstOrDefault();
    }

    private LandingPageRecord MapAdminRow(LandingPageRow row)
    {
        if (row.PageId == Guid.Empty || string.IsNullOrWhiteSpace(row.Slug))
        {
            throw new ArgumentException("The landing page database row is incomplete.");
        }

        var draft = _contentValidator.NormalizeAndValidate(row.DraftContent ?? new LandingPageContent());
        LandingPageContent? published = null;
        if (row.PublishedContent is not null)
        {
            published = _contentValidator.NormalizeAndValidate(row.PublishedContent);
        }

        return new LandingPageRecord(
            row.PageId,
            row.Slug,
            draft,
            row.IsPublished,
            published is null || !AreContentEqual(draft, published),
            row.HasEverPublished,
            row.Revision,
            row.UpdatedAt == default ? DateTimeOffset.UtcNow : row.UpdatedAt);
    }

    private async Task<AdminContext> ResolveAdminContextAsync(CancellationToken cancellationToken)
    {
        string? email;
        try
        {
            var authenticationState = await _authenticationStateProvider.GetAuthenticationStateAsync();
            var principal = authenticationState.User;
            if (principal.Identity?.IsAuthenticated != true)
            {
                return AdminContext.Failure(LandingPageErrorCodes.AdminRequired);
            }

            email = principal.FindFirst(ClaimTypes.Email)?.Value ?? principal.FindFirst("email")?.Value;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Landing page admin identity could not be resolved.");
            return AdminContext.Failure(LandingPageErrorCodes.AdminRequired);
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            return AdminContext.Failure(LandingPageErrorCodes.AdminRequired);
        }

        if (!TryBuildSupabaseBaseUri(out var baseUri))
        {
            return AdminContext.Failure(LandingPageErrorCodes.SupabaseNotConfigured);
        }

        var apiKey = ResolveSecretKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return AdminContext.Failure(LandingPageErrorCodes.SupabaseNotConfigured);
        }

        var adminLookupError = await VerifyEnabledAdminAsync(
            baseUri,
            apiKey,
            email.Trim().ToLowerInvariant(),
            cancellationToken);
        if (adminLookupError is not null)
        {
            return AdminContext.Failure(adminLookupError);
        }

        return new AdminContext(baseUri, apiKey, null);
    }

    private async Task<string?> VerifyEnabledAdminAsync(
        Uri baseUri,
        string apiKey,
        string email,
        CancellationToken cancellationToken)
    {
        var uri = new Uri(
            baseUri,
            $"rest/v1/admin_users?select=admin_user_id&email=eq.{Uri.EscapeDataString(email)}&is_enabled=eq.true&limit=1");
        try
        {
            var response = await SendAsync(CreateRequest(HttpMethod.Get, uri, apiKey), cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Landing page admin lookup failed. Status={StatusCode}", (int)response.StatusCode);
                return MapApiError(response.StatusCode, response.Body);
            }

            var rows = JsonSerializer.Deserialize<List<AdminUserRow>>(response.Body, JsonOptions) ?? [];
            return rows.Count > 0 && rows[0].AdminUserId != Guid.Empty
                ? null
                : LandingPageErrorCodes.AdminRequired;
        }
        catch (Exception exception) when (IsRequestException(exception, cancellationToken))
        {
            _logger.LogWarning(exception, "Landing page admin lookup failed unexpectedly.");
            return LandingPageErrorCodes.RequestFailed;
        }
        catch (JsonException exception)
        {
            _logger.LogWarning(exception, "Landing page admin lookup response could not be read.");
            return LandingPageErrorCodes.RequestFailed;
        }
    }

    private static bool AreContentEqual(LandingPageContent left, LandingPageContent right) =>
        string.Equals(
            JsonSerializer.Serialize(left, JsonOptions),
            JsonSerializer.Serialize(right, JsonOptions),
            StringComparison.Ordinal);

    private static LandingPageOperationResult Success(LandingPageRecord page) => new(true, null, page);

    private static LandingPageOperationResult Failure(string errorCode) => new(false, errorCode, null);

    private bool TryBuildSupabaseBaseUri(out Uri baseUri)
    {
        baseUri = null!;
        var url = _options.Url?.Trim();
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate($"{url.TrimEnd('/')}/", UriKind.Absolute, out var resolvedUri))
        {
            return false;
        }

        baseUri = resolvedUri;
        return true;
    }

    private string? ResolveSecretKey() =>
        string.IsNullOrWhiteSpace(_options.SecretKey) ? null : _options.SecretKey.Trim();

    private static Uri BuildLandingPagesUri(Uri baseUri, string query) =>
        new(baseUri, $"rest/v1/landing_pages{query}");

    private static HttpRequestMessage CreateRequest(HttpMethod method, Uri uri, string apiKey)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.TryAddWithoutValidation("apikey", apiKey);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return request;
    }

    private static HttpRequestMessage CreateJsonRequest(
        HttpMethod method,
        Uri uri,
        string apiKey,
        object payload,
        string prefer)
    {
        var request = CreateRequest(method, uri, apiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");
        request.Headers.TryAddWithoutValidation("Prefer", prefer);
        return request;
    }

    private async Task<ApiResponse> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using (request)
        using (var response = await _httpClient.SendAsync(request, cancellationToken))
        {
            return new ApiResponse(
                response.StatusCode,
                await response.Content.ReadAsStringAsync(cancellationToken));
        }
    }

    private static List<LandingPageRow> DeserializeRows(string body) =>
        JsonSerializer.Deserialize<List<LandingPageRow>>(body, JsonOptions) ?? [];

    private static string? NormalizeSlug(string? slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return null;
        }

        var normalized = slug.Trim().ToLowerInvariant();
        return LandingPageSlugRegex().IsMatch(normalized) ? normalized : null;
    }

    private static string MapApiError(HttpStatusCode statusCode, string? responseBody)
    {
        var body = responseBody ?? string.Empty;
        if (body.Contains("PGRST205", StringComparison.OrdinalIgnoreCase) ||
            body.Contains("42P01", StringComparison.OrdinalIgnoreCase) ||
            body.Contains("landing_pages", StringComparison.OrdinalIgnoreCase) &&
            (statusCode == HttpStatusCode.NotFound || body.Contains("does not exist", StringComparison.OrdinalIgnoreCase)))
        {
            return LandingPageErrorCodes.SchemaNotReady;
        }

        if (body.Contains("landing_pages_slug_key", StringComparison.OrdinalIgnoreCase) ||
            body.Contains("23505", StringComparison.OrdinalIgnoreCase))
        {
            return LandingPageErrorCodes.SlugTaken;
        }

        return LandingPageErrorCodes.RequestFailed;
    }

    private static bool IsRequestException(Exception exception, CancellationToken cancellationToken) =>
        exception is HttpRequestException or JsonException ||
        exception is TaskCanceledException && !cancellationToken.IsCancellationRequested;

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex LandingPageSlugRegex();

    private sealed record ApiResponse(HttpStatusCode StatusCode, string Body)
    {
        public bool IsSuccessStatusCode => (int)StatusCode is >= 200 and <= 299;
    }

    private sealed record AdminContext(Uri? BaseUri, string? ApiKey, string? ErrorCode)
    {
        public static AdminContext Failure(string code) => new(null, null, code);
    }

    private sealed class AdminUserRow
    {
        [JsonPropertyName("admin_user_id")]
        public Guid AdminUserId { get; set; }
    }

    private sealed class LandingPageRow
    {
        [JsonPropertyName("page_id")]
        public Guid PageId { get; set; }

        [JsonPropertyName("slug")]
        public string Slug { get; set; } = string.Empty;

        [JsonPropertyName("draft_content")]
        public LandingPageContent? DraftContent { get; set; }

        [JsonPropertyName("published_content")]
        public LandingPageContent? PublishedContent { get; set; }

        [JsonPropertyName("is_published")]
        public bool IsPublished { get; set; }

        [JsonPropertyName("has_ever_published")]
        public bool HasEverPublished { get; set; }

        [JsonPropertyName("revision")]
        public long Revision { get; set; }

        [JsonPropertyName("updated_at")]
        public DateTimeOffset UpdatedAt { get; set; }
    }
}
