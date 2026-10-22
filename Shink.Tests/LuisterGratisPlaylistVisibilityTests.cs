using System.Reflection;
using System.Security.Claims;
using AngleSharp.Html.Parser;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.HtmlRendering.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shink.Components.Content;
using Shink.Components.Pages;
using Shink.Services;

namespace Shink.Tests;

[TestClass]
public sealed class LuisterGratisPlaylistVisibilityTests
{
    public static IEnumerable<object[]> ActivePaidTiers =>
        new[] { StoryAccessPolicy.StoryCornerMonthlyTierCode }
            .Concat(StoryAccessPolicy.AllStoriesTierCodes)
            .Select(tier => new object[] { tier });

    [TestMethod]
    [DynamicData(nameof(ActivePaidTiers))]
    public async Task Luister_ActivePaidTier_HidesGratisSectionAndCarouselEntry(string tier)
    {
        // A paid account may retain a gratis tier alongside its paid tier.
        var html = await RenderAsync(signedIn: true, activeTiers: ["gratis", tier]);
        using var document = new HtmlParser().ParseDocument(html);

        Assert.IsNull(document.QuerySelector("#luister-playlist-gratis-stories-heading"));
        Assert.IsNull(document.QuerySelector("a.luister-speellyste-link[href*='gratis-stories']"));
        Assert.IsNotNull(document.QuerySelector("#luister-playlist-slaaptyd-heading"));
        Assert.IsNotNull(document.QuerySelector("a[href='/luister/gratis-storie']"),
            "Free stories must remain available in other playlists for paying users.");
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task Luister_NoActivePaidTier_KeepsConfiguredGratisPlaylist(bool signedIn, bool hasGratisTier)
    {
        // Lapsed subscribers have no active paid tier, even if they remain signed in.
        var html = await RenderAsync(signedIn, activeTiers: hasGratisTier ? ["gratis"] : []);
        using var document = new HtmlParser().ParseDocument(html);

        Assert.AreEqual("Gratis uit die admin", document.QuerySelector("#luister-playlist-gratis-stories-heading")?.TextContent.Trim());
        Assert.Contains("Die ingestelde beskrywing", html);
        Assert.IsNotNull(document.QuerySelector("a.luister-speellyste-link[href*='gratis-stories']"));
        Assert.IsNotNull(document.QuerySelector(".luister-gratis-stories-section a[href='/luister/gratis-storie']"));
    }

    [TestMethod]
    public async Task Luister_GratisOmittedByAdmin_DoesNotAddItForFreeUsers()
    {
        var html = await RenderAsync(signedIn: true, activeTiers: ["gratis"], includeGratis: false);
        using var document = new HtmlParser().ParseDocument(html);

        Assert.IsNull(document.QuerySelector("#luister-playlist-gratis-stories-heading"));
        Assert.IsNull(document.QuerySelector("a.luister-speellyste-link[href*='gratis-stories']"));
    }

    private static async Task<string> RenderAsync(bool signedIn, string[] activeTiers, bool includeGratis = true)
    {
        var freeStory = new StoryItem("gratis-storie", "Gratis storie", "", "/cover.png", "audio.mp3", AccessLevel: "free");
        var playlists = new List<StoryPlaylist>
        {
            new("slaaptyd", "Slaaptyd", null, 1, [freeStory], IncludeInSpeellysteCarousel: true)
        };
        if (includeGratis)
        {
            playlists.Add(new(StoryAccessPolicy.GratisPlaylistSlug, "Gratis uit die admin", "Die ingestelde beskrywing", 2,
                [freeStory], IncludeInSpeellysteCarousel: true));
        }

        using var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<NavigationManager, TestNavigationManager>()
            .AddSingleton<AuthenticationStateProvider>(new TestAuthenticationProvider(signedIn))
            .AddSingleton(Service<IStoryCatalogService>(method => method.Name == nameof(IStoryCatalogService.GetLuisterPlaylistsAsync)
                ? Task.FromResult<IReadOnlyList<StoryPlaylist>>(playlists)
                : throw new InvalidOperationException("Unexpected catalog operation.")))
            .AddSingleton(Service<ISubscriptionLedgerService>(method => method.Name == nameof(ISubscriptionLedgerService.GetActiveTierCodesAsync)
                ? Task.FromResult<IReadOnlyList<string>>(activeTiers)
                : throw new InvalidOperationException("Rendering must not change subscriptions.")))
            .AddSingleton(Service<IStoryFavoriteService>(method => method.Name == nameof(IStoryFavoriteService.GetFavoriteStorySlugsAsync)
                ? Task.FromResult<IReadOnlyList<string>>([])
                : throw new InvalidOperationException("Rendering must not change favorites.")))
            .AddSingleton<IJSRuntime, NoRenderJs>()
            .BuildServiceProvider();
        await using var renderer = new TestRenderer(services, services.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var rendered = renderer.BeginRenderingComponent(typeof(Luister), ParameterView.Empty);
            await rendered.QuiescenceTask;
            return rendered.ToHtmlString();
        });
    }

    private static T Service<T>(Func<MethodInfo, object?> handler) where T : class
    {
        var service = DispatchProxy.Create<T, TestService>();
        ((TestService)(object)service).Handler = handler;
        return service;
    }

    public class TestService : DispatchProxy
    {
        public Func<MethodInfo, object?> Handler { get; set; } = null!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Handler(targetMethod!);
    }

    private sealed class TestRenderer(IServiceProvider services, ILoggerFactory loggerFactory) : StaticHtmlRenderer(services, loggerFactory)
    {
        protected override IComponent ResolveComponentForRenderMode(Type componentType, int? parentComponentId,
            IComponentActivator componentActivator, IComponentRenderMode renderMode) => componentActivator.CreateInstance(componentType);
    }

    private sealed class TestNavigationManager : NavigationManager
    {
        public TestNavigationManager() => Initialize("https://schink.example/", "https://schink.example/luister");
        protected override void NavigateToCore(string uri, bool forceLoad) => throw new InvalidOperationException("Rendering should not navigate.");
    }

    private sealed class TestAuthenticationProvider(bool signedIn) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(new ClaimsPrincipal(
            signedIn ? new ClaimsIdentity([new Claim(ClaimTypes.Email, "listener@example.com")], "test") : new ClaimsIdentity())));
    }

    private sealed class NoRenderJs : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => throw new InvalidOperationException("Rendering must not use JavaScript.");
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => InvokeAsync<TValue>(identifier, args);
    }
}
