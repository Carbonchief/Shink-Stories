namespace Shink.Services;

public interface ILandingPageCatalogService
{
    Task<PublishedLandingPage?> FindPublishedBySlugAsync(
        string? slug,
        CancellationToken cancellationToken = default);
}
