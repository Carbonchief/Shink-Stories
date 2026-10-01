using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MudBlazor;
using Shink.Components.Pages;
using Shink.Services;

namespace Shink.Tests;

[TestClass]
public class AdminLoadingTests
{
    [TestMethod]
    public async Task AuthorizedInitializationShowsSubscribersWithoutFetchingOtherTabs()
    {
        var (page, service) = CreatePage();

        await InvokeAsync(page, "OnInitializedAsync");
        await SelectTabAsync(page, 0);

        CollectionAssert.AreEqual(new[] { "IsAdminAsync" }, service.Calls.ToArray());
        Assert.IsFalse(Get<bool>(page, "IsLoading"));
        Assert.IsFalse(Get<bool>(page, "SubscriberTableReloadPending"));
    }

    [TestMethod]
    public async Task AccessDeniedDoesNotFetchAdminData()
    {
        var (page, service) = CreatePage();
        service.IsAdmin = false;

        await InvokeAsync(page, "OnInitializedAsync");

        Assert.IsTrue(Get<bool>(page, "IsAccessDenied"));
        CollectionAssert.AreEqual(new[] { "IsAdminAsync" }, service.Calls.ToArray());
    }

    [TestMethod]
    public async Task StoryAndPlaylistTabsSharePendingLoadAndReuseCompletedCatalog()
    {
        var (page, service) = CreatePage();
        var stories = new TaskCompletionSource<IReadOnlyList<AdminStoryRecord>>();
        service.Stories = () => stories.Task;

        var first = SelectTabAsync(page, 1);
        Assert.IsTrue(Get<bool>(page, "IsLoadingStoryCatalog"));
        var second = SelectTabAsync(page, 2);
        Assert.AreEqual(1, service.Count("GetStoriesAsync"));
        Assert.AreEqual(1, service.Count("GetPlaylistsAsync"));
        Assert.AreEqual(0, service.Count("GetAnalyticsAsync"));
        Assert.AreEqual(0, service.Count("GetSubscriberReportsAsync"));

        // Returning to Subscribers must not wait for the catalog request.
        Assert.IsTrue(SelectTabAsync(page, 0).IsCompletedSuccessfully);
        stories.SetResult([]);
        await Task.WhenAll(first, second);
        Assert.IsFalse(Get<bool>(page, "IsLoadingStoryCatalog"));
        await SelectTabAsync(page, 1);
        await SelectTabAsync(page, 2);
        Assert.AreEqual(1, service.Count("GetStoriesAsync"));
        Assert.AreEqual(1, service.Count("GetPlaylistsAsync"));
    }

    [TestMethod]
    public async Task AnalyticsLoadsOnSelectionSharesPendingRequestsAndKeepsManualRefresh()
    {
        var (page, service) = CreatePage();
        var reports = new TaskCompletionSource<AdminSubscriberReportsSnapshot>();
        service.Reports = () => reports.Task;

        var first = SelectTabAsync(page, 10);
        var second = SelectTabAsync(page, 10);
        Assert.IsTrue(Get<bool>(page, "IsLoadingAnalyticsTab"));
        Assert.AreEqual(1, service.Count("GetAnalyticsAsync"));
        Assert.AreEqual(1, service.Count("GetSubscriberReportsAsync"));
        Assert.IsTrue(SelectTabAsync(page, 0).IsCompletedSuccessfully);
        reports.SetResult(AdminSubscriberReportsSnapshot.Empty);
        await Task.WhenAll(first, second);
        Assert.IsFalse(Get<bool>(page, "IsLoadingAnalyticsTab"));

        await SelectTabAsync(page, 10);
        Assert.AreEqual(1, service.Count("GetAnalyticsAsync"));
        await InvokeAsync(page, "RefreshAnalyticsAsync");
        Assert.AreEqual(2, service.Count("GetAnalyticsAsync"));
        Assert.AreEqual(2, service.Count("GetSubscriberReportsAsync"));
    }

    [TestMethod]
    public async Task FailedCatalogLoadCanRetryWithoutRefetchingSuccessfulPlaylistLoad()
    {
        var (page, service) = CreatePage();
        service.Stories = () => Task.FromException<IReadOnlyList<AdminStoryRecord>>(new HttpRequestException("Test failure"));

        await SelectTabAsync(page, 1);
        Assert.IsFalse(Get<bool>(page, "HasLoadedStories"));
        Assert.IsFalse(Get<bool>(page, "IsLoadingStoryCatalog"));
        service.Stories = () => Task.FromResult<IReadOnlyList<AdminStoryRecord>>([]);
        await SelectTabAsync(page, 1);

        Assert.IsTrue(Get<bool>(page, "HasLoadedStories"));
        Assert.AreEqual(2, service.Count("GetStoriesAsync"));
        Assert.AreEqual(1, service.Count("GetPlaylistsAsync"));
    }

    [TestMethod]
    public async Task FailedAnalyticsReportsCanRetryOnNextVisit()
    {
        var (page, service) = CreatePage();
        service.Reports = () => Task.FromException<AdminSubscriberReportsSnapshot>(new HttpRequestException("Test failure"));

        await SelectTabAsync(page, 10);
        Assert.IsFalse(Get<bool>(page, "HasLoadedSubscriberReports"));
        Assert.IsFalse(Get<bool>(page, "IsLoadingAnalyticsTab"));
        service.Reports = () => Task.FromResult(AdminSubscriberReportsSnapshot.Empty);
        await SelectTabAsync(page, 10);

        Assert.IsTrue(Get<bool>(page, "HasLoadedSubscriberReports"));
        Assert.AreEqual(2, service.Count("GetSubscriberReportsAsync"));
        Assert.AreEqual(1, service.Count("GetStoriesAsync"));
    }

    private static (Admin Page, AdminServiceProxy Service) CreatePage()
    {
        var service = DispatchProxy.Create<IAdminManagementService, AdminServiceProxy>();
        var page = new Admin();
        Set(page, "AdminManagementService", service);
        Set(page, "Logger", NullLogger<Admin>.Instance);
        Set(page, "Snackbar", DispatchProxy.Create<ISnackbar, NoopSnackbarProxy>());
        Set(page, "AuthenticationStateProvider", new AdminAuthStateProvider());
        Set(page, "NavigationManager", new TestNavigationManager());
        Set(page, "CurrentAdminEmail", "admin@example.test");
        return (page, (AdminServiceProxy)service);
    }

    private static Task SelectTabAsync(Admin page, int index)
    {
        typeof(Admin).GetField("_activeAdminTabIndex", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(page, index);
        return InvokeAsync(page, "EnsureActiveAdminTabDataAsync");
    }

    private static Task InvokeAsync(Admin page, string method) =>
        (Task)typeof(Admin).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, null)!;

    private static T Get<T>(Admin page, string name) =>
        (T)(typeof(Admin).GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(page)
            ?? typeof(Admin).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page))!;

    private static void Set(Admin page, string name, object value) =>
        typeof(Admin).GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(page, value);

    public class AdminServiceProxy : DispatchProxy
    {
        public List<string> Calls { get; } = [];
        public bool IsAdmin { get; set; } = true;
        public Func<Task<IReadOnlyList<AdminStoryRecord>>> Stories { get; set; } = () => Task.FromResult<IReadOnlyList<AdminStoryRecord>>([]);
        public Func<Task<AdminSubscriberReportsSnapshot>> Reports { get; set; } = () => Task.FromResult(AdminSubscriberReportsSnapshot.Empty);
        public int Count(string method) => Calls.Count(call => call == method);

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            Calls.Add(targetMethod!.Name);
            return targetMethod.Name switch
            {
                "IsAdminAsync" => Task.FromResult(IsAdmin),
                "GetStoriesAsync" => Stories(),
                "GetPlaylistsAsync" => Task.FromResult<IReadOnlyList<AdminPlaylistRecord>>([]),
                "GetAnalyticsAsync" => Task.FromResult(AdminAnalyticsSnapshot.Empty),
                "GetSubscriberReportsAsync" => Reports(),
                _ => throw new InvalidOperationException($"Unexpected service call: {targetMethod.Name}")
            };
        }
    }

    public class NoopSnackbarProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => null;
    }

    private sealed class AdminAuthStateProvider : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(
            new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.Email, "admin@example.test")], "Test"))));
    }

    private sealed class TestNavigationManager : NavigationManager
    {
        public TestNavigationManager() => Initialize("https://example.test/", "https://example.test/admin");
        protected override void NavigateToCore(string uri, bool forceLoad) => throw new InvalidOperationException("Unexpected navigation");
        protected override void SetNavigationLockState(bool value) { }
    }
}
