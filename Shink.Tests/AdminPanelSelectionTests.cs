using System.Collections;
using System.Reflection;
using Microsoft.JSInterop;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MudBlazor;
using Shink.Components.Pages;
using Shink.Services;

namespace Shink.Tests;

[TestClass]
public sealed class AdminPanelSelectionTests
{
    [TestMethod]
    public async Task SwitchingResourceTypeKeepsTheNewestDocumentsWhenTheFirstReadFinishesLast()
    {
        var panel = new AdminResourceTypesPanel();
        var a = new AdminResourceTypeRecord(Guid.NewGuid(), "a", "A", null, 0, true, 0, null);
        var b = a with { ResourceTypeId = Guid.NewGuid(), Slug = "b" };
        var slow = new TaskCompletionSource<IReadOnlyList<AdminResourceDocumentRecord>>();
        IReadOnlyList<AdminResourceDocumentRecord> newest = new List<AdminResourceDocumentRecord>();
        Set(panel, "AdminManagementService", Proxy<IAdminManagementService>((_, args) =>
            (Guid)args[1]! == a.ResourceTypeId ? slow.Task : Task.FromResult(newest)));

        var first = Run(panel, "SelectResourceTypeAsync", a);
        await Run(panel, "SelectResourceTypeAsync", b);
        slow.SetResult([]);
        await first;
        Assert.AreSame(newest, Get(panel, "ResourceDocuments"));
        Assert.AreEqual(b.ResourceTypeId, Get(Get(panel, "Editor")!, "ResourceTypeId"));
    }

    [TestMethod]
    public async Task SwitchingSchoolAndClosingSeatIgnoreLateSchoolAndSeatResponses()
    {
        var panel = new AdminSchoolsPanel();
        Set(panel, "AdminEmail", "admin@example.test");
        var a = new AdminSchoolAccountRecord(Guid.NewGuid(), "A", "", "plan", "Plan", 1, true, "active", 0, 0, 0, default, default, null);
        var b = a with { SchoolAccountId = Guid.NewGuid(), SchoolName = "B" };
        var dashboard = new SchoolDashboardSnapshot(true, null, [], []);
        var oldDashboard = new TaskCompletionSource<SchoolDashboardSnapshot?>();
        var oldStats = new TaskCompletionSource<SchoolSeatStatsSnapshot?>();
        Set(panel, "AdminManagementService", Proxy<IAdminManagementService>((method, args) => method.Name switch
        {
            "GetAdminSchoolDashboardAsync" => (Guid)args[1]! == a.SchoolAccountId ? oldDashboard.Task : Task.FromResult<SchoolDashboardSnapshot?>(dashboard),
            "GetAdminSchoolSeatStatsAsync" => oldStats.Task,
            _ => throw new InvalidOperationException(method.Name)
        }));

        var first = Run(panel, "SelectSchoolAsync", a);
        await Run(panel, "SelectSchoolAsync", b);
        oldDashboard.SetException(new HttpRequestException("Late failure"));
        await first;
        Assert.AreSame(dashboard, Get(panel, "SelectedSchoolDashboard"));
        Assert.IsNull(Get(panel, "ErrorMessage"));

        var seat = new SchoolSeatRecord(Guid.NewGuid(), b.SchoolAccountId, "seat@example.test", null, "member", "accepted", true, default, null, null);
        var stats = Run(panel, "OpenSeatStatsAsync", seat);
        Call(panel, "CloseSeatStats");
        oldStats.SetResult(new(seat, true, null, 1, 1, 0, 0, 0, null, []));
        await stats;
        Assert.IsNull(Get(panel, "SeatStats"));
        Assert.IsNull(Get(panel, "StatsModalSeat"));
    }

    [TestMethod]
    public async Task SavingOneTierUpdatesItsNormalizedPriceAndKeepsSiblingEdits()
    {
        var panel = new AdminSettingsPanel();
        var one = new AdminSubscriptionTypeRecord("one", "One", null, 1, 10, "one", null, true);
        var two = one with { TierCode = "two", PriceZar = 20 };
        var loads = 0;
        Set(panel, "AdminEmail", "admin@example.test");
        Set(panel, "Snackbar", DispatchProxy.Create<ISnackbar, AdminLoadingTests.NoopSnackbarProxy>());
        Set(panel, "AdminManagementService", Proxy<IAdminManagementService>((method, _) =>
        {
            if (method.Name == "SaveSubscriptionTypeAsync")
                return Task.FromResult(new AdminSubscriptionTypeSaveResult(true, SavedTier: one with { PriceZar = 11.11m }));
            Assert.AreEqual("GetSubscriptionSettingsAsync", method.Name);
            loads++;
            return Task.FromResult(new AdminSubscriptionSettingsSnapshot(true, [one, two], []));
        }));
        await Run(panel, "LoadAsync", "admin@example.test", false);
        var tiers = (IList)Get(panel, "SubscriptionTypeEditors")!;
        Set(tiers[0]!, "PriceZar", 11.111m);
        Set(tiers[1]!, "PriceZar", 22m);
        await Run(panel, "SaveSubscriptionTypeAsync", tiers[0]);
        var after = (IList)Get(panel, "SubscriptionTypeEditors")!;
        Assert.AreEqual(1, loads);
        Assert.AreEqual(11.11m, Get(after[0]!, "PriceZar"));
        Assert.AreEqual(22m, Get(after[1]!, "PriceZar"));
    }

    [TestMethod]
    public async Task CancellingBlogSwitchNewAndRefreshKeepsUnpublishedDraft()
    {
        var panel = new AdminBlogPanel();
        var confirms = 0;
        Set(panel, "JSRuntime", Proxy<IJSRuntime>((_, args) =>
        {
            Assert.AreEqual("confirm", args[0]);
            confirms++;
            return ValueTask.FromResult(false);
        }));
        await Run(panel, "BeginCreatePost");
        var draft = Get(panel, "Editor")!;
        Set(draft, "Title", "Keep this draft");
        await Run(panel, "SelectPost", new AdminBlogPostListItem(Guid.NewGuid(), "Other", false, null, default));
        await Run(panel, "BeginCreatePost");
        await Run(panel, "RefreshBlogAsync");
        Assert.AreEqual(3, confirms);
        Assert.AreSame(draft, Get(panel, "Editor"));
    }

    [TestMethod]
    public void StoryAnalyticsReusesResultsUntilFiltersCatalogOrSnapshotChange()
    {
        var page = new Admin();
        var activity = Get(page, "ActivityState")!;
        var snapshot = AdminAnalyticsSnapshot.Empty with
        {
            TopStories = [new("bird", "Bird", 3, 1, 0, 0, 0, DateTimeOffset.Now)]
        };
        Set(activity, "Value", snapshot);
        var list = Get(page, "FilteredAnalyticsStoryDetails");
        Set(page, "SelectedAnalyticsStorySlug", "bird");
        Assert.AreSame(list, Get(page, "FilteredAnalyticsStoryDetails"));
        Set(page, "StoryStatsSearch", "BIRD");
        var filtered = (IReadOnlyList<AdminAnalyticsStoryDetailRecord>)Get(page, "FilteredAnalyticsStoryDetails")!;
        Assert.HasCount(1, filtered);
        Assert.AreNotSame(list, filtered);
        Set(page, "StoryStatsFromDate", DateTime.Today.AddDays(1));
        Assert.IsEmpty((IReadOnlyList<AdminAnalyticsStoryDetailRecord>)Get(page, "FilteredAnalyticsStoryDetails")!);
        Set(page, "StoryStatsFromDate", null);
        Set(activity, "Value", snapshot with { TopStories = [] });
        Assert.IsEmpty((IReadOnlyList<AdminAnalyticsStoryDetailRecord>)Get(page, "FilteredAnalyticsStoryDetails")!);
        Set(page, "Stories", new AdminStoryRecord[] { new(Guid.NewGuid(), "bird", "Catalog bird", null, null, null, [], null, null,
            "", null, null, null, "audio", "free", "published", 0, null, null, null) });
        Assert.AreEqual("Catalog bird", ((IReadOnlyList<AdminAnalyticsStoryDetailRecord>)Get(page, "FilteredAnalyticsStoryDetails")!).Single().Title);
    }

    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static object? Get(object owner, string property) => owner.GetType().GetProperty(property, Members)!.GetValue(owner);
    private static void Set(object owner, string property, object? value) => owner.GetType().GetProperty(property, Members)!.SetValue(owner, value);
    private static object? Call(object owner, string method, params object?[] args) => owner.GetType().GetMethod(method, Members)!.Invoke(owner, args);
    private static Task Run(object owner, string method, params object?[] args) => (Task)Call(owner, method, args)!;

    private static T Proxy<T>(Func<MethodInfo, object?[], object?> invoke) where T : class
    {
        var proxy = DispatchProxy.Create<T, CallbackProxy>();
        ((CallbackProxy)(object)proxy).Callback = invoke;
        return proxy;
    }

    public class CallbackProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[], object?> Callback { get; set; } = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => Callback(method!, args ?? []);
    }
}
