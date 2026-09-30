using AngleSharp.Html.Parser;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shink.Components.Pages;
using Shink.Components.Shared;
using Shink.Services;

namespace Shink.Tests;

[TestClass]
public class LandingPageHtmlRendererTests
{
    [TestMethod]
    public async Task Renderer_PreservesBlockOrderAndRendersSquareImagesAndWrappingClasses()
    {
        var content = new LandingPageContent
        {
            Title = "Private internal title",
            Blocks =
            [
                new() { Type = "text", Html = "<p>FIRST TEXT BLOCK</p>", Alignment = "right" },
                new() { Type = "image", ImageUrl = "/media/hero.webp", AltText = "Hero image", Alignment = "left" },
                new() { Type = "button", Text = "LAST BUTTON BLOCK", Url = "/next-step", Alignment = "center" }
            ]
        };

        var html = await RenderRendererAsync(content);

        var document = new HtmlParser().ParseDocument(html);
        Assert.AreEqual("Private internal title", document.QuerySelector("h1.landing-page-accessible-title")?.TextContent);
        Assert.IsTrue(document.QuerySelector("figure.landing-image-block img")?.ClassList.Contains("landing-image-square"));
        Assert.IsTrue(document.QuerySelector(".landing-text-block")?.ClassList.Contains("landing-align-right"));
        Assert.IsTrue(document.QuerySelector(".landing-button-block")?.ClassList.Contains("landing-align-center"));

        var blocks = document.QuerySelectorAll(
            ".landing-renderer-column > .landing-text-block, " +
            ".landing-renderer-column > .landing-image-block, " +
            ".landing-renderer-column > .landing-button-block");
        CollectionAssert.AreEqual(
            new[] { "landing-text-block", "landing-image-block", "landing-button-block" },
            blocks.Select(element => element.ClassList.First()).ToArray());
    }

    [TestMethod]
    public async Task Renderer_SanitizesMarkupAndDoesNotRenderUnsafeUrlsOrAttributes()
    {
        var content = new LandingPageContent
        {
            Blocks =
            [
                new()
                {
                    Type = "text",
                    Html = """
                           <p onclick="run()">VISIBLE COPY
                           <img src="https://example.com/tracker.png" onerror="run()">
                           <a href="javascript:run()">UNSAFE LINK TEXT</a></p>
                           """,
                    Alignment = "center",
                    TextColor = "#123456"
                },
                new() { Type = "image", ImageUrl = "javascript:run()", AltText = "Hidden bad image" },
                new() { Type = "button", Text = "DISABLED CTA", Url = "javascript:run()", Alignment = "right" }
            ]
        };

        var html = await RenderRendererAsync(content);

        var document = new HtmlParser().ParseDocument(html);
        StringAssert.Contains(document.QuerySelector(".landing-text-block")?.TextContent ?? string.Empty, "VISIBLE COPY");
        StringAssert.Contains(document.QuerySelector(".landing-text-block")?.TextContent ?? string.Empty, "UNSAFE LINK TEXT");
        Assert.IsTrue(document.QuerySelector(".landing-text-block")?.ClassList.Contains("landing-align-center"));
        Assert.IsTrue(document.QuerySelector(".landing-button-block")?.ClassList.Contains("landing-align-right"));
        Assert.AreEqual("DISABLED CTA", document.QuerySelector(".landing-button-block span[aria-disabled='true']")?.TextContent);
        Assert.AreEqual(0, document.QuerySelectorAll("[onclick], [onerror]").Length);
        Assert.IsNull(document.QuerySelector("img"));
        Assert.IsNull(document.QuerySelector("a[href^='javascript:']"));
        Assert.IsFalse(document.DocumentElement!.TextContent.Contains("javascript:", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task PublicPage_UnpublishedSlugRendersNotFoundWithHttp404AndNoCache()
    {
        var rendered = await RenderPublicPageAsync(new FixedLandingPageCatalog(null), "unknown-campaign");

        Assert.AreEqual(StatusCodes.Status404NotFound, rendered.HttpContext.Response.StatusCode);
        StringAssert.Contains(rendered.Html, "Blad nie gevind nie");
        Assert.AreEqual("no-store, no-cache, max-age=0, must-revalidate",
            rendered.HttpContext.Response.Headers["Cache-Control"].ToString());
        Assert.AreEqual("no-cache", rendered.HttpContext.Response.Headers["Pragma"].ToString());
        Assert.AreEqual("0", rendered.HttpContext.Response.Headers["Expires"].ToString());
    }

    [TestMethod]
    public async Task PublicPage_CatalogFailureRendersAfrikaansUnavailableStateWithHttp503()
    {
        var rendered = await RenderPublicPageAsync(
            new FixedLandingPageCatalog(null, new InvalidOperationException("catalog unavailable")),
            "campaign");

        Assert.AreEqual(StatusCodes.Status503ServiceUnavailable, rendered.HttpContext.Response.StatusCode);
        StringAssert.Contains(rendered.Html, "Ons kon nie die bladsy laai nie");
        Assert.AreEqual("no-store, no-cache, max-age=0, must-revalidate",
            rendered.HttpContext.Response.Headers["Cache-Control"].ToString());
    }

    private static async Task<string> RenderRendererAsync(LandingPageContent content)
    {
        using var services = new ServiceCollection()
            .AddLogging()
            .BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());

        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var parameters = ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(LandingPageRenderer.Content)] = content
            });
            var rendered = await renderer.RenderComponentAsync<LandingPageRenderer>(parameters);
            return rendered.ToHtmlString();
        });
    }

    private static async Task<(string Html, DefaultHttpContext HttpContext)> RenderPublicPageAsync(
        ILandingPageCatalogService catalog,
        string slug)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(catalog);
        services.AddScoped<LandingPageContentValidator>(_ => new LandingPageContentValidator());
        services.AddScoped<NavigationManager>(_ => new TestNavigationManager());
        services.AddSingleton<IJSRuntime, TestJsRuntime>();
        using var serviceProvider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(
            serviceProvider,
            serviceProvider.GetRequiredService<ILoggerFactory>());

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Scheme = "https";
        httpContext.Request.Host = new HostString("www.schink.example");
        var childContent = (RenderFragment)(builder =>
        {
            builder.OpenComponent<HeadOutlet>(0);
            builder.CloseComponent();
            builder.OpenComponent<LandingPage>(1);
            builder.AddAttribute(2, nameof(LandingPage.Slug), slug);
            builder.CloseComponent();
        });
        var parameters = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(CascadingValue<HttpContext>.Value)] = httpContext,
            [nameof(CascadingValue<HttpContext>.ChildContent)] = childContent
        });

        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var rendered = await renderer.RenderComponentAsync<CascadingValue<HttpContext>>(parameters);
            return rendered.ToHtmlString();
        });
        return (html, httpContext);
    }

    private sealed class FixedLandingPageCatalog(
        PublishedLandingPage? page,
        Exception? failure = null) : ILandingPageCatalogService
    {
        public Task<PublishedLandingPage?> FindPublishedBySlugAsync(
            string? slug,
            CancellationToken cancellationToken = default) =>
            failure is null
                ? Task.FromResult(page)
                : Task.FromException<PublishedLandingPage?>(failure);
    }

    private sealed class TestNavigationManager : NavigationManager
    {
        public TestNavigationManager() =>
            Initialize("https://www.schink.example/", "https://www.schink.example/");

        protected override void NavigateToCore(string uri, bool forceLoad)
        {
            throw new NotSupportedException("Navigation is not used by the static renderer test.");
        }
    }

    private sealed class TestJsRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            ValueTask.FromResult(default(TValue)!);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args) =>
            ValueTask.FromResult(default(TValue)!);
    }
}
