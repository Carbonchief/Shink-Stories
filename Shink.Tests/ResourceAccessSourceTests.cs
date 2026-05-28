using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Shink.Tests;

[TestClass]
public class ResourceAccessSourceTests
{
    [TestMethod]
    public void PreviewRouteUsesSameTierAuthorizationAsDocumentDownload()
    {
        var program = File.ReadAllText(GetRepoPath("Shink", "Program.cs"));
        var previewRouteStart = program.IndexOf("app.MapGet(\"/media/resources/{resourceDocumentId:guid}/preview\"", StringComparison.Ordinal);
        Assert.AreNotEqual(-1, previewRouteStart);
        var previewRouteEnd = program.IndexOf("app.MapGet(\"/betaal/payfast/{planSlug}\"", previewRouteStart, StringComparison.Ordinal);
        Assert.AreNotEqual(-1, previewRouteEnd);
        var previewRoute = program[previewRouteStart..previewRouteEnd];

        StringAssert.Contains(previewRoute, "ISubscriptionLedgerService subscriptionLedgerService");
        StringAssert.Contains(previewRoute, "HasAccessToResourceDocumentAsync(");
        StringAssert.Contains(previewRoute, "preview.RequiredTierCode");
        StringAssert.Contains(previewRoute, "return Results.Forbid();");

        var downloadRouteStart = program.IndexOf("app.MapGet(\"/media/resources/{resourceDocumentId:guid}\"", StringComparison.Ordinal);
        Assert.AreNotEqual(-1, downloadRouteStart);
        var downloadRoute = program[downloadRouteStart..previewRouteStart];
        StringAssert.Contains(downloadRoute, "HasAccessToResourceDocumentAsync(");
    }

    [TestMethod]
    public void PreviewDownloadsCarryRequiredTierAndLockedPreviewUrlsAreHidden()
    {
        var contract = File.ReadAllText(GetRepoPath("Shink", "Services", "IResourceCatalogService.cs"));
        var service = File.ReadAllText(GetRepoPath("Shink", "Services", "SupabaseResourceCatalogService.cs"));
        var resourcesPage = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "Resources.razor"));

        StringAssert.Contains(contract, "public sealed record ResourceDocumentPreviewDownload(");
        StringAssert.Contains(contract, "string? RequiredTierCode");
        StringAssert.Contains(service, "RequiredTierCode: NormalizeOptionalText(document.RequiredTierCode, 64)");
        StringAssert.Contains(resourcesPage, "canAccessDocument && !string.IsNullOrWhiteSpace(document.PreviewUrl)");
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
