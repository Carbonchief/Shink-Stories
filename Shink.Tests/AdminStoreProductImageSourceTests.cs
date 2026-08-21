using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Runtime.CompilerServices;

namespace Shink.Tests;

[TestClass]
public class AdminStoreProductImageSourceTests
{
    [TestMethod]
    public void ExistingProductSavePreservesItsImageWhenNoReplacementIsProvided()
    {
        var service = File.ReadAllText(GetRepoPath("Shink", "Services", "SupabaseAdminManagementService.cs"));
        var panel = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "AdminStorePanel.razor"));

        StringAssert.Contains(service, "var isNewProduct = request.StoreProductId is null || request.StoreProductId == Guid.Empty;");
        StringAssert.Contains(service, "if (isNewProduct && string.IsNullOrWhiteSpace(normalizedImagePath))");
        StringAssert.Contains(service, "payload[\"image_path\"] = normalizedImagePath;");
        StringAssert.Contains(panel, "else if (!IsNewProduct &&");
        StringAssert.Contains(panel, "Editor.ImagePath = originalImagePath;");
    }

    [TestMethod]
    public void AdminProductListFallsBackToTheBundledImageForKnownProductSlugs()
    {
        var service = File.ReadAllText(GetRepoPath("Shink", "Services", "SupabaseAdminManagementService.cs"));
        var panel = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "AdminStorePanel.razor"));

        StringAssert.Contains(service, "StoreProductCatalog.FindBySlug(normalizedSlug)?.ImagePath");
        StringAssert.Contains(panel, "ResolveStoreProductImageUrl(product)");
        StringAssert.Contains(panel, "data-fallback-src=\"@productImageFallbackUrl\"");
        StringAssert.Contains(panel, "this.dataset.fallbackSrc || '/branding/schink-logo-green.png'");
    }

    private static string GetRepoPath(params string[] segments)
    {
        var parts = new[]
        {
            Path.GetDirectoryName(GetSourceFilePath())!,
            ".."
        }.Concat(segments).ToArray();

        return Path.GetFullPath(Path.Combine(parts));
    }

    private static string GetSourceFilePath([CallerFilePath] string path = "") => path;
}
