using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Shink.Services;

public sealed partial class SupabaseBlogService(
    HttpClient httpClient,
    IOptions<SupabaseOptions> supabaseOptions,
    IMemoryCache memoryCache,
    IBlogContentRenderer blogContentRenderer,
    IUserNotificationService userNotificationService,
    ILogger<SupabaseBlogService> logger) : IBlogCatalogService, IBlogAdminService
{
    private const string PublishedBlogCacheKey = "blog:published:v1";
    private static readonly TimeSpan PublishedBlogCacheDuration = TimeSpan.FromMinutes(2);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient = httpClient;
    private readonly SupabaseOptions _options = supabaseOptions.Value;
    private readonly IMemoryCache _memoryCache = memoryCache;
    private readonly IBlogContentRenderer _blogContentRenderer = blogContentRenderer;
    private readonly IUserNotificationService _userNotificationService = userNotificationService;
    private readonly ILogger<SupabaseBlogService> _logger = logger;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public async Task<IReadOnlyList<BlogPostListItem>> GetPublishedPostsAsync(CancellationToken cancellationToken = default)
    {
        var posts = await GetPublishedSnapshotAsync(cancellationToken);
        return posts
            .Select(MapToListItem)
            .OrderByDescending(post => post.PublishedAt)
            .ThenBy(post => post.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<BlogPostDetail?> FindPublishedPostBySlugAsync(
        string? slug,
        CancellationToken cancellationToken = default)
    {
        var normalizedSlug = NormalizeOptionalSlug(slug);
        if (string.IsNullOrWhiteSpace(normalizedSlug))
        {
            return null;
        }

        var posts = await GetPublishedSnapshotAsync(cancellationToken);
        return posts.FirstOrDefault(post =>
            string.Equals(post.Slug, normalizedSlug, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<AdminBlogDashboardData> GetDashboardAsync(
        string? adminEmail,
        CancellationToken cancellationToken = default)
    {
        if (!await TryResolveAdminContextAsync(adminEmail, cancellationToken))
        {
            return new AdminBlogDashboardData([], [], []);
        }

        if (!TryBuildSupabaseBaseUri(out var baseUri))
        {
            return new AdminBlogDashboardData([], [], []);
        }

        var apiKey = ResolveSecretKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new AdminBlogDashboardData([], [], []);
        }

        var postsTask = FetchBlogPostsAsync(baseUri, apiKey, publicOnly: false, cancellationToken);
        var categoriesTask = FetchCategoriesAsync(baseUri, apiKey, cancellationToken);
        var tagsTask = FetchTagsAsync(baseUri, apiKey, cancellationToken);
        var joinsTask = FetchPostTagsAsync(baseUri, apiKey, cancellationToken);

        await Task.WhenAll(postsTask, categoriesTask, tagsTask, joinsTask);

        var categories = categoriesTask.Result
            .Where(row => row.CategoryId != Guid.Empty)
            .Where(row => !string.IsNullOrWhiteSpace(row.Slug))
            .Where(row => !string.IsNullOrWhiteSpace(row.Name))
            .Select(row => new BlogCategoryItem(
                row.CategoryId,
                row.Slug.Trim(),
                row.Name.Trim()))
            .OrderBy(category => category.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var tags = tagsTask.Result
            .Where(row => row.TagId != Guid.Empty)
            .Where(row => !string.IsNullOrWhiteSpace(row.Slug))
            .Where(row => !string.IsNullOrWhiteSpace(row.Name))
            .Select(row => new BlogTagItem(
                row.TagId,
                row.Slug.Trim(),
                row.Name.Trim()))
            .OrderBy(tag => tag.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var categoriesById = categories.ToDictionary(category => category.CategoryId);
        var tagsById = tags.ToDictionary(tag => tag.TagId);
        var tagsByPostId = joinsTask.Result
            .Where(row => row.PostId != Guid.Empty && row.TagId != Guid.Empty)
            .GroupBy(row => row.PostId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(row => tagsById.TryGetValue(row.TagId, out var tag) ? tag : null)
                    .Where(static tag => tag is not null)
                    .Cast<BlogTagItem>()
                    .DistinctBy(tag => tag.TagId)
                    .OrderBy(tag => tag.Name, StringComparer.OrdinalIgnoreCase)
                    .ToArray());

        var posts = postsTask.Result
            .Where(row => row.PostId != Guid.Empty)
            .Where(row => !string.IsNullOrWhiteSpace(row.Slug))
            .Where(row => !string.IsNullOrWhiteSpace(row.Title))
            .Select(row =>
            {
                categoriesById.TryGetValue(row.CategoryId ?? Guid.Empty, out var category);
                tagsByPostId.TryGetValue(row.PostId, out var postTags);
                var normalizedMarkdown = NormalizeMarkdown(row.ContentMarkdown);
                var plainTextContent = ResolvePlainTextContent(row.PlainTextContent, normalizedMarkdown);

                return new AdminBlogPostRecord(
                    PostId: row.PostId,
                    Slug: row.Slug.Trim(),
                    Title: row.Title.Trim(),
                    Summary: ResolveSummary(row.Summary, plainTextContent),
                    PlainTextContent: plainTextContent,
                    ContentMarkdown: normalizedMarkdown,
                    FeaturedImageUrl: NormalizePublicImageUrl(row.FeaturedImageUrl),
                    AuthorName: NormalizeOptionalText(row.AuthorName, 120),
                    CategoryId: row.CategoryId,
                    CategoryName: category?.Name,
                    Tags: postTags ?? [],
                    IsPublished: row.IsPublished,
                    PublishedAt: row.PublishedAt,
                    CreatedAt: row.CreatedAt,
                    UpdatedAt: row.UpdatedAt,
                    SeoTitle: NormalizeOptionalText(row.SeoTitle, 180),
                    SeoDescription: NormalizeOptionalText(row.SeoDescription, 320));
            })
            .OrderByDescending(post => post.UpdatedAt)
            .ThenByDescending(post => post.PublishedAt ?? DateTimeOffset.MinValue)
            .ThenBy(post => post.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new AdminBlogDashboardData(posts, categories, tags);
    }

    public async Task<AdminOperationResult> SavePostAsync(
        string? adminEmail,
        AdminBlogPostSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!await TryResolveAdminContextAsync(adminEmail, cancellationToken))
        {
            return new AdminOperationResult(false, "Jy het nie admin toegang nie.");
        }

        if (!TryBuildSupabaseBaseUri(out var baseUri))
        {
            return new AdminOperationResult(false, "Supabase URL is nog nie opgestel nie.");
        }

        var apiKey = ResolveSecretKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new AdminOperationResult(false, "Supabase SecretKey is nog nie opgestel nie.");
        }

        var normalizedTitle = NormalizeOptionalText(request.Title, 180);
        if (string.IsNullOrWhiteSpace(normalizedTitle))
        {
            return new AdminOperationResult(false, "Blog titel is verpligtend.");
        }

        var normalizedSlug = NormalizeSlugCandidate(request.Slug, normalizedTitle);
        if (!BlogSlugRegex().IsMatch(normalizedSlug))
        {
            return new AdminOperationResult(false, "Die blog slug is ongeldig.");
        }

        var normalizedContentMarkdown = NormalizeMarkdown(request.ContentMarkdown);
        if (string.IsNullOrWhiteSpace(normalizedContentMarkdown))
        {
            return new AdminOperationResult(false, "Blog inhoud is verpligtend.");
        }

        var contentHtml = _blogContentRenderer.RenderHtml(normalizedContentMarkdown);
        var plainTextContent = _blogContentRenderer.ConvertToPlainText(normalizedContentMarkdown);
        var summary = ResolveSummary(
            NormalizeOptionalText(request.Summary, 600),
            plainTextContent,
            _blogContentRenderer.BuildExcerpt(normalizedContentMarkdown, 240));
        var seoTitle = NormalizeOptionalText(request.SeoTitle, 180);
        var seoDescription = NormalizeOptionalText(request.SeoDescription, 320) ?? summary;
        var authorName = NormalizeOptionalText(request.AuthorName, 120);
        var featuredImageUrl = NormalizePublicImageUrl(request.FeaturedImageUrl);

        DateTimeOffset? publishedAt = request.PublishedAt;
        if (request.IsPublished && publishedAt is null)
        {
            publishedAt = DateTimeOffset.UtcNow;
        }

        BlogPostRow? existingPost = null;
        if (request.PostId is Guid currentPostId && currentPostId != Guid.Empty)
        {
            existingPost = await FetchBlogPostByIdAsync(baseUri, apiKey, currentPostId, cancellationToken);
        }

        var shouldCreatePublishedBlogNotifications =
            IsBlogPostPubliclyPublished(request.IsPublished, publishedAt) &&
            !IsBlogPostPubliclyPublished(existingPost);

        try
        {
            var categoryId = await UpsertCategoryAsync(baseUri, apiKey, request.CategoryName, cancellationToken);
            var tags = await UpsertTagsAsync(baseUri, apiKey, request.TagNames, cancellationToken);
            var payload = new Dictionary<string, object?>
            {
                ["slug"] = normalizedSlug,
                ["title"] = normalizedTitle,
                ["summary"] = summary,
                ["plain_text_content"] = plainTextContent,
                ["content_markdown"] = normalizedContentMarkdown,
                ["content_html"] = contentHtml,
                ["featured_image_url"] = featuredImageUrl,
                ["author_name"] = authorName,
                ["category_id"] = categoryId,
                ["is_published"] = request.IsPublished,
                ["published_at"] = publishedAt?.UtcDateTime,
                ["seo_title"] = seoTitle,
                ["seo_description"] = seoDescription
            };

            Guid postId;
            if (request.PostId is Guid existingPostId && existingPostId != Guid.Empty)
            {
                var updateUri = new Uri(baseUri, $"rest/v1/blog_posts?post_id=eq.{Uri.EscapeDataString(existingPostId.ToString("D"))}");
                using var updateRequest = CreateJsonRequest(new HttpMethod("PATCH"), updateUri, apiKey, payload, "return=minimal");
                using var updateResponse = await _httpClient.SendAsync(updateRequest, cancellationToken);
                if (!updateResponse.IsSuccessStatusCode)
                {
                    var body = await updateResponse.Content.ReadAsStringAsync(cancellationToken);
                    if (ContainsDuplicateBlogSlugViolation(body))
                    {
                        return new AdminOperationResult(false, "Die blog slug bestaan reeds.");
                    }

                    _logger.LogWarning(
                        "Blog post update failed. post_id={PostId} Status={StatusCode} Body={Body}",
                        existingPostId,
                        (int)updateResponse.StatusCode,
                        body);
                    return new AdminOperationResult(false, "Kon nie blog pos nou opdateer nie.");
                }

                postId = existingPostId;
            }
            else
            {
                var insertUri = new Uri(baseUri, "rest/v1/blog_posts?select=post_id");
                using var insertRequest = CreateJsonRequest(HttpMethod.Post, insertUri, apiKey, payload, "return=representation");
                using var insertResponse = await _httpClient.SendAsync(insertRequest, cancellationToken);
                var responseBody = await insertResponse.Content.ReadAsStringAsync(cancellationToken);
                if (!insertResponse.IsSuccessStatusCode)
                {
                    if (ContainsDuplicateBlogSlugViolation(responseBody))
                    {
                        return new AdminOperationResult(false, "Die blog slug bestaan reeds.");
                    }

                    _logger.LogWarning(
                        "Blog post create failed. slug={Slug} Status={StatusCode} Body={Body}",
                        normalizedSlug,
                        (int)insertResponse.StatusCode,
                        responseBody);
                    return new AdminOperationResult(false, "Kon nie blog pos nou skep nie.");
                }

                postId = TryReadFirstGuidProperty(responseBody, "post_id") ?? Guid.Empty;
                if (postId == Guid.Empty)
                {
                    return new AdminOperationResult(false, "Kon nie blog pos nou skep nie.");
                }
            }

            var syncTagsResult = await SyncPostTagsAsync(baseUri, apiKey, postId, tags.Select(tag => tag.TagId), cancellationToken);
            if (!syncTagsResult)
            {
                return new AdminOperationResult(false, "Kon nie blog etikette nou stoor nie.");
            }

            InvalidatePublishedBlogCache();

            if (shouldCreatePublishedBlogNotifications)
            {
                await _userNotificationService.CreatePublishedBlogNotificationsAsync(
                    new PublishedBlogNotificationRequest(
                        postId,
                        normalizedSlug,
                        normalizedTitle,
                        summary,
                        featuredImageUrl),
                    cancellationToken);
            }

            return new AdminOperationResult(true, EntityId: postId);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(exception, "Blog save failed unexpectedly.");
            return new AdminOperationResult(false, "Kon nie blog pos nou stoor nie.");
        }
    }

    public async Task<AdminOperationResult> DeletePostAsync(
        string? adminEmail,
        Guid postId,
        CancellationToken cancellationToken = default)
    {
        if (!await TryResolveAdminContextAsync(adminEmail, cancellationToken))
        {
            return new AdminOperationResult(false, "Jy het nie admin toegang nie.");
        }

        if (postId == Guid.Empty)
        {
            return new AdminOperationResult(false, "Kies asseblief 'n geldige blog pos.");
        }

        if (!TryBuildSupabaseBaseUri(out var baseUri))
        {
            return new AdminOperationResult(false, "Supabase URL is nog nie opgestel nie.");
        }

        var apiKey = ResolveSecretKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new AdminOperationResult(false, "Supabase SecretKey is nog nie opgestel nie.");
        }

        try
        {
            var uri = new Uri(baseUri, $"rest/v1/blog_posts?post_id=eq.{Uri.EscapeDataString(postId.ToString("D"))}");
            using var request = CreateRequest(HttpMethod.Delete, uri, apiKey);
            request.Headers.TryAddWithoutValidation("Prefer", "return=minimal");

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "Blog post delete failed. post_id={PostId} Status={StatusCode} Body={Body}",
                    postId,
                    (int)response.StatusCode,
                    body);
                return new AdminOperationResult(false, "Kon nie blog pos nou verwyder nie.");
            }

            InvalidatePublishedBlogCache();
            return new AdminOperationResult(true);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(exception, "Blog delete failed unexpectedly.");
            return new AdminOperationResult(false, "Kon nie blog pos nou verwyder nie.");
        }
    }

    private async Task<IReadOnlyList<BlogPostDetail>> GetPublishedSnapshotAsync(CancellationToken cancellationToken)
    {
        if (_memoryCache.TryGetValue(PublishedBlogCacheKey, out IReadOnlyList<BlogPostDetail>? cachedPosts) &&
            cachedPosts is not null)
        {
            return cachedPosts;
        }

        await _refreshLock.WaitAsync(CancellationToken.None);
        try
        {
            if (_memoryCache.TryGetValue(PublishedBlogCacheKey, out cachedPosts) &&
                cachedPosts is not null)
            {
                return cachedPosts;
            }

            var posts = await BuildPublishedSnapshotAsync(CancellationToken.None);
            _memoryCache.Set(PublishedBlogCacheKey, posts, PublishedBlogCacheDuration);
            return posts;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private async Task<IReadOnlyList<BlogPostDetail>> BuildPublishedSnapshotAsync(CancellationToken cancellationToken)
    {
        if (!TryBuildSupabaseBaseUri(out var baseUri))
        {
            _logger.LogWarning("Supabase blog lookup skipped: URL is not configured.");
            return Array.Empty<BlogPostDetail>();
        }

        var apiKey = ResolveReadApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("Supabase blog lookup skipped: PublishableKey is not configured.");
            return Array.Empty<BlogPostDetail>();
        }

        var postsTask = FetchBlogPostsAsync(baseUri, apiKey, publicOnly: true, cancellationToken);
        var categoriesTask = FetchCategoriesAsync(baseUri, apiKey, cancellationToken);
        var tagsTask = FetchTagsAsync(baseUri, apiKey, cancellationToken);
        var joinsTask = FetchPostTagsAsync(baseUri, apiKey, cancellationToken);

        await Task.WhenAll(postsTask, categoriesTask, tagsTask, joinsTask);

        return BuildBlogDetails(postsTask.Result, categoriesTask.Result, tagsTask.Result, joinsTask.Result)
            .Where(post => post.PublishedAt <= DateTimeOffset.UtcNow)
            .OrderByDescending(post => post.PublishedAt)
            .ThenBy(post => post.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private IReadOnlyList<BlogPostDetail> BuildBlogDetails(
        IReadOnlyList<BlogPostRow> postRows,
        IReadOnlyList<BlogCategoryRow> categoryRows,
        IReadOnlyList<BlogTagRow> tagRows,
        IReadOnlyList<BlogPostTagRow> joinRows)
    {
        var categoriesById = categoryRows
            .Where(row => row.CategoryId != Guid.Empty)
            .Where(row => !string.IsNullOrWhiteSpace(row.Slug))
            .Where(row => !string.IsNullOrWhiteSpace(row.Name))
            .Select(row => new BlogCategoryItem(
                row.CategoryId,
                row.Slug.Trim(),
                row.Name.Trim()))
            .ToDictionary(category => category.CategoryId);

        var tagsById = tagRows
            .Where(row => row.TagId != Guid.Empty)
            .Where(row => !string.IsNullOrWhiteSpace(row.Slug))
            .Where(row => !string.IsNullOrWhiteSpace(row.Name))
            .Select(row => new BlogTagItem(
                row.TagId,
                row.Slug.Trim(),
                row.Name.Trim()))
            .ToDictionary(tag => tag.TagId);

        var tagsByPostId = joinRows
            .Where(row => row.PostId != Guid.Empty && row.TagId != Guid.Empty)
            .GroupBy(row => row.PostId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(row => tagsById.TryGetValue(row.TagId, out var tag) ? tag : null)
                    .Where(static tag => tag is not null)
                    .Cast<BlogTagItem>()
                    .DistinctBy(tag => tag.TagId)
                    .OrderBy(tag => tag.Name, StringComparer.OrdinalIgnoreCase)
                    .ToArray());

        return postRows
            .Where(row => row.PostId != Guid.Empty)
            .Where(row => !string.IsNullOrWhiteSpace(row.Slug))
            .Where(row => !string.IsNullOrWhiteSpace(row.Title))
            .Where(row => row.PublishedAt is not null)
            .Select(row =>
            {
                categoriesById.TryGetValue(row.CategoryId ?? Guid.Empty, out var category);
                tagsByPostId.TryGetValue(row.PostId, out var tags);
                var normalizedMarkdown = NormalizeMarkdown(row.ContentMarkdown);
                var plainTextContent = ResolvePlainTextContent(row.PlainTextContent, normalizedMarkdown);

                return new BlogPostDetail(
                    PostId: row.PostId,
                    Slug: row.Slug.Trim(),
                    Title: row.Title.Trim(),
                    Summary: ResolveSummary(row.Summary, plainTextContent),
                    PlainTextContent: plainTextContent,
                    ContentMarkdown: normalizedMarkdown,
                    ContentHtml: ResolveContentHtml(row.ContentHtml, normalizedMarkdown),
                    FeaturedImageUrl: NormalizePublicImageUrl(row.FeaturedImageUrl),
                    AuthorName: NormalizeOptionalText(row.AuthorName, 120),
                    PublishedAt: row.PublishedAt!.Value,
                    CreatedAt: row.CreatedAt,
                    UpdatedAt: row.UpdatedAt,
                    Category: category,
                    Tags: tags ?? [],
                    SeoTitle: NormalizeOptionalText(row.SeoTitle, 180),
                    SeoDescription: NormalizeOptionalText(row.SeoDescription, 320));
            })
            .ToArray();
    }

    private static BlogPostListItem MapToListItem(BlogPostDetail post) =>
        new(
            PostId: post.PostId,
            Slug: post.Slug,
            Title: post.Title,
            Summary: post.Summary,
            PlainTextContent: post.PlainTextContent,
            FeaturedImageUrl: post.FeaturedImageUrl,
            AuthorName: post.AuthorName,
            PublishedAt: post.PublishedAt,
            CreatedAt: post.CreatedAt,
            UpdatedAt: post.UpdatedAt,
            Category: post.Category,
            Tags: post.Tags,
            SeoTitle: post.SeoTitle,
            SeoDescription: post.SeoDescription);

    private async Task<Guid?> UpsertCategoryAsync(
        Uri baseUri,
        string apiKey,
        string? categoryName,
        CancellationToken cancellationToken)
    {
        var normalizedName = NormalizeOptionalText(categoryName, 120);
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            return null;
        }

        var payload = new[]
        {
            new Dictionary<string, object?>
            {
                ["slug"] = NormalizeSlugCandidate(null, normalizedName),
                ["name"] = normalizedName
            }
        };

        var uri = new Uri(baseUri, "rest/v1/blog_categories?on_conflict=slug&select=category_id,slug,name");
        using var request = CreateJsonRequest(HttpMethod.Post, uri, apiKey, payload, "resolution=merge-duplicates,return=representation");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "Blog category upsert failed. name={CategoryName} Status={StatusCode} Body={Body}",
                normalizedName,
                (int)response.StatusCode,
                body);
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var rows = await JsonSerializer.DeserializeAsync<List<BlogCategoryRow>>(stream, JsonOptions, cancellationToken)
            ?? [];
        return rows.FirstOrDefault()?.CategoryId;
    }

    private async Task<IReadOnlyList<BlogTagItem>> UpsertTagsAsync(
        Uri baseUri,
        string apiKey,
        IReadOnlyList<string>? tagNames,
        CancellationToken cancellationToken)
    {
        var normalizedTags = NormalizeTagInputs(tagNames);
        if (normalizedTags.Count == 0)
        {
            return Array.Empty<BlogTagItem>();
        }

        var payload = normalizedTags
            .Select(tag => new Dictionary<string, object?>
            {
                ["slug"] = tag.Slug,
                ["name"] = tag.Name
            })
            .ToArray();

        var uri = new Uri(baseUri, "rest/v1/blog_tags?on_conflict=slug&select=tag_id,slug,name");
        using var request = CreateJsonRequest(HttpMethod.Post, uri, apiKey, payload, "resolution=merge-duplicates,return=representation");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "Blog tag upsert failed. Status={StatusCode} Body={Body}",
                (int)response.StatusCode,
                body);
            return Array.Empty<BlogTagItem>();
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var rows = await JsonSerializer.DeserializeAsync<List<BlogTagRow>>(stream, JsonOptions, cancellationToken)
            ?? [];

        return rows
            .Where(row => row.TagId != Guid.Empty)
            .Where(row => !string.IsNullOrWhiteSpace(row.Slug))
            .Where(row => !string.IsNullOrWhiteSpace(row.Name))
            .Select(row => new BlogTagItem(row.TagId, row.Slug.Trim(), row.Name.Trim()))
            .OrderBy(tag => tag.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task<bool> SyncPostTagsAsync(
        Uri baseUri,
        string apiKey,
        Guid postId,
        IEnumerable<Guid> tagIds,
        CancellationToken cancellationToken)
    {
        var deleteUri = new Uri(baseUri, $"rest/v1/blog_post_tags?post_id=eq.{Uri.EscapeDataString(postId.ToString("D"))}");
        using (var deleteRequest = CreateRequest(HttpMethod.Delete, deleteUri, apiKey))
        {
            deleteRequest.Headers.TryAddWithoutValidation("Prefer", "return=minimal");
            using var deleteResponse = await _httpClient.SendAsync(deleteRequest, cancellationToken);
            if (!deleteResponse.IsSuccessStatusCode)
            {
                var body = await deleteResponse.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "Blog post tags delete failed. post_id={PostId} Status={StatusCode} Body={Body}",
                    postId,
                    (int)deleteResponse.StatusCode,
                    body);
                return false;
            }
        }

        var distinctTagIds = tagIds
            .Where(tagId => tagId != Guid.Empty)
            .Distinct()
            .ToArray();
        if (distinctTagIds.Length == 0)
        {
            return true;
        }

        var payload = distinctTagIds
            .Select(tagId => new Dictionary<string, object?>
            {
                ["post_id"] = postId,
                ["tag_id"] = tagId
            })
            .ToArray();

        var insertUri = new Uri(baseUri, "rest/v1/blog_post_tags");
        using var insertRequest = CreateJsonRequest(HttpMethod.Post, insertUri, apiKey, payload, "return=minimal");
        using var insertResponse = await _httpClient.SendAsync(insertRequest, cancellationToken);
        if (!insertResponse.IsSuccessStatusCode)
        {
            var body = await insertResponse.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "Blog post tags insert failed. post_id={PostId} Status={StatusCode} Body={Body}",
                postId,
                (int)insertResponse.StatusCode,
                body);
            return false;
        }

        return true;
    }

    private async Task<IReadOnlyList<BlogPostRow>> FetchBlogPostsAsync(
        Uri baseUri,
        string apiKey,
        bool publicOnly,
        CancellationToken cancellationToken)
    {
        var queryBuilder = new StringBuilder(
            "rest/v1/blog_posts" +
            "?select=post_id,slug,title,summary,plain_text_content,content_markdown,content_html,featured_image_url,author_name,category_id,is_published,published_at,created_at,updated_at,seo_title,seo_description");

        if (publicOnly)
        {
            queryBuilder.Append("&is_published=eq.true");
            queryBuilder.Append("&published_at=lte.");
            queryBuilder.Append(Uri.EscapeDataString(DateTimeOffset.UtcNow.ToString("O")));
        }

        queryBuilder.Append("&order=published_at.desc.nullslast");
        queryBuilder.Append("&order=updated_at.desc");
        queryBuilder.Append("&order=title.asc");
        queryBuilder.Append("&limit=500");

        return await FetchRowsAsync<BlogPostRow>(new Uri(baseUri, queryBuilder.ToString()), apiKey, cancellationToken);
    }

    private async Task<IReadOnlyList<BlogCategoryRow>> FetchCategoriesAsync(
        Uri baseUri,
        string apiKey,
        CancellationToken cancellationToken)
    {
        var uri = new Uri(
            baseUri,
            "rest/v1/blog_categories" +
            "?select=category_id,slug,name" +
            "&order=name.asc" +
            "&limit=200");

        return await FetchRowsAsync<BlogCategoryRow>(uri, apiKey, cancellationToken);
    }

    private async Task<IReadOnlyList<BlogTagRow>> FetchTagsAsync(
        Uri baseUri,
        string apiKey,
        CancellationToken cancellationToken)
    {
        var uri = new Uri(
            baseUri,
            "rest/v1/blog_tags" +
            "?select=tag_id,slug,name" +
            "&order=name.asc" +
            "&limit=500");

        return await FetchRowsAsync<BlogTagRow>(uri, apiKey, cancellationToken);
    }

    private async Task<IReadOnlyList<BlogPostTagRow>> FetchPostTagsAsync(
        Uri baseUri,
        string apiKey,
        CancellationToken cancellationToken)
    {
        var uri = new Uri(
            baseUri,
            "rest/v1/blog_post_tags" +
            "?select=post_id,tag_id" +
            "&limit=5000");

        return await FetchRowsAsync<BlogPostTagRow>(uri, apiKey, cancellationToken);
    }

    private async Task<BlogPostRow?> FetchBlogPostByIdAsync(
        Uri baseUri,
        string apiKey,
        Guid postId,
        CancellationToken cancellationToken)
    {
        if (postId == Guid.Empty)
        {
            return null;
        }

        var escapedPostId = Uri.EscapeDataString(postId.ToString("D"));
        var queryBuilder = new StringBuilder(
            "rest/v1/blog_posts" +
            "?select=post_id,is_published,published_at" +
            $"&post_id=eq.{escapedPostId}" +
            "&limit=1");

        var rows = await FetchRowsAsync<BlogPostRow>(new Uri(baseUri, queryBuilder.ToString()), apiKey, cancellationToken);
        return rows
            .Where(row => row.PostId != Guid.Empty)
            .FirstOrDefault();
    }

    private async Task<IReadOnlyList<T>> FetchRowsAsync<T>(Uri uri, string apiKey, CancellationToken cancellationToken)
    {
        try
        {
            using var request = CreateRequest(HttpMethod.Get, uri, apiKey);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    return Array.Empty<T>();
                }

                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "Supabase blog fetch failed. uri={Uri} Status={StatusCode} Body={Body}",
                    uri,
                    (int)response.StatusCode,
                    responseBody);
                return Array.Empty<T>();
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return await JsonSerializer.DeserializeAsync<List<T>>(stream, JsonOptions, cancellationToken)
                ?? [];
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(exception, "Supabase blog fetch failed unexpectedly. uri={Uri}", uri);
            return Array.Empty<T>();
        }
    }

    private async Task<bool> TryResolveAdminContextAsync(string? adminEmail, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(adminEmail))
        {
            return false;
        }

        if (!TryBuildSupabaseBaseUri(out var baseUri))
        {
            return false;
        }

        var apiKey = ResolveSecretKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return false;
        }

        var normalizedEmail = adminEmail.Trim().ToLowerInvariant();
        var uri = new Uri(
            baseUri,
            $"rest/v1/admin_users?select=admin_user_id&email=eq.{Uri.EscapeDataString(normalizedEmail)}&is_enabled=eq.true&limit=1");

        try
        {
            using var request = CreateRequest(HttpMethod.Get, uri, apiKey);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "Blog admin lookup failed. email={Email} Status={StatusCode} Body={Body}",
                    normalizedEmail,
                    (int)response.StatusCode,
                    responseBody);
                return false;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var rows = await JsonSerializer.DeserializeAsync<List<AdminUserRow>>(stream, JsonOptions, cancellationToken)
                ?? [];
            return rows.Count > 0;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(exception, "Blog admin lookup failed unexpectedly for {Email}.", normalizedEmail);
            return false;
        }
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, Uri uri, string apiKey)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.TryAddWithoutValidation("apikey", apiKey);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return request;
    }

    private HttpRequestMessage CreateJsonRequest(
        HttpMethod method,
        Uri uri,
        string apiKey,
        object payload,
        string? prefer = null)
    {
        var request = CreateRequest(method, uri, apiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");
        if (!string.IsNullOrWhiteSpace(prefer))
        {
            request.Headers.TryAddWithoutValidation("Prefer", prefer);
        }

        return request;
    }

    private bool TryBuildSupabaseBaseUri(out Uri baseUri)
    {
        baseUri = null!;
        var url = _options.Url?.Trim();
        if (string.IsNullOrWhiteSpace(url) ||
            !Uri.TryCreate(url, UriKind.Absolute, out var resolvedUri))
        {
            return false;
        }

        baseUri = resolvedUri;
        return true;
    }

    private string? ResolveReadApiKey() =>
        string.IsNullOrWhiteSpace(_options.PublishableKey)
            ? null
            : _options.PublishableKey.Trim();

    private string? ResolveSecretKey() =>
        string.IsNullOrWhiteSpace(_options.SecretKey)
            ? null
            : _options.SecretKey.Trim();

    private void InvalidatePublishedBlogCache()
    {
        _memoryCache.Remove(PublishedBlogCacheKey);
    }

    private string ResolveContentHtml(string? configuredHtml, string markdown)
    {
        var normalizedHtml = NormalizeOptionalText(configuredHtml, 200_000);
        if (!string.IsNullOrWhiteSpace(normalizedHtml))
        {
            return normalizedHtml;
        }

        return _blogContentRenderer.RenderHtml(markdown);
    }

    private string ResolvePlainTextContent(string? configuredPlainText, string markdown)
    {
        var normalizedPlainText = NormalizeOptionalText(configuredPlainText, 50_000);
        if (!string.IsNullOrWhiteSpace(normalizedPlainText))
        {
            return normalizedPlainText;
        }

        return _blogContentRenderer.ConvertToPlainText(markdown);
    }

    private static string ResolveSummary(string? configuredSummary, string? plainTextContent, string? fallbackSummary = null)
    {
        var normalizedSummary = NormalizeOptionalText(configuredSummary, 600);
        if (!string.IsNullOrWhiteSpace(normalizedSummary))
        {
            return normalizedSummary;
        }

        normalizedSummary = NormalizeOptionalText(fallbackSummary, 600);
        if (!string.IsNullOrWhiteSpace(normalizedSummary))
        {
            return normalizedSummary;
        }

        normalizedSummary = NormalizeOptionalText(plainTextContent, 240);
        if (!string.IsNullOrWhiteSpace(normalizedSummary))
        {
            if (normalizedSummary.Length <= 240)
            {
                return normalizedSummary;
            }

            var candidate = normalizedSummary[..240];
            var lastSpace = candidate.LastIndexOf(' ');
            return (lastSpace > 80 ? candidate[..lastSpace] : candidate).Trim();
        }

        return string.Empty;
    }

    private static string? NormalizeOptionalText(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length <= maxLength
            ? normalized
            : normalized[..maxLength].TrimEnd();
    }

    private static string NormalizeMarkdown(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();

    private static string? NormalizeOptionalSlug(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var candidate = value.Trim().ToLowerInvariant();
        return BlogSlugRegex().IsMatch(candidate) ? candidate : null;
    }

    private static string NormalizeSlugCandidate(string? slug, string fallbackTitle)
    {
        var source = string.IsNullOrWhiteSpace(slug) ? fallbackTitle : slug;
        var normalized = source.Trim().ToLowerInvariant();
        normalized = NonSlugCharacterRegex().Replace(normalized, "-");
        normalized = MultiDashRegex().Replace(normalized, "-");
        return normalized.Trim('-');
    }

    private static string? NormalizePublicImageUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var candidate = value.Trim();
        if (candidate.StartsWith("~/", StringComparison.Ordinal))
        {
            candidate = $"/{candidate[2..]}";
        }

        if (candidate.StartsWith("/", StringComparison.Ordinal))
        {
            return candidate;
        }

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var absoluteUri))
        {
            return null;
        }

        return absoluteUri.Scheme is "http" or "https"
            ? absoluteUri.ToString()
            : null;
    }

    private static IReadOnlyList<TagInput> NormalizeTagInputs(IEnumerable<string>? tagNames)
    {
        var distinctTags = new Dictionary<string, TagInput>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in tagNames ?? Array.Empty<string>())
        {
            var normalizedName = NormalizeOptionalText(candidate, 80);
            if (string.IsNullOrWhiteSpace(normalizedName))
            {
                continue;
            }

            var slug = NormalizeSlugCandidate(null, normalizedName);
            if (!BlogSlugRegex().IsMatch(slug) || distinctTags.ContainsKey(slug))
            {
                continue;
            }

            distinctTags[slug] = new TagInput(slug, normalizedName);
        }

        return distinctTags.Values.ToArray();
    }

    private static Guid? TryReadFirstGuidProperty(string? json, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array ||
                document.RootElement.GetArrayLength() == 0)
            {
                return null;
            }

            var first = document.RootElement[0];
            if (first.ValueKind != JsonValueKind.Object ||
                !first.TryGetProperty(propertyName, out var property) ||
                property.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            return Guid.TryParse(property.GetString(), out var parsedGuid)
                ? parsedGuid
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool ContainsDuplicateBlogSlugViolation(string? responseBody) =>
        !string.IsNullOrWhiteSpace(responseBody) &&
        responseBody.Contains("blog_posts_slug_key", StringComparison.OrdinalIgnoreCase);

    private static bool IsBlogPostPubliclyPublished(bool isPublished, DateTimeOffset? publishedAt) =>
        isPublished &&
        publishedAt is DateTimeOffset publishAt &&
        publishAt <= DateTimeOffset.UtcNow;

    private static bool IsBlogPostPubliclyPublished(BlogPostRow? post) =>
        post is not null &&
        IsBlogPostPubliclyPublished(post.IsPublished, post.PublishedAt);

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex BlogSlugRegex();

    [GeneratedRegex("[^a-z0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex NonSlugCharacterRegex();

    [GeneratedRegex("-{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex MultiDashRegex();

    private sealed record TagInput(string Slug, string Name);

    private sealed class AdminUserRow
    {
        [JsonPropertyName("admin_user_id")]
        public Guid AdminUserId { get; set; }
    }

    private sealed class BlogPostRow
    {
        [JsonPropertyName("post_id")]
        public Guid PostId { get; set; }

        [JsonPropertyName("slug")]
        public string Slug { get; set; } = string.Empty;

        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("summary")]
        public string? Summary { get; set; }

        [JsonPropertyName("plain_text_content")]
        public string? PlainTextContent { get; set; }

        [JsonPropertyName("content_markdown")]
        public string? ContentMarkdown { get; set; }

        [JsonPropertyName("content_html")]
        public string? ContentHtml { get; set; }

        [JsonPropertyName("featured_image_url")]
        public string? FeaturedImageUrl { get; set; }

        [JsonPropertyName("author_name")]
        public string? AuthorName { get; set; }

        [JsonPropertyName("category_id")]
        public Guid? CategoryId { get; set; }

        [JsonPropertyName("is_published")]
        public bool IsPublished { get; set; }

        [JsonPropertyName("published_at")]
        public DateTimeOffset? PublishedAt { get; set; }

        [JsonPropertyName("created_at")]
        public DateTimeOffset CreatedAt { get; set; }

        [JsonPropertyName("updated_at")]
        public DateTimeOffset UpdatedAt { get; set; }

        [JsonPropertyName("seo_title")]
        public string? SeoTitle { get; set; }

        [JsonPropertyName("seo_description")]
        public string? SeoDescription { get; set; }
    }

    private sealed class BlogCategoryRow
    {
        [JsonPropertyName("category_id")]
        public Guid CategoryId { get; set; }

        [JsonPropertyName("slug")]
        public string Slug { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
    }

    private sealed class BlogTagRow
    {
        [JsonPropertyName("tag_id")]
        public Guid TagId { get; set; }

        [JsonPropertyName("slug")]
        public string Slug { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
    }

    private sealed class BlogPostTagRow
    {
        [JsonPropertyName("post_id")]
        public Guid PostId { get; set; }

        [JsonPropertyName("tag_id")]
        public Guid TagId { get; set; }
    }
}
