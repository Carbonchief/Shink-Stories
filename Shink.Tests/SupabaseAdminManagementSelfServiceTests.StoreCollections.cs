using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shink.Components.Content;
using Shink.Services;

namespace Shink.Tests;

public partial class SupabaseAdminManagementSelfServiceTests
{
    [TestMethod]
    [DataRow("kinderboeke", false)]
    [DataRow("storietjommies", false)]
    [DataRow("pakkette", false)]
    [DataRow("kinderboeke", true)]
    [DataRow("storietjommies", true)]
    [DataRow(" PAKKETTE ", true)]
    public async Task StoreCollections_SaveAndPublicReadUseTheSameAssignment(string collection, bool update)
    {
        var id = Guid.Parse("11111111-1111-1111-1111-111111111111");
        JsonElement saved = default;
        var handler = new RecordingHandler(request =>
        {
            if (IsSupabaseGet(request, "/rest/v1/admin_users"))
                return JsonResponse("""[{"email":"admin@example.com"}]""");
            if (request.RequestUri?.AbsolutePath == "/rest/v1/store_products")
            {
                if (request.Method == HttpMethod.Get)
                {
                    StringAssert.Contains(Uri.UnescapeDataString(request.RequestUri.Query), "collection_slug");
                    return JsonResponse("[" + saved.GetRawText() + "]");
                }
                Assert.AreEqual(update ? "PATCH" : "POST", request.Method.Method);
                using var body = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
                var row = update ? body.RootElement : body.RootElement[0];
                var properties = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(row.GetRawText())!;
                properties["store_product_id"] = JsonSerializer.SerializeToElement(id);
                saved = JsonSerializer.SerializeToElement(properties);
                return JsonResponse("[" + saved.GetRawText() + "]");
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var admin = CreateService(handler);
        var result = await admin.SaveStoreProductAsync("admin@example.com", CollectionRequest(update ? id : null, collection));
        Assert.IsTrue(result.IsSuccess, result.ErrorMessage);
        Assert.AreEqual(collection.Trim().ToLowerInvariant(), saved.GetProperty("collection_slug").GetString());

        var records = await admin.GetStoreProductsAsync("admin@example.com");
        Assert.AreEqual(1, records.Count, saved.GetRawText());
        Assert.AreEqual(collection.Trim().ToLowerInvariant(), records[0].CollectionSlug);

        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var client = new HttpClient(handler);
        var catalogue = new SupabaseStoreProductCatalogService(client,
            Options.Create(new SupabaseOptions { Url = "https://example.supabase.co", PublishableKey = "test" }),
            cache, NullLogger<SupabaseStoreProductCatalogService>.Instance);
        var products = await catalogue.GetEnabledProductsAsync();
        Assert.AreEqual(1, products.Count);
        Assert.AreEqual(records[0].CollectionSlug, products[0].CollectionSlug);
        Assert.AreEqual(StoreProductCollections.Find(collection)!.Name, products[0].CollectionName);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("unknown")]
    [DataRow("Kinderboeke,Pakkette")]
    public async Task StoreCollections_RejectInvalidAssignmentBeforeWriting(string collection)
    {
        var handler = new RecordingHandler(request =>
        {
            Assert.IsTrue(IsSupabaseGet(request, "/rest/v1/admin_users"), "Invalid collections must not write products.");
            return JsonResponse("""[{"email":"admin@example.com"}]""");
        });
        var result = await CreateService(handler).SaveStoreProductAsync("admin@example.com", CollectionRequest(null, collection));
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual("Kies 'n geldige produkversameling.", result.ErrorMessage);
    }

    [TestMethod]
    public async Task StoreCollections_NonAdminCannotReassignProducts()
    {
        var handler = new RecordingHandler(request =>
        {
            Assert.IsTrue(IsSupabaseGet(request, "/rest/v1/admin_users"));
            return JsonResponse("[]");
        });
        var result = await CreateService(handler).SaveStoreProductAsync("buyer@example.com", CollectionRequest(Guid.NewGuid(), "pakkette"));
        Assert.IsFalse(result.IsSuccess);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("invalid")]
    public void StoreCollections_LegacyRecordsKeepTheirStoryBuddyLabel(string? collection)
    {
        Assert.AreEqual("storietjommies", StoreProductCollections.GetOrDefault(collection).Slug);
        Assert.AreEqual("StorieTjommies", StoreProductCollections.GetOrDefault(collection).Name);
    }

    private static AdminStoreProductSaveRequest CollectionRequest(Guid? id, string collection) =>
        new(id, "collection-test", "Versamelingstoets", "Toetsbeskrywing", "https://example.supabase.co/test.png", "Toets", null,
            180m, 1, true, false, collection);
}
