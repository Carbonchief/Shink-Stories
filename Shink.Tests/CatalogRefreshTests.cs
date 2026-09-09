using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shink.Services;

namespace Shink.Tests;

[TestClass]
[DoNotParallelize]
public sealed class CatalogRefreshTests
{
    [TestMethod]
    [DataRow("stories", 3)]
    [DataRow("store_products", 1)]
    [DataRow("resource_types", 2)]
    [DataRow("blog_posts", 4)]
    [DataRow("story_characters", 1)]
    public async Task ConcurrentServiceInstances_ShareOneRefresh(string table, int expectedRequests)
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var handler = new CatalogHandler();
        using var client = new HttpClient(handler);
        var first = CreateRead(table, client, cache);
        var second = CreateRead(table, client, cache);

        var firstRead = first(CancellationToken.None);
        var secondRead = second(CancellationToken.None);
        try
        {
            Assert.AreEqual(1, handler.Count(table), "Transient services must coordinate refreshes of the shared cache.");
        }
        finally
        {
            handler.Release.TrySetResult();
            await Task.WhenAll(firstRead, secondRead).WaitAsync(TimeSpan.FromSeconds(10));
        }

        Assert.AreEqual(expectedRequests, handler.TotalRequests);
        await second(CancellationToken.None);
        Assert.AreEqual(expectedRequests, handler.TotalRequests, "A warm read must not query the database.");

        cache.Remove(CacheKey(table));
        await second(CancellationToken.None);
        Assert.AreEqual(expectedRequests * 2, handler.TotalRequests, "Invalidation must still refresh the catalogue.");
    }

    [TestMethod]
    [DataRow("stories")]
    [DataRow("store_products")]
    [DataRow("resource_types")]
    [DataRow("blog_posts")]
    [DataRow("story_characters")]
    public async Task CancelledWaiter_DoesNotCancelOrBlockSharedRefresh(string table)
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var handler = new CatalogHandler();
        using var client = new HttpClient(handler);
        using var cancellation = new CancellationTokenSource();
        var firstRead = CreateRead(table, client, cache)(CancellationToken.None);
        var waitingRead = CreateRead(table, client, cache)(cancellation.Token);
        try
        {
            cancellation.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await waitingRead.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.IsFalse(firstRead.IsCompleted, "Cancelling a waiter must not stop the shared fetch.");
        }
        finally
        {
            handler.Release.TrySetResult();
            await firstRead.WaitAsync(TimeSpan.FromSeconds(10));
            try { await waitingRead; } catch (OperationCanceledException) { }
        }

        var requestCount = handler.TotalRequests;
        await CreateRead(table, client, cache)(CancellationToken.None);
        Assert.AreEqual(requestCount, handler.TotalRequests);
    }

    [TestMethod]
    public async Task StoryRefresh_StartsIndependentQueriesTogether()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var handler = new CatalogHandler();
        using var client = new HttpClient(handler);
        var read = CreateRead("stories", client, cache)(CancellationToken.None);
        try
        {
            Assert.AreEqual(1, handler.Count("stories"));
            Assert.AreEqual(1, handler.Count("story_playlists"));
            Assert.AreEqual(1, handler.Count("story_playlist_items"));
        }
        finally
        {
            handler.Release.TrySetResult();
            await read.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    private static Func<CancellationToken, Task> CreateRead(string table, HttpClient client, IMemoryCache cache)
    {
        var options = Options.Create(new SupabaseOptions
        {
            Url = "https://example.supabase.co/",
            PublishableKey = "test-publishable-key"
        });
        return table switch
        {
            "stories" => token => new SupabaseStoryCatalogService(client, options, cache,
                NullLogger<SupabaseStoryCatalogService>.Instance).GetLuisterStoriesAsync(token),
            "store_products" => token => new SupabaseStoreProductCatalogService(client, options, cache,
                NullLogger<SupabaseStoreProductCatalogService>.Instance).GetEnabledProductsAsync(token),
            "resource_types" => token => new SupabaseResourceCatalogService(client, options, cache,
                NullLogger<SupabaseResourceCatalogService>.Instance).GetResourceTypesAsync(token),
            "story_characters" => token => new SupabaseCharacterService(client, options, cache,
                NullLogger<SupabaseCharacterService>.Instance).GetPublishedCharactersAsync(token),
            // Public catalogue reads must never invoke notifications; a null dependency makes that fail loudly.
            "blog_posts" => token => new SupabaseBlogService(client, options, cache, new BlogContentRenderer(), null!,
                NullLogger<SupabaseBlogService>.Instance).GetPublishedPostsAsync(token),
            _ => throw new ArgumentOutOfRangeException(nameof(table))
        };
    }

    private static string CacheKey(string table) => table switch
    {
        "stories" => "stories:catalog:v2",
        "store_products" => StoreProductCatalogCacheKeys.Catalog,
        "resource_types" => "resources:catalog:v1",
        "blog_posts" => "blog:published:v1",
        "story_characters" => "story-characters:published:v1",
        _ => throw new ArgumentOutOfRangeException(nameof(table))
    };

    private sealed class CatalogHandler : HttpMessageHandler
    {
        private readonly ConcurrentDictionary<string, int> _counts = new();
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int TotalRequests => _counts.Values.Sum();
        public int Count(string table) => _counts.GetValueOrDefault(table);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.AreEqual(HttpMethod.Get, request.Method, "Catalogue reads must not write data.");
            Assert.AreEqual("example.supabase.co", request.RequestUri!.Host);
            _counts.AddOrUpdate(request.RequestUri.Segments[^1], 1, (_, count) => count + 1);
            await Release.Task.WaitAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", Encoding.UTF8, "application/json")
            };
        }
    }
}
