using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.HtmlRendering.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shink.Components.Content;
using Shink.Components.Pages;
using Shink.Services;

namespace Shink.Tests;

[TestClass]
public sealed class LuisterPlaylistRouteTests
{
    private static readonly Guid PlaylistId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;

    [TestMethod]
    public async Task PersonalRoute_BindsGuidAndKeepsOrderedAuthorizedPlayback()
    {
        var fixture = new Fixture();
        fixture.PlaylistService.Result = new(true, [new(PlaylistId, "My slaaptyd", ["beta", "locked", "alpha"])]);

        var result = await RenderAsync(fixture, personal: true);

        CollectionAssert.AreEqual(new Guid?[] { PlaylistId }, fixture.PlaylistService.RequestedIds);
        Assert.AreEqual(0, fixture.Catalog.PublicPlaylistReads);
        using var document = new HtmlParser().ParseDocument(result.Html);
        Assert.AreEqual("My slaaptyd", document.QuerySelector("h1")?.TextContent);
        CollectionAssert.AreEqual(new[] { "beta", "alpha" }, fixture.Audio.SignedSlugs.ToArray());
        Assert.AreEqual("/media/audio/beta?token=test-signed", document.QuerySelector("audio")?.GetAttribute("src"));
        Assert.AreEqual($"/my-stories/speellys/{PlaylistId}?storie=alpha",
            document.QuerySelector("a.story-nav-next")?.GetAttribute("href"));
        Assert.AreEqual($"https://schink.example/my-stories/speellys/{PlaylistId}", result.CanonicalUrl);
        using var queue = JsonDocument.Parse(document.QuerySelector(".story-playlist-autoplay-data")!.TextContent);
        foreach (var track in queue.RootElement.EnumerateArray())
        {
            var audioUrl = track.GetProperty("audioUrl").GetString()!;
            Assert.StartsWith("/media/audio/", audioUrl);
            Assert.Contains("?token=", audioUrl);
        }
    }

    [TestMethod]
    [DataRow("slaaptyd")]
    [DataRow("11111111-1111-1111-1111-111111111111")]
    public async Task PublicRoute_KeepsStringSlugsSeparateFromPersonalIds(string slug)
    {
        var fixture = new Fixture();
        fixture.Catalog.PublicPlaylist = new(slug, "Openbare speellys", null, 0, [Story("alpha")]);

        var result = await RenderAsync(fixture, personal: false, slug);

        Assert.IsEmpty(fixture.PlaylistService.RequestedIds);
        Assert.AreEqual(1, fixture.Catalog.PublicPlaylistReads);
        Assert.AreEqual($"https://schink.example/luister/speellys/{slug}", result.CanonicalUrl);
        Assert.Contains("Openbare speellys", result.Html);
        CollectionAssert.AreEqual(new[] { "alpha" }, fixture.Audio.SignedSlugs.ToArray());
    }

    [TestMethod]
    public async Task AnonymousPersonalRoute_DoesNotReadOrExposePrivatePlaylist()
    {
        var fixture = new Fixture { SignedIn = false };

        var result = await RenderAsync(fixture, personal: true);

        Assert.IsEmpty(fixture.PlaylistService.RequestedIds);
        Assert.AreEqual(0, fixture.Catalog.StoryReads);
        Assert.AreEqual(0, fixture.Catalog.PublicPlaylistReads);
        Assert.IsEmpty(fixture.Audio.SignedSlugs);
        Assert.DoesNotContain("My slaaptyd", result.Html);
        Assert.Contains("Hierdie speellys is nog gesluit.", result.Html);
        Assert.AreEqual($"https://schink.example/my-stories/speellys/{PlaylistId}", result.CanonicalUrl);
    }

    [TestMethod]
    public async Task PersonalRoute_WithoutStoryEntitlement_DoesNotSignAudio()
    {
        var fixture = new Fixture();
        fixture.PlaylistService.Result = new(true, [new(PlaylistId, "My slaaptyd", ["locked"])]);

        var result = await RenderAsync(fixture, personal: true);

        Assert.IsEmpty(fixture.Audio.SignedSlugs);
        Assert.Contains("Hierdie speellys is nog gesluit.", result.Html);
        Assert.DoesNotContain("<audio", result.Html);
    }

    [TestMethod]
    public async Task PersonalRoute_UnknownOrOtherUsersPlaylist_KeepsNotFoundState()
    {
        var fixture = new Fixture();
        fixture.PlaylistService.Result = new(true, []);

        var result = await RenderAsync(fixture, personal: true);

        CollectionAssert.AreEqual(new Guid?[] { PlaylistId }, fixture.PlaylistService.RequestedIds);
        Assert.IsEmpty(fixture.Audio.SignedSlugs);
        Assert.Contains("Ons kon nie daardie speellys kry nie.", result.Html);
        Assert.DoesNotContain("<audio", result.Html);
        Assert.AreEqual($"https://schink.example/my-stories/speellys/{PlaylistId}", result.CanonicalUrl);
    }

    [TestMethod]
    public void RouteParameters_SwitchingBetweenPersonalAndPublic_ClearsUnusedValues()
    {
        var page = new LuisterPlaylist();

        RouteParameters(personal: true).SetParameterProperties(page);
        Assert.AreEqual(PlaylistId, page.PersonalPlaylistId);
        Assert.IsNull(page.PlaylistSlug);

        RouteParameters(personal: false, "slaaptyd").SetParameterProperties(page);
        Assert.AreEqual("slaaptyd", page.PlaylistSlug);
        Assert.IsNull(page.PersonalPlaylistId);

        RouteParameters(personal: true).SetParameterProperties(page);
        Assert.AreEqual(PlaylistId, page.PersonalPlaylistId);
        Assert.IsNull(page.PlaylistSlug);
    }

    private static ParameterView RouteParameters(bool personal, string slug = "slaaptyd")
    {
        var routes = typeof(LuisterPlaylist).GetCustomAttributes<RouteAttribute>().Select(route => route.Template).ToArray();
        var route = routes.Single(template => template.StartsWith(personal ? "/my-stories/" : "/luister/", StringComparison.Ordinal));
        var token = Regex.Match(route, @"\{(?<name>[^}:]+)(?<constraint>:guid)?\}");
        Assert.IsTrue(token.Success);
        Assert.AreEqual(personal, token.Groups["constraint"].Success, "Personal routes must retain their GUID constraint.");
        // Blazor converts :guid values to Guid before assigning component parameters.
        // Include nulls for parameters from the other route, as the Router does.
        var values = routes.SelectMany(template => Regex.Matches(template, @"\{([^}:]+)(?::[^}]+)?\}")
                .Select(match => match.Groups[1].Value))
            .Distinct().ToDictionary(name => name, _ => (object?)null);
        values[token.Groups["name"].Value] = personal ? PlaylistId : slug;
        return ParameterView.FromDictionary(values);
    }

    private static async Task<(string Html, string CanonicalUrl)> RenderAsync(Fixture fixture, bool personal, string slug = "slaaptyd")
    {
        var path = personal ? $"/my-stories/speellys/{PlaylistId}" : $"/luister/speellys/{slug}";
        var page = new LuisterPlaylist();
        using var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<NavigationManager>(new TestNavigationManager(path))
            .AddSingleton<AuthenticationStateProvider>(new TestAuthenticationProvider(fixture.SignedIn))
            .AddSingleton<IHttpContextAccessor>(new HttpContextAccessor { HttpContext = new DefaultHttpContext() })
            .AddSingleton<IAudioAccessService>(fixture.Audio)
            .AddSingleton<IStoryCatalogService>(fixture.Catalog)
            .AddSingleton<ISubscriberPlaylistService>(fixture.PlaylistService)
            .AddSingleton<IStoryFavoriteService, NoFavorites>()
            .AddSingleton(DispatchProxy.Create<ISubscriptionLedgerService, NoPaidSubscription>())
            .AddSingleton<IJSRuntime, NoPrerenderJs>()
            .AddSingleton<IComponentActivator>(new TestComponentActivator(page))
            .BuildServiceProvider();
        await using var renderer = new TestRenderer(services, services.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var rendered = renderer.BeginRenderingComponent(typeof(LuisterPlaylist), RouteParameters(personal, slug));
            await rendered.QuiescenceTask;
            return (rendered.ToHtmlString(), (string)typeof(LuisterPlaylist).GetProperty("PlaylistCanonicalUrl", PrivateInstance)!.GetValue(page)!);
        });
    }

    private static StoryItem Story(string slug, string access = "free") => new(slug, slug, "", "cover.png", "audio.mp3", AccessLevel: access);

    private sealed class Fixture
    {
        public bool SignedIn { get; init; } = true;
        public TestPlaylistService PlaylistService { get; } = new();
        public TestCatalog Catalog { get; } = new();
        public TestAudio Audio { get; } = new();
    }

    private sealed class TestRenderer(IServiceProvider services, ILoggerFactory loggerFactory) : StaticHtmlRenderer(services, loggerFactory)
    {
        protected override IComponent ResolveComponentForRenderMode(Type componentType, int? parentComponentId,
            IComponentActivator componentActivator, IComponentRenderMode renderMode) => componentActivator.CreateInstance(componentType);
    }

    private sealed class TestComponentActivator(LuisterPlaylist page) : IComponentActivator
    {
        public IComponent CreateInstance(Type componentType) => componentType == typeof(LuisterPlaylist) ? page : (IComponent)Activator.CreateInstance(componentType)!;
    }

    private sealed class TestNavigationManager : NavigationManager
    {
        public TestNavigationManager(string path) => Initialize("https://schink.example/", $"https://schink.example{path}");
        protected override void NavigateToCore(string uri, bool forceLoad) => throw new InvalidOperationException("Rendering should not navigate.");
    }

    private sealed class TestAuthenticationProvider(bool signedIn) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(new ClaimsPrincipal(
            signedIn ? new ClaimsIdentity([new Claim(ClaimTypes.Email, "listener@example.com")], "test") : new ClaimsIdentity())));
    }

    private sealed class TestPlaylistService : ISubscriberPlaylistService
    {
        public List<Guid?> RequestedIds { get; } = [];
        public SubscriberPlaylistListResult Result { get; set; } = new(true, [new(PlaylistId, "My slaaptyd", ["alpha"])]);
        public Task<SubscriberPlaylistListResult> GetPlaylistsAsync(Guid? playlistId = null, CancellationToken cancellationToken = default)
        {
            RequestedIds.Add(playlistId);
            return Task.FromResult(Result);
        }
        public Task<SubscriberPlaylistSaveResult> SavePlaylistAsync(Guid? playlistId, string title, IReadOnlyList<string> storySlugs, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Playback must not save.");
        public Task<bool> DeletePlaylistAsync(Guid playlistId, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Playback must not delete.");
    }

    private sealed class TestCatalog : IStoryCatalogService
    {
        public int StoryReads { get; private set; }
        public int PublicPlaylistReads { get; private set; }
        public StoryPlaylist? PublicPlaylist { get; set; }
        public Task<IReadOnlyList<StoryItem>> GetLuisterStoriesAsync(CancellationToken cancellationToken = default)
        {
            StoryReads++;
            return Task.FromResult<IReadOnlyList<StoryItem>>([Story("alpha"), Story("beta"), Story("locked", "subscriber")]);
        }
        public Task<IReadOnlyList<StoryPlaylist>> GetLuisterPlaylistsAsync(string? userEmail = null, CancellationToken cancellationToken = default)
        {
            PublicPlaylistReads++;
            return Task.FromResult<IReadOnlyList<StoryPlaylist>>(PublicPlaylist is { } playlist ? [playlist] : []);
        }
        public Task<IReadOnlyList<StoryItem>> GetFreeStoriesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<StoryPreviewItem>> GetNewestTop10Async(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<StoryPreviewItem>> GetBibleStoriesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<StoryItem?> FindAnyBySlugAsync(string? slug, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<StoryItem?> FindFreeBySlugAsync(string? slug, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<StoryItem?> FindLuisterBySlugAsync(string? slug, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class TestAudio : IAudioAccessService
    {
        public List<string> SignedSlugs { get; } = [];
        public string CreateSignedAudioUrl(string slug, TimeSpan? lifetime = null)
        {
            SignedSlugs.Add(slug);
            return $"/media/audio/{slug}?token=test-signed";
        }
        public bool IsTokenValid(string slug, string? token) => throw new NotSupportedException();
    }

    private sealed class NoFavorites : IStoryFavoriteService
    {
        public Task<IReadOnlyList<string>> GetFavoriteStorySlugsAsync(string? email, string? source = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<bool> SetStoryFavoriteAsync(string? email, StoryFavoriteMutationRequest request, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Rendering must not change favorites.");
    }

    public class NoPaidSubscription : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod?.Name == nameof(ISubscriptionLedgerService.GetActiveTierCodesAsync)
            ? Task.FromResult<IReadOnlyList<string>>([]) : throw new InvalidOperationException("Unexpected subscription operation.");
    }

    private sealed class NoPrerenderJs : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => throw new InvalidOperationException("Prerendering must not use JavaScript.");
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => InvokeAsync<TValue>(identifier, args);
    }
}
