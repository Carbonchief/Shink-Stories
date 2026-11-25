using System.Reflection;
using System.Security.Claims;
using AngleSharp.Html.Parser;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.HtmlRendering.Infrastructure;
using Microsoft.AspNetCore.WebUtilities;
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
        const string plan = "schink-stories-maandeliks";
        const string checkoutPath = "/betaal/" + plan;
        var signup = new Signup { Plan = plan, ReturnUrl = checkoutPath, DiscountCode = "PROMO" };
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(Signup).GetProperty("NavigationManager", flags)!
            .SetValue(signup, new SignupNavigationManager(checkoutPath, plan, method: null));
        typeof(Signup).GetProperty("SelectedPlan", flags)!
            .SetValue(signup, PaymentPlanCatalog.FindBySlug(plan));
        var form = typeof(Signup).GetProperty("SignUpForm", flags)!.GetValue(signup)!;
        var discount = form.GetType().GetProperty("DiscountCode")!;
        discount.SetValue(form, "PROMO");
        typeof(Signup).GetMethod("SelectGratisPlan", flags)!.Invoke(signup, null);

        Assert.AreEqual(string.Empty, discount.GetValue(form));
        Assert.IsTrue((bool)typeof(Signup).GetProperty("IsFreeSignup", flags)!.GetValue(signup)!);
        Assert.AreEqual("/teken-in?returnUrl=%2Fluister", typeof(Signup).GetProperty("SignInHref", flags)!.GetValue(signup));
        Assert.AreEqual("/api/auth/google/start?returnUrl=%2Fluister", typeof(Signup).GetProperty("GoogleSignInHref", flags)!.GetValue(signup));
        Assert.AreEqual("/api/auth/apple/start?returnUrl=%2Fluister", typeof(Signup).GetProperty("AppleSignInHref", flags)!.GetValue(signup));
        Assert.AreEqual("https://schink.example/teken-op", typeof(Signup).GetProperty("SignupChoicesHref", flags)!.GetValue(signup));
    }

    [TestMethod]
    public async Task SignupFromDiscountedCheckoutRetainsTheCodeFromTheSignInReturnUrl()
    {
        using var document = new HtmlParser().ParseDocument(await RenderAsync(
            returnUrl: "/betaal/schink-stories-maandeliks?discountCode=PROMO&returnUrl=%2Fluister%2Fstorie"));
        Assert.AreEqual("PROMO", document.QuerySelector("#signup-discount-code")?.GetAttribute("value"));
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
    public async Task FreeSignupOffersThreeMethodsBeforeShowingTheEmailForm(string? returnUrl)
    {
        var parser = new HtmlParser();
        using var choices = parser.ParseDocument(await RenderAsync(returnUrl: returnUrl));
        using var emailForm = parser.ParseDocument(await RenderAsync(returnUrl: returnUrl, selectEmail: true));

        Assert.IsNotNull(choices.QuerySelector(".signup-free-card"));
        Assert.IsNull(choices.QuerySelector(".signup-card-membership"));
        CollectionAssert.AreEqual(
            new[] { "Gaan voort met Google", "Gaan voort met Apple", "Gaan voort met e-pos" },
            choices.QuerySelectorAll(".signup-free-methods .signup-free-provider").Select(method => method.TextContent.Trim()).ToArray());
        var emailLink = choices.QuerySelector("a.signup-free-email");
        Assert.IsNotNull(emailLink);
        var emailUri = new Uri(emailLink.GetAttribute("href")!);
        Assert.AreEqual("/teken-op", emailUri.AbsolutePath);
        var emailQuery = QueryHelpers.ParseQuery(emailUri.Query);
        Assert.AreEqual("email", emailQuery["method"].ToString());
        Assert.AreEqual(returnUrl ?? string.Empty, emailQuery.GetValueOrDefault("returnUrl").ToString());
        Assert.IsNull(choices.QuerySelector("input:not([type=hidden]), button[type=submit]"));
        Assert.IsNull(choices.QuerySelector(".signup-free-back"));

        Assert.IsNull(emailForm.QuerySelector(".signup-free-provider"));
        var backLink = emailForm.QuerySelector("a.signup-free-back");
        Assert.IsNotNull(backLink);
        Assert.AreEqual("Terug", backLink.TextContent.Trim());
        var backUri = new Uri(backLink.GetAttribute("href")!);
        Assert.AreEqual("/teken-op", backUri.AbsolutePath);
        var backQuery = QueryHelpers.ParseQuery(backUri.Query);
        Assert.IsFalse(backQuery.ContainsKey("method"));
        Assert.AreEqual(returnUrl ?? string.Empty, backQuery.GetValueOrDefault("returnUrl").ToString());
        Assert.AreEqual("Volgende", emailForm.QuerySelector("button[type=submit]")?.TextContent.Trim());
        CollectionAssert.AreEqual(
            new[] { "signup-first-name", "signup-email", "signup-password", "signup-password-confirm" },
            emailForm.QuerySelectorAll("input:not([type=checkbox]):not([type=hidden])")
                .Select(input => input.Id).ToArray());
        Assert.IsNotNull(emailForm.QuerySelector("#signup-email-consent"), "Keep the existing email preference visible.");
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("schink-stories-maandeliks")]
    public async Task PasswordControlsStartMaskedWithAccessibleRequirements(string? plan)
    {
        using var document = new HtmlParser().ParseDocument(await RenderAsync(plan: plan, selectEmail: plan is null));
        foreach (var id in new[] { "signup-password", "signup-password-confirm" })
        {
            var input = document.QuerySelector($"#{id}")!;
            Assert.AreEqual("password", input.GetAttribute("type"));
            Assert.AreEqual("new-password", input.GetAttribute("autocomplete"));
            Assert.IsTrue(input.ClassList.Contains("ph-no-capture"));
            var help = document.GetElementById(input.GetAttribute("aria-describedby")!);
            Assert.IsNotNull(help, "Instructions must be associated with the input.");
            var toggle = document.QuerySelector($"button[aria-controls='{id}']")!;
            Assert.AreEqual("button", toggle.GetAttribute("type"), "Revealing a password must never submit the form.");
            StringAssert.StartsWith(toggle.GetAttribute("aria-label")!, "Wys ");
        }

        StringAssert.Contains(document.GetElementById("signup-password-help")!.TextContent, "6 tot 200");
        StringAssert.Contains(document.GetElementById("signup-password-confirm-help")!.TextContent, "dieselfde wagwoord");
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("schink-stories-maandeliks")]
    public async Task PasswordVisibilityTogglesIndependentlyWithoutChangingValues(string? plan)
    {
        using var document = new HtmlParser().ParseDocument(await RenderAsync(plan: plan, selectEmail: plan is null, afterRender: signup =>
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var form = typeof(Signup).GetProperty("SignUpForm", flags)!.GetValue(signup)!;
            form.GetType().GetProperty("Password")!.SetValue(form, "Voorbeeld123");
            form.GetType().GetProperty("ConfirmPassword")!.SetValue(form, "Voorbeeld123");

            // Reveal, then hide the original; leave confirmation revealed.
            typeof(Signup).GetMethod("TogglePasswordVisibility", flags)!.Invoke(signup, null);
            typeof(Signup).GetMethod("ToggleConfirmPasswordVisibility", flags)!.Invoke(signup, null);
            typeof(Signup).GetMethod("TogglePasswordVisibility", flags)!.Invoke(signup, null);
        }));

        Assert.AreEqual("password", document.GetElementById("signup-password")!.GetAttribute("type"));
        Assert.AreEqual("text", document.GetElementById("signup-password-confirm")!.GetAttribute("type"));
        foreach (var id in new[] { "signup-password", "signup-password-confirm" })
        {
            var input = document.GetElementById(id)!;
            Assert.AreEqual("Voorbeeld123", input.GetAttribute("value"));
            Assert.IsTrue(input.ClassList.Contains("ph-no-capture"), "Revealing must preserve the analytics exclusion.");
        }
        Assert.AreEqual("Versteek bevestigde wagwoord", document.QuerySelector("button[aria-controls='signup-password-confirm']")!.GetAttribute("aria-label"));
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
        string? discountCode = null,
        bool selectEmail = false,
        Action<Signup>? afterRender = null)
    {
        var signup = new Signup
        {
            Plan = plan,
            ReturnUrl = returnUrl,
            Source = source,
            DiscountCode = discountCode,
            SignupMethod = selectEmail ? "email" : null
        };
        using var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<NavigationManager>(new SignupNavigationManager(returnUrl, plan, signup.SignupMethod))
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
            if (afterRender is not null)
            {
                afterRender(signup);
                typeof(ComponentBase).GetMethod("StateHasChanged", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(signup, null);
            }
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
        public SignupNavigationManager(string? returnUrl, string? plan, string? method)
        {
            const string signupUrl = "https://schink.example/teken-op";
            var query = new Dictionary<string, string?>
            {
                ["returnUrl"] = returnUrl,
                ["plan"] = plan,
                ["method"] = method
            };
            Initialize("https://schink.example/", QueryHelpers.AddQueryString(signupUrl, query));
        }

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
