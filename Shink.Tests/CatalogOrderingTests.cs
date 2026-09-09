using System.Net;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shink.Services;

namespace Shink.Tests;

[TestClass]
public sealed class CatalogOrderingTests
{
    [TestMethod]
    public async Task StoreReads_PreserveOrderAndNeverReturnDisabledProductsFromPublicLookup()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var handler = new RowsHandler("store_products", """
            [
              {"store_product_id":"00000001-0000-0000-0000-000000000000","slug":"last","name":"Last","sort_order":2,"is_enabled":true,"unit_price_zar":49,"image_path":"/last.png"},
              {"store_product_id":"00000002-0000-0000-0000-000000000000","slug":"beta","name":"Beta","sort_order":1,"is_enabled":true,"unit_price_zar":29,"image_path":"/beta.png"},
              {"store_product_id":"00000003-0000-0000-0000-000000000000","slug":"alpha","name":"alpha","sort_order":1,"is_enabled":true,"unit_price_zar":19,"image_path":"/alpha.png"},
              {"store_product_id":"00000004-0000-0000-0000-000000000000","slug":"hidden","name":"Hidden","sort_order":0,"is_enabled":false,"unit_price_zar":39,"image_path":"/hidden.png"}
            ]
            """);
        using var client = new HttpClient(handler);
        var service = new SupabaseStoreProductCatalogService(client, Options(), cache,
            NullLogger<SupabaseStoreProductCatalogService>.Instance);
        CollectionAssert.AreEqual(new[] { "hidden", "alpha", "beta", "last" },
            (await service.GetAllProductsAsync()).Select(product => product.Slug).ToArray());
        CollectionAssert.AreEqual(new[] { "alpha", "beta", "last" },
            (await service.GetEnabledProductsAsync()).Select(product => product.Slug).ToArray());
        Assert.IsNull(await service.FindEnabledBySlugAsync("hidden"));
        Assert.IsNull(await service.FindEnabledBySlugAsync("missing"));
        Assert.AreEqual(19m, (await service.FindEnabledBySlugAsync(" ALPHA "))!.UnitPriceZar);
        Assert.AreEqual(1, handler.RequestCount);
    }

    [TestMethod]
    public async Task BlogReads_PreserveNewestFirstTitleTiebreakAndScheduledPostExclusion()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var handler = new RowsHandler("blog_posts", """
            [
              {"post_id":"00000001-0000-0000-0000-000000000000","slug":"old","title":"Old","published_at":"2025-01-01T00:00:00Z","content_markdown":"Old body"},
              {"post_id":"00000002-0000-0000-0000-000000000000","slug":"beta","title":"Beta","published_at":"2026-01-01T00:00:00Z","content_markdown":"Beta body"},
              {"post_id":"00000003-0000-0000-0000-000000000000","slug":"alpha","title":"alpha","published_at":"2026-01-01T00:00:00Z","content_markdown":"Alpha body"},
              {"post_id":"00000004-0000-0000-0000-000000000000","slug":"future","title":"Future","published_at":"2099-01-01T00:00:00Z","content_markdown":"Future body"}
            ]
            """);
        using var client = new HttpClient(handler);
        var service = new SupabaseBlogService(client, Options(), cache, new BlogContentRenderer(), null!,
            NullLogger<SupabaseBlogService>.Instance);
        CollectionAssert.AreEqual(new[] { "alpha", "beta", "old" },
            (await service.GetPublishedPostsAsync()).Select(post => post.Slug).ToArray());
        Assert.IsNull(await service.FindPublishedPostBySlugAsync("future"));
        Assert.IsNotNull(await service.FindPublishedPostBySlugAsync("ALPHA"));
        Assert.AreEqual(4, handler.RequestCount);
    }

    private static IOptions<SupabaseOptions> Options() => Microsoft.Extensions.Options.Options.Create(new SupabaseOptions
    {
        Url = "https://example.supabase.co/", PublishableKey = "test-public"
    });

    private sealed class RowsHandler(string table, string rows) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.AreEqual(HttpMethod.Get, request.Method);
            Assert.AreEqual("example.supabase.co", request.RequestUri!.Host);
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(request.RequestUri.Segments[^1] == table ? rows : "[]", Encoding.UTF8, "application/json")
            });
        }
    }
}
