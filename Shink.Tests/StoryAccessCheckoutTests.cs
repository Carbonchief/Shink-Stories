using System.Security.Claims;
using System.Reflection;
using AngleSharp.Html.Parser;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shink.Components.Content;
using Shink.Components.Pages;

namespace Shink.Tests;

[TestClass]
public sealed class StoryAccessCheckoutTests
{
    [TestMethod]
    [DataRow("schink-stories-maandeliks", 79)]
    [DataRow("schink-stories-jaarliks", 790)]
    public void SignedInPlanGoesToPaystackCheckoutWithOriginalStory(string slug, int amount)
    {
        var plan = PaymentPlanCatalog.FindBySlug(slug)!;
        const string storyPath = "/luister/die-ware-wenner?playlist=gunstelinge";
        var href = StoryAccessCheckoutLinks.BuildCheckoutHref(plan, true, storyPath);
        var uri = new Uri(new Uri("https://www.schink.example"), href);
        var query = QueryHelpers.ParseQuery(uri.Query);

        Assert.AreEqual($"/betaal/{slug}", uri.AbsolutePath);
        Assert.AreEqual("paystack", query["provider"].ToString());
        Assert.AreEqual(storyPath, query["returnUrl"].ToString());
        Assert.AreEqual((decimal)amount, plan.Amount);
    }

    [TestMethod]
    [DataRow("schink-stories-maandeliks")]
    [DataRow("schink-stories-jaarliks")]
    public void AnonymousPlanPreservesPaystackAndStoryThroughAccountCreation(string slug)
    {
        const string storyPath = "/luister/die-ware-wenner";
        var href = StoryAccessCheckoutLinks.BuildCheckoutHref(PaymentPlanCatalog.FindBySlug(slug)!, false, storyPath);
        var uri = new Uri(new Uri("https://www.schink.example"), href);
        var signupQuery = QueryHelpers.ParseQuery(uri.Query);
        var checkoutUri = new Uri(new Uri("https://www.schink.example"), signupQuery["returnUrl"].ToString());
        var checkoutQuery = QueryHelpers.ParseQuery(checkoutUri.Query);

        Assert.AreEqual("/teken-op", uri.AbsolutePath);
        Assert.AreEqual(slug, signupQuery["plan"].ToString());
        Assert.AreEqual($"/betaal/{slug}", checkoutUri.AbsolutePath);
        Assert.AreEqual("paystack", checkoutQuery["provider"].ToString());
        Assert.AreEqual(storyPath, checkoutQuery["returnUrl"].ToString());
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("https://other.example/luister/storie")]
    [DataRow("//other.example/luister/storie")]
    [DataRow("/\\other.example/luister/storie")]
    [DataRow("/betaal/schink-stories-maandeliks")]
    [DataRow("/teken-in")]
    [DataRow("/kry-toegang")]
    [DataRow("/luister/stor\rie")]
    public void InvalidOrLoopingReturnTargetsAreNotForwarded(string? returnUrl)
    {
        var href = StoryAccessCheckoutLinks.BuildCheckoutHref(
            PaymentPlanCatalog.FindBySlug("schink-stories-maandeliks")!, true, returnUrl);
        Assert.AreEqual("/betaal/schink-stories-maandeliks?provider=paystack", href);
        Assert.AreEqual("/kry-toegang", StoryAccessCheckoutLinks.BuildPageHref(returnUrl));
        Assert.AreEqual("/teken-in?returnUrl=%2Fluister", StoryAccessCheckoutLinks.BuildSignInHref(returnUrl));
    }

    [TestMethod]
    public void PopupDestinationPreservesStoryAndPlaylistQuery()
    {
        const string story = "/luister/die-ware-wenner?playlist=nuwe-stories";
        var uri = new Uri(new Uri("https://www.schink.example"), StoryAccessCheckoutLinks.BuildPageHref(story));
        Assert.AreEqual("/kry-toegang", uri.AbsolutePath);
        Assert.AreEqual(story, QueryHelpers.ParseQuery(uri.Query)["returnUrl"].ToString());
        var signInUri = new Uri(new Uri("https://www.schink.example"), StoryAccessCheckoutLinks.BuildSignInHref(story));
        Assert.AreEqual("/teken-in", signInUri.AbsolutePath);
        Assert.AreEqual(story, QueryHelpers.ParseQuery(signInUri.Query)["returnUrl"].ToString());
    }

    [TestMethod]
    [DataRow(typeof(Home))]
    [DataRow(typeof(Luister))]
    [DataRow(typeof(LuisterStory))]
    [DataRow(typeof(LuisterPlaylist))]
    [DataRow(typeof(LuisterPlaylistShowcase))]
    public void LockedStoryPopupOffersAccessOptionsAndSignIn(Type pageType)
    {
        var page = Activator.CreateInstance(pageType)!;
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        const string storyPath = "/luister/die-ware-wenner";
        pageType.GetProperty("PendingLockedStoryPath", flags)!.SetValue(page, storyPath);

        var expected = StoryAccessCheckoutLinks.BuildPageHref(storyPath);
        Assert.AreEqual(expected, pageType.GetProperty("StoryAccessPopupPrimaryHref", flags)!.GetValue(page));
        Assert.AreEqual(StoryAccessCheckoutLinks.BuildSignInHref(storyPath),
            pageType.GetProperty("StoryAccessPopupSignInHref", flags)!.GetValue(page));
    }

    [TestMethod]
    public void AnonymousFreeStoryKeepsSignInInsteadOfRequiringPayment()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var home = new Home();
        typeof(Home).GetProperty("PendingLockedStoryPath", flags)!.SetValue(home, "/gratis/gratis-storie");
        StringAssert.StartsWith((string)typeof(Home).GetProperty("StoryAccessPopupPrimaryHref", flags)!.GetValue(home)!, "/teken-in?");
        Assert.AreEqual(string.Empty, typeof(Home).GetProperty("StoryAccessPopupSignInHref", flags)!.GetValue(home));

        var storyPage = new LuisterStory();
        typeof(LuisterStory).GetProperty("PendingLockedStoryPath", flags)!.SetValue(storyPage, "/luister/gratis-storie");
        typeof(LuisterStory).GetProperty("CurrentStory", flags)!.SetValue(storyPage,
            new StoryItem("gratis-storie", "Gratis storie", "", "cover.png", "story.mp3", AccessLevel: "free"));
        StringAssert.StartsWith((string)typeof(LuisterStory).GetProperty("StoryAccessPopupPrimaryHref", flags)!.GetValue(storyPage)!, "/teken-in?");
        Assert.AreEqual(string.Empty, typeof(LuisterStory).GetProperty("StoryAccessPopupSignInHref", flags)!.GetValue(storyPage));
    }

    [TestMethod]
    public async Task LockedStoryPopup_RendersAccessButtonAndSignInLink()
    {
        const string storyPath = "/luister/die-ware-wenner?playlist=nuwe-stories";
        var html = await RenderPopupAsync(new()
        {
            [nameof(StoryAccessPopup.IsOpen)] = true,
            [nameof(StoryAccessPopup.PrimaryActionHref)] = StoryAccessCheckoutLinks.BuildPageHref(storyPath),
            [nameof(StoryAccessPopup.SignInHref)] = StoryAccessCheckoutLinks.BuildSignInHref(storyPath)
        });
        var document = await new HtmlParser().ParseDocumentAsync(html);
        var accessButton = Assert.ContainsSingle(document.QuerySelectorAll("a.story-access-popup-button"));
        Assert.AreEqual(StoryAccessCheckoutLinks.BuildPageHref(storyPath), accessButton.GetAttribute("href"));
        Assert.Contains("Kry toegang", accessButton.TextContent);
        var signInLink = Assert.ContainsSingle(document.QuerySelectorAll("a.story-access-popup-sign-in"));
        Assert.AreEqual(StoryAccessCheckoutLinks.BuildSignInHref(storyPath), signInLink.GetAttribute("href"));
        Assert.Contains("Teken in", signInLink.TextContent);
        Assert.IsEmpty(document.QuerySelectorAll(".story-access-popup-button-secondary"));
    }

    [TestMethod]
    public async Task AccountRepairPopup_PreservesBothPostActions()
    {
        var html = await RenderPopupAsync(new()
        {
            [nameof(StoryAccessPopup.IsOpen)] = true,
            [nameof(StoryAccessPopup.PrimaryActionHref)] = "/rekening/herstel-intekening",
            [nameof(StoryAccessPopup.PrimaryActionText)] = "Maak rekening reg",
            [nameof(StoryAccessPopup.PrimaryActionUsesPost)] = true,
            [nameof(StoryAccessPopup.PlanActionHref)] = "/rekening/skuif-na-gratis",
            [nameof(StoryAccessPopup.PlanActionText)] = "Skuif na gratis",
            [nameof(StoryAccessPopup.PlanActionUsesPost)] = true
        });
        var document = await new HtmlParser().ParseDocumentAsync(html);
        var forms = document.QuerySelectorAll("form[method='post']");
        Assert.HasCount(2, forms);
        Assert.AreEqual("/rekening/herstel-intekening", forms[0].GetAttribute("action"));
        Assert.AreEqual("/rekening/skuif-na-gratis", forms[1].GetAttribute("action"));
        Assert.Contains("Maak rekening reg", forms[0].TextContent);
        Assert.Contains("Skuif na gratis", forms[1].TextContent);
        Assert.IsEmpty(document.QuerySelectorAll(".story-access-popup-sign-in"));
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task RenderedPageHasOnlyTwoFullAccessPlansWithCorrectCheckoutLinks(bool isAuthenticated)
    {
        using var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<AuthenticationStateProvider>(new FixedAuthenticationStateProvider(isAuthenticated))
            .BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<KryToegang>()).ToHtmlString());
        var document = await new HtmlParser().ParseDocumentAsync(html);
        Assert.HasCount(7, document.QuerySelectorAll(".access-benefits li"));
        var layouts = document.QuerySelectorAll(".access-plans");
        Assert.HasCount(1, layouts);
        foreach (var layout in layouts)
        {
            var plans = layout.QuerySelectorAll("a.access-plan");
            Assert.HasCount(2, plans);
            StringAssert.Contains(plans[0].GetAttribute("aria-label")!, "R79 per maand");
            StringAssert.Contains(plans[1].GetAttribute("aria-label")!, "R790 per jaar");
            StringAssert.Contains(layout.TextContent, "2 maande");
            foreach (var (link, slug) in plans.Zip(new[] { "schink-stories-maandeliks", "schink-stories-jaarliks" }))
            {
                Assert.AreEqual("false", link.GetAttribute("data-enhance-nav"));
                Assert.AreEqual(StoryAccessCheckoutLinks.BuildCheckoutHref(PaymentPlanCatalog.FindBySlug(slug)!, isAuthenticated, null),
                    link.GetAttribute("href"));
            }
        }
    }

    private static async Task<string> RenderPopupAsync(Dictionary<string, object?> parameters)
    {
        using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<StoryAccessPopup>(ParameterView.FromDictionary(parameters))).ToHtmlString());
    }

    private sealed class FixedAuthenticationStateProvider(bool isAuthenticated) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
            Task.FromResult(new AuthenticationState(new ClaimsPrincipal(
                isAuthenticated ? new ClaimsIdentity([new Claim(ClaimTypes.Name, "Test ouer")], "test") : new ClaimsIdentity())));
    }
}
