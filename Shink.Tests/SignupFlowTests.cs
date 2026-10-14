using System.Reflection;
using System.Security.Claims;
using AngleSharp.Html.Parser;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.HtmlRendering.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shink.Components.Pages;
using Shink.Components.Content;
using Shink.Services;

namespace Shink.Tests;

[TestClass]
public class SignupFlowTests
{
    [TestMethod]
    public void SwitchingFromPaidToFreeClearsCheckoutFromSignInAndSocialLinks()
    {
        var signup = new Signup { ReturnUrl = "/betaal/schink-stories-maandeliks" };
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(Signup).GetProperty("SelectedPlan", flags)!
            .SetValue(signup, PaymentPlanCatalog.FindBySlug("schink-stories-maandeliks"));
        typeof(Signup).GetMethod("SelectGratisPlan", flags)!.Invoke(signup, null);

        Assert.IsTrue((bool)typeof(Signup).GetProperty("IsFreeSignup", flags)!.GetValue(signup)!);
        Assert.AreEqual("/teken-in?returnUrl=%2Fluister", typeof(Signup).GetProperty("SignInHref", flags)!.GetValue(signup));
        Assert.AreEqual("/api/auth/google/start?returnUrl=%2Fluister", typeof(Signup).GetProperty("GoogleSignInHref", flags)!.GetValue(signup));
        Assert.AreEqual("/api/auth/apple/start?returnUrl=%2Fluister", typeof(Signup).GetProperty("AppleSignInHref", flags)!.GetValue(signup));
    }

    [TestMethod]
    public async Task BrowserAutofillSyncPassesOneArrayAndUpdatesTheBoundForm()
    {
        var signup = new Signup();
        var js = new InputSyncJsRuntime();
        typeof(Signup).GetProperty("JS", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(signup, js);

        await (Task)typeof(Signup).GetMethod("SyncSignUpInputsFromDomAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(signup, null)!;

        Assert.HasCount(1, js.Module.Arguments!);
        Assert.IsInstanceOfType<string[]>(js.Module.Arguments![0]);
        var form = typeof(Signup).GetProperty("SignUpForm", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(signup)!;
        Assert.AreEqual("Voorbeeld", form.GetType().GetProperty("FirstName")!.GetValue(form));
        Assert.AreEqual("voorbeeld@example.com", form.GetType().GetProperty("Email")!.GetValue(form));
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("/luister")]
    [DataRow("/luister/suurlemoentjie")]
    public async Task NoSelectedPlanRendersCompactFreeForm(string? returnUrl)
    {
        using var document = new HtmlParser().ParseDocument(await RenderAsync(returnUrl: returnUrl));

        Assert.IsNotNull(document.QuerySelector(".signup-free-card"));
        Assert.IsNull(document.QuerySelector(".signup-card-membership"));
        Assert.AreEqual("Volgende", document.QuerySelector("button[type=submit]")?.TextContent.Trim());
        CollectionAssert.AreEqual(
            new[] { "signup-first-name", "signup-email", "signup-password", "signup-password-confirm" },
            document.QuerySelectorAll("input:not([type=checkbox]):not([type=hidden])")
                .Select(input => input.Id).ToArray());
        Assert.IsNotNull(document.QuerySelector("#signup-email-consent"), "Keep the existing email preference visible.");
        Assert.IsTrue(document.QuerySelector(".signup-free-divider")?.TextContent.Contains("of"));
        Assert.AreEqual(2, document.QuerySelectorAll(".signup-free-social a").Length);
    }

    [TestMethod]
    [DataRow("storie-hoekie-maandeliks", null)]
    [DataRow("schink-stories-maandeliks", null)]
    [DataRow("schink-stories-jaarliks", null)]
    [DataRow(null, "/betaal/schink-stories-maandeliks")]
    [DataRow(null, "%2Fbetaal%2Fschink-stories-jaarliks")]
    public async Task SelectedOrCheckoutPlanKeepsFullSignup(string? plan, string? returnUrl)
    {
        using var document = new HtmlParser().ParseDocument(await RenderAsync(plan: plan, returnUrl: returnUrl));

        Assert.IsNull(document.QuerySelector(".signup-free-card"));
        Assert.IsNotNull(document.QuerySelector(".signup-card-membership"));
        Assert.IsNotNull(document.QuerySelector("#signup-discount-code"));
        StringAssert.Contains(document.QuerySelector(".signup-selected-option")!.TextContent, "R ");
        Assert.AreEqual("Skep rekening", document.QuerySelector("button[type=submit]")?.TextContent.Trim());
    }

    [TestMethod]
    [DataRow("skoolopsies", null, null)]
    [DataRow(null, "skool-klein-jaarliks", null)]
    [DataRow(null, null, "WELCOME")]
    public async Task SchoolAndVoucherEntryPointsKeepTheirExistingFields(string? source, string? plan, string? discountCode)
    {
        using var document = new HtmlParser().ParseDocument(await RenderAsync(plan, source: source, discountCode: discountCode));

        Assert.IsNull(document.QuerySelector(".signup-free-card"));
        Assert.IsNotNull(document.QuerySelector("#signup-discount-code"));
        Assert.IsNotNull(document.QuerySelector("#signup-last-name"));
    }

    [TestMethod]
    [DataRow(null, "/luister")]
    [DataRow("/luister/suurlemoentjie", "/luister/suurlemoentjie")]
    [DataRow("https://example.com", "/luister")]
    [DataRow("//example.com", "/luister")]
    [DataRow("/teken-op", "/luister")]
    [DataRow("/teken-in", "/luister")]
    public async Task SocialProvidersReturnToListeningOrTheRequestedLocalStory(string? returnUrl, string expectedReturnUrl)
    {
        using var document = new HtmlParser().ParseDocument(await RenderAsync(returnUrl: returnUrl));

        foreach (var provider in new[] { "google", "apple" })
        {
            var link = document.QuerySelector($".signup-free-{provider}");
            Assert.AreEqual($"/api/auth/{provider}/start?returnUrl={Uri.EscapeDataString(expectedReturnUrl)}", link?.GetAttribute("href"));
            Assert.AreEqual("false", link?.GetAttribute("data-enhance-nav"));
        }
    }

    private static async Task<string> RenderAsync(
        string? plan = null,
        string? returnUrl = null,
        string? source = null,
        string? discountCode = null)
    {
        var signup = new Signup { Plan = plan, ReturnUrl = returnUrl, Source = source, DiscountCode = discountCode };
        using var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<NavigationManager, SignupNavigationManager>()
            .AddSingleton<AuthenticationStateProvider, SignedOutAuthenticationStateProvider>()
            .AddSingleton<IJSRuntime, NoPrerenderJsRuntime>()
            .AddSingleton(DispatchProxy.Create<ISubscriptionLedgerService, UnusedLedger>())
            .AddSingleton<IComponentActivator>(new SignupActivator(signup))
            .BuildServiceProvider();
        await using var renderer = new SignupStaticRenderer(services, services.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var rendered = renderer.BeginRenderingComponent(typeof(Signup), ParameterView.Empty);
            await rendered.QuiescenceTask;
            return rendered.ToHtmlString();
        });
    }

    private sealed class SignupStaticRenderer(IServiceProvider services, ILoggerFactory loggerFactory)
        : StaticHtmlRenderer(services, loggerFactory)
    {
        protected override IComponent ResolveComponentForRenderMode(
            Type componentType, int? parentComponentId, IComponentActivator componentActivator, IComponentRenderMode renderMode) =>
            componentActivator.CreateInstance(componentType);
    }

    private sealed class SignupActivator(Signup signup) : IComponentActivator
    {
        public IComponent CreateInstance(Type componentType) => componentType == typeof(Signup)
            ? signup
            : (IComponent)Activator.CreateInstance(componentType)!;
    }

    private sealed class SignupNavigationManager : NavigationManager
    {
        public SignupNavigationManager() => Initialize("https://schink.example/", "https://schink.example/teken-op");
        protected override void NavigateToCore(string uri, bool forceLoad) => throw new InvalidOperationException("Signed-out rendering must not redirect.");
    }

    private sealed class SignedOutAuthenticationStateProvider : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
            Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity())));
    }

    private sealed class NoPrerenderJsRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => throw new InvalidOperationException("Prerendering must not require JavaScript.");
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => InvokeAsync<TValue>(identifier, args);
    }

    private sealed class InputSyncJsRuntime : IJSRuntime
    {
        public InputSyncModule Module { get; } = new();
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => ValueTask.FromResult((TValue)(object)Module);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => InvokeAsync<TValue>(identifier, args);
    }

    private sealed class InputSyncModule : IJSObjectReference
    {
        public object?[]? Arguments { get; private set; }
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            Arguments = args;
            var values = args is [string[]]
                ? new Dictionary<string, string> { ["signup-first-name"] = "Voorbeeld", ["signup-email"] = "voorbeeld@example.com" }
                : new Dictionary<string, string>();
            return ValueTask.FromResult((TValue)(object)values);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => InvokeAsync<TValue>(identifier, args);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    public class UnusedLedger : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => throw new InvalidOperationException("Rendering signed-out signup must not access the database.");
    }
}
