namespace Shink.Services;

public sealed class LandingPageContent
{
    public int SchemaVersion { get; set; } = 1;
    public string Title { get; set; } = string.Empty;
    public string BackgroundColor { get; set; } = "#ff7133";
    public string TextColor { get; set; } = "#ffffff";
    public string SharingTitle { get; set; } = string.Empty;
    public string SharingDescription { get; set; } = string.Empty;
    public List<LandingPageBlock> Blocks { get; set; } = [];
}

public sealed class LandingPageBlock
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Type { get; set; } = "text";
    public string ImageUrl { get; set; } = string.Empty;
    public string AltText { get; set; } = string.Empty;
    public string ImageShape { get; set; } = "square";
    public string Html { get; set; } = string.Empty;
    public string Alignment { get; set; } = "left";
    public string Text { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string LinkType { get; set; } = string.Empty;
    public string BackgroundColor { get; set; } = string.Empty;
    public string TextColor { get; set; } = string.Empty;
}

public sealed record LandingPageRecord(
    Guid PageId,
    string Slug,
    LandingPageContent Draft,
    bool IsPublished,
    bool HasUnpublishedChanges,
    bool HasEverPublished,
    long Revision,
    DateTimeOffset UpdatedAt);

public sealed record LandingPageSaveRequest(
    Guid? PageId,
    string Slug,
    LandingPageContent Content,
    long? ExpectedRevision);

public sealed record LandingPageOperationResult(
    bool Success,
    string? ErrorCode,
    LandingPageRecord? Page);

public sealed record PublishedLandingPage(
    Guid PageId,
    string Slug,
    LandingPageContent Content);

public static class LandingPageErrorCodes
{
    public const string AdminRequired = "admin_required";
    public const string SupabaseNotConfigured = "supabase_not_configured";
    public const string SchemaNotReady = "schema_not_ready";
    public const string SlugInvalid = "slug_invalid";
    public const string SlugTaken = "slug_taken";
    public const string SlugImmutable = "slug_immutable";
    public const string ContentInvalid = "content_invalid";
    public const string PublishValidationFailed = "publish_validation_failed";
    public const string PageNotFound = "page_not_found";
    public const string PageNotPublished = "page_not_published";
    public const string RevisionRequired = "revision_required";
    public const string RevisionConflict = "revision_conflict";
    public const string RequestFailed = "request_failed";
}

public sealed class LandingPageServiceException(string errorCode)
    : InvalidOperationException(errorCode)
{
    public string ErrorCode { get; } = errorCode;
}
