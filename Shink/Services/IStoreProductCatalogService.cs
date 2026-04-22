using Shink.Components.Content;

namespace Shink.Services;

public interface IStoreProductCatalogService
{
    Task<IReadOnlyList<StoreProduct>> GetEnabledProductsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StoreProduct>> GetAllProductsAsync(CancellationToken cancellationToken = default);
    Task<StoreProduct?> FindEnabledBySlugAsync(string? slug, CancellationToken cancellationToken = default);
}
