using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shink.Components.Content;
using Shink.Services;

namespace Shink.Tests;

[TestClass]
public class SubscriberPlaylistTests
{
    private static readonly Guid PlaylistId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [TestMethod]
    public async Task AnonymousUsersCannotReadSaveOrDeletePlaylists()
    {
        var handler = new RpcHandler("[]");
        var service = CreateService(handler, signedIn: false);
        Assert.IsFalse((await service.GetPlaylistsAsync()).IsSuccess);
        Assert.IsFalse((await service.SavePlaylistAsync(null, "My stories", ["alpha"])).IsSuccess);
        Assert.IsFalse(await service.DeletePlaylistAsync(PlaylistId));
        Assert.AreEqual(0, handler.Calls.Count);
    }

    [TestMethod]
    public async Task SaveUsesSessionIdentityAndPreservesSelectedOrder()
    {
        var handler = new RpcHandler($$"""[{"playlist_id":"{{PlaylistId}}","title":"Slaaptyd","story_slugs":["beta","alpha"]}]""");
        var result = await CreateService(handler).SavePlaylistAsync(PlaylistId, " Slaaptyd ", [" Beta ", "alpha", "BETA"]);
        Assert.IsTrue(result.IsSuccess);
        CollectionAssert.AreEqual(new[] { "beta", "alpha" }, result.Playlist!.StorySlugs.ToArray());
        var call = handler.Calls.Single();
        StringAssert.EndsWith(call.Path, "/rpc/save_subscriber_playlist");
        var payload = JsonSerializer.Deserialize<JsonElement>(call.Payload);
        Assert.AreEqual("listener@example.com", payload.GetProperty("p_email").GetString());
        Assert.AreEqual("Slaaptyd", payload.GetProperty("p_title").GetString());
        Assert.AreEqual(PlaylistId.ToString(), payload.GetProperty("p_playlist_id").GetString());
        CollectionAssert.AreEqual(new[] { "beta", "alpha" }, payload.GetProperty("p_story_slugs").EnumerateArray().Select(item => item.GetString()).ToArray());
    }

    [TestMethod]
    public async Task InvalidTitlesUnknownStoriesAndVideosCannotBeSaved()
    {
        var handler = new RpcHandler("[]");
        var service = CreateService(handler);
        Assert.IsFalse((await service.SavePlaylistAsync(null, " ", [])).IsSuccess);
        Assert.IsFalse((await service.SavePlaylistAsync(null, new string('a', 81), [])).IsSuccess);
        Assert.IsFalse((await service.SavePlaylistAsync(null, "Bedtime", ["missing"])).IsSuccess);
        Assert.IsFalse((await service.SavePlaylistAsync(null, "Bedtime", ["video"])).IsSuccess);
        Assert.IsFalse((await service.SavePlaylistAsync(null, "Bedtime", Enumerable.Range(0, 101).Select(i => $"story-{i}").ToArray())).IsSuccess);
        Assert.AreEqual(0, handler.Calls.Count);
    }

    [TestMethod]
    public async Task EmptyPlaylistCanBeCreated()
    {
        var handler = new RpcHandler($$"""[{"playlist_id":"{{PlaylistId}}","title":"Reistyd","story_slugs":[]}]""");
        var result = await CreateService(handler).SavePlaylistAsync(null, "Reistyd", []);
        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(0, result.Playlist!.StorySlugs.Count);
    }

    [TestMethod]
    public async Task LookupScopesPlaybackRequestToSessionAndPlaylistId()
    {
        var handler = new RpcHandler("[]");
        var result = await CreateService(handler).GetPlaylistsAsync(PlaylistId);
        Assert.IsTrue(result.IsSuccess);
        var payload = JsonSerializer.Deserialize<JsonElement>(handler.Calls.Single().Payload);
        Assert.AreEqual("listener@example.com", payload.GetProperty("p_email").GetString());
        Assert.AreEqual(PlaylistId.ToString(), payload.GetProperty("p_playlist_id").GetString());
    }

    [TestMethod]
    public async Task ProviderFailuresDoNotLookLikeSuccessfulEmptyListsOrSaves()
    {
        var handler = new RpcHandler("unavailable", HttpStatusCode.ServiceUnavailable);
        var service = CreateService(handler);
        Assert.IsFalse((await service.GetPlaylistsAsync()).IsSuccess);
        Assert.IsFalse((await service.SavePlaylistAsync(null, "Slaaptyd", ["alpha"])).IsSuccess);
        Assert.IsFalse(await service.DeletePlaylistAsync(PlaylistId));
        Assert.IsFalse((await CreateService(new RpcHandler("bad-json")).GetPlaylistsAsync()).IsSuccess);
    }

    [TestMethod]
    public async Task DeleteRequiresProviderConfirmation()
    {
        Assert.IsTrue(await CreateService(new RpcHandler("true")).DeletePlaylistAsync(PlaylistId));
        Assert.IsFalse(await CreateService(new RpcHandler("false")).DeletePlaylistAsync(PlaylistId));
    }

    [TestMethod]
    public async Task CallerCancellationIsPropagated()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var service = CreateService(new RpcHandler("[]"));
        await Assert.ThrowsExactlyAsync<TaskCanceledException>(() => service.GetPlaylistsAsync(cancellationToken: cancellation.Token));
    }

    [TestMethod]
    public void PlaybackKeepsSavedOrderAndFiltersUnavailableAndVideoStories()
    {
        var catalog = new[] { Story("alpha"), Story("beta"), Story("video", "video") };
        var result = SubscriberPlaylistPlayback.BuildPlaylist(new(PlaylistId, "Slaaptyd", ["beta", "removed", "video", "alpha", "beta"]), catalog);
        Assert.AreEqual(PlaylistId.ToString(), result.Slug);
        Assert.AreEqual("Slaaptyd", result.Title);
        CollectionAssert.AreEqual(new[] { "beta", "alpha" }, result.Stories.Select(story => story.Slug).ToArray());
        Assert.AreSame(catalog[1], result.Stories[0]);
    }

    private static StoryItem Story(string slug, string type = "story") => new(slug, slug, "", "cover.png", "audio.mp3", StoryType: type);

    private static SupabaseSubscriberPlaylistService CreateService(RpcHandler handler, bool signedIn = true) => new(
        new HttpClient(handler), Options.Create(new SupabaseOptions { Url = "https://example.supabase.co", SecretKey = "test-server-key" }),
        new TestAuthenticationProvider(signedIn), new TestCatalog(), NullLogger<SupabaseSubscriberPlaylistService>.Instance);

    private sealed class TestAuthenticationProvider(bool signedIn) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(
            new ClaimsPrincipal(signedIn
                ? new ClaimsIdentity([new Claim(ClaimTypes.Email, " Listener@Example.com ")], "test")
                : new ClaimsIdentity())));
    }

    private sealed class RpcHandler(string body, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public List<(string Path, string Payload)> Calls { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls.Add((request.RequestUri!.AbsolutePath, await request.Content!.ReadAsStringAsync(cancellationToken)));
            return new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    private sealed class TestCatalog : IStoryCatalogService
    {
        public Task<IReadOnlyList<StoryItem>> GetLuisterStoriesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<StoryItem>>([Story("alpha"), Story("beta"), Story("video", "video")]);
        public Task<IReadOnlyList<StoryItem>> GetFreeStoriesAsync(CancellationToken cancellationToken = default) => GetLuisterStoriesAsync(cancellationToken);
        public Task<IReadOnlyList<StoryPlaylist>> GetLuisterPlaylistsAsync(string? userEmail = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<StoryPlaylist>>([]);
        public Task<IReadOnlyList<StoryPreviewItem>> GetNewestTop10Async(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<StoryPreviewItem>>([]);
        public Task<IReadOnlyList<StoryPreviewItem>> GetBibleStoriesAsync(CancellationToken cancellationToken = default) => GetNewestTop10Async(cancellationToken);
        public Task<StoryItem?> FindAnyBySlugAsync(string? slug, CancellationToken cancellationToken = default) => Task.FromResult<StoryItem?>(Story(slug ?? ""));
        public Task<StoryItem?> FindFreeBySlugAsync(string? slug, CancellationToken cancellationToken = default) => FindAnyBySlugAsync(slug, cancellationToken);
        public Task<StoryItem?> FindLuisterBySlugAsync(string? slug, CancellationToken cancellationToken = default) => FindAnyBySlugAsync(slug, cancellationToken);
    }
}
