namespace Shink.Services;

public interface IBlogCatalogService
{
    Task<IReadOnlyList<BlogPostListItem>> GetPublishedPostsAsync(CancellationToken cancellationToken = default);

    Task<BlogPostDetail?> FindPublishedPostBySlugAsync(
        string? slug,
        CancellationToken cancellationToken = default);
}

public interface IBlogAdminService
{
    Task<AdminBlogDashboardData> GetDashboardAsync(
        string? adminEmail,
        CancellationToken cancellationToken = default);

    Task<AdminOperationResult> SavePostAsync(
        string? adminEmail,
        AdminBlogPostSaveRequest request,
        CancellationToken cancellationToken = default);

    Task<AdminOperationResult> DeletePostAsync(
        string? adminEmail,
        Guid postId,
        CancellationToken cancellationToken = default);
}

public sealed record BlogCategoryItem(
    Guid CategoryId,
    string Slug,
    string Name);

public sealed record BlogTagItem(
    Guid TagId,
    string Slug,
    string Name);

public sealed record BlogPostListItem(
    Guid PostId,
    string Slug,
    string Title,
    string Summary,
    string PlainTextContent,
    string? FeaturedImageUrl,
    string? AuthorName,
    DateTimeOffset PublishedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    BlogCategoryItem? Category,
    IReadOnlyList<BlogTagItem> Tags,
    string? SeoTitle,
    string? SeoDescription);

public sealed record BlogPostDetail(
    Guid PostId,
    string Slug,
    string Title,
    string Summary,
    string PlainTextContent,
    string ContentMarkdown,
    string ContentHtml,
    string? FeaturedImageUrl,
    string? AuthorName,
    DateTimeOffset PublishedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    BlogCategoryItem? Category,
    IReadOnlyList<BlogTagItem> Tags,
    string? SeoTitle,
    string? SeoDescription);

public sealed record AdminBlogDashboardData(
    IReadOnlyList<AdminBlogPostRecord> Posts,
    IReadOnlyList<BlogCategoryItem> Categories,
    IReadOnlyList<BlogTagItem> Tags);

public sealed record AdminBlogPostRecord(
    Guid PostId,
    string Slug,
    string Title,
    string Summary,
    string PlainTextContent,
    string ContentMarkdown,
    string? FeaturedImageUrl,
    string? AuthorName,
    Guid? CategoryId,
    string? CategoryName,
    IReadOnlyList<BlogTagItem> Tags,
    bool IsPublished,
    DateTimeOffset? PublishedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? SeoTitle,
    string? SeoDescription);

public sealed record AdminBlogPostSaveRequest(
    Guid? PostId,
    string Title,
    string? Slug,
    string? Summary,
    string ContentMarkdown,
    string? FeaturedImageUrl,
    string? AuthorName,
    string? CategoryName,
    IReadOnlyList<string>? TagNames,
    bool IsPublished,
    DateTimeOffset? PublishedAt,
    string? SeoTitle,
    string? SeoDescription);
