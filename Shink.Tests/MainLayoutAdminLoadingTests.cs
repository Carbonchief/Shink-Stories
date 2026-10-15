using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shink.Components.Layout;
using Shink.Services;

namespace Shink.Tests;

[TestClass]
public sealed class MainLayoutAdminLoadingTests
{
    [TestMethod]
    [DataRow("/admin", 0)]
    [DataRow("/admin?language=en", 0)]
    [DataRow("/luister", 1)]
    public async Task AdminLayoutSkipsUnusedBillingPopupCheckAndPreservesSchoolNavigation(string path, int expectedBillingCalls)
    {
        var ledger = DispatchProxy.Create<ISubscriptionLedgerService, AccountLookupProxy>();
        var school = DispatchProxy.Create<ISchoolManagementService, AccountLookupProxy>();
        var layout = new MainLayout();
        Set(layout, "NavigationManager", new LayoutNavigationManager(path));
        Set(layout, "AuthenticationStateProvider", new SignedInStateProvider());
        Set(layout, "SubscriptionLedgerService", ledger);
        Set(layout, "SchoolManagementService", school);

        await (Task)typeof(MainLayout).GetMethod("OnInitializedAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(layout, null)!;

        Assert.AreEqual(expectedBillingCalls, ((AccountLookupProxy)ledger).Calls);
        Assert.AreEqual(1, ((AccountLookupProxy)school).Calls);
    }

    private static void Set(MainLayout layout, string name, object value) =>
        typeof(MainLayout).GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(layout, value);

    public class AccountLookupProxy : DispatchProxy
    {
        public int Calls { get; private set; }
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            Calls++;
            return method!.Name switch
            {
                "GetPaidSubscriptionAttentionAsync" => Task.FromResult(new PaidSubscriptionAttention(false)),
                "HasSchoolAdminAccessAsync" => Task.FromResult(false),
                _ => throw new InvalidOperationException($"Unexpected layout lookup: {method.Name}")
            };
        }
    }

    private sealed class SignedInStateProvider : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(
            new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.Email, "admin@example.test")], "Test"))));
    }

    private sealed class LayoutNavigationManager : NavigationManager
    {
        public LayoutNavigationManager(string path) => Initialize("https://example.test/", "https://example.test" + path);
        protected override void NavigateToCore(string uri, bool forceLoad) => throw new InvalidOperationException("Unexpected navigation");
    }
}
