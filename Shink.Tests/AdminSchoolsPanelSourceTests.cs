using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Shink.Tests;

[TestClass]
public class AdminSchoolsPanelSourceTests
{
    [TestMethod]
    public void AdminPageIncludesLocalizedSchoolsTab()
    {
        var markup = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "Admin.razor"));

        StringAssert.Contains(markup, "<MudTabPanel Text='@T(\"Skole\", \"Schools\")'>");
        StringAssert.Contains(markup, "<AdminSchoolsPanel AdminEmail=\"@CurrentAdminEmail\" LanguageCode=\"@CurrentLanguageCode\" />");
    }

    [TestMethod]
    public void SchoolsPanelUsesLocalizedAdminCopyAndFallbackAdminIdentity()
    {
        var markup = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "AdminSchoolsPanel.razor"));
        var css = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "AdminSchoolsPanel.razor.css"));

        StringAssert.Contains(markup, "@T(\"Skole\", \"Schools\")");
        StringAssert.Contains(markup, "@T(\"Skoolnaam\", \"School name\")");
        StringAssert.Contains(markup, "@T(\"Admin e-pos\", \"Admin email\")");
        StringAssert.Contains(markup, "@T(\"Admin gebruik 'n plek\", \"Admin uses a seat\")");
        StringAssert.Contains(markup, "ResolveAdminEmailAsync");
        StringAssert.Contains(markup, "AuthenticationStateProvider.GetAuthenticationStateAsync()");
        StringAssert.Contains(markup, "authState.User.FindFirst(ClaimTypes.Email)?.Value");
        StringAssert.Contains(css, ".admin-schools-layout");
        StringAssert.Contains(css, ".admin-schools-row");
        StringAssert.Contains(css, "@media (max-width: 900px)");
    }

    [TestMethod]
    public void AdminManagementServiceExposesSchoolSetupContract()
    {
        var service = File.ReadAllText(GetRepoPath("Shink", "Services", "IAdminManagementService.cs"));
        var implementation = File.ReadAllText(GetRepoPath("Shink", "Services", "SupabaseAdminManagementService.Schools.cs"));

        StringAssert.Contains(service, "Task<AdminSchoolSetupSnapshot> GetSchoolSetupsAsync");
        StringAssert.Contains(service, "Task<AdminOperationResult> SaveSchoolSetupAsync");
        StringAssert.Contains(service, "public sealed record AdminSchoolSetupSaveRequest");
        StringAssert.Contains(implementation, "rest/v1/school_accounts");
        StringAssert.Contains(implementation, "source_system = \"admin_override\"");
        StringAssert.Contains(implementation, "source_system = \"school_seat\"");
        Assert.IsFalse(implementation.Contains("SendPasswordReset", StringComparison.Ordinal));
        Assert.IsFalse(implementation.Contains("CreateRecoveryEmail", StringComparison.Ordinal));
    }

    [TestMethod]
    public void AdminManagementServiceExposesSchoolSeatAdministrationContract()
    {
        var service = File.ReadAllText(GetRepoPath("Shink", "Services", "IAdminManagementService.cs"));
        var implementation = File.ReadAllText(GetRepoPath("Shink", "Services", "SupabaseAdminManagementService.Schools.cs"));

        StringAssert.Contains(service, "Task<SchoolDashboardSnapshot> GetAdminSchoolDashboardAsync");
        StringAssert.Contains(service, "Task<SchoolOperationResult> InviteAdminSchoolSeatAsync");
        StringAssert.Contains(service, "Task<SchoolOperationResult> RemoveAdminSchoolSeatAsync");
        StringAssert.Contains(service, "Task<SchoolSeatStatsSnapshot?> GetAdminSchoolSeatStatsAsync");
        StringAssert.Contains(implementation, "TryCreateAdminSchoolContextAsync");
        StringAssert.Contains(implementation, "schoolAccountId");
        StringAssert.Contains(implementation, "SchoolInviteTeacherRequest");
        StringAssert.Contains(implementation, "_supabaseAuthService.CreateConfirmedUserWithPasswordAsync");
        AssertLessThan(
            implementation.IndexOf("_supabaseAuthService.CreateConfirmedUserWithPasswordAsync", StringComparison.Ordinal),
            implementation.IndexOf("UpsertAdminSchoolSeatAsync(", StringComparison.Ordinal));
        StringAssert.Contains(implementation, "SchoolSeatStatsSnapshot");
        StringAssert.Contains(implementation, "invited_by_email = context.AdminContext.AdminEmail");
        Assert.IsFalse(implementation.Contains("UpdateAdminSeatUsageAsync", StringComparison.Ordinal));
    }

    [TestMethod]
    public void SchoolsPanelAllowsAdminToManageSeatsAndOpenSeatStats()
    {
        var markup = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "AdminSchoolsPanel.razor"));
        var css = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "AdminSchoolsPanel.razor.css"));

        StringAssert.Contains(markup, "SelectedSchoolDashboard");
        StringAssert.Contains(markup, "GetAdminSchoolDashboardAsync");
        StringAssert.Contains(markup, "InviteAdminSchoolSeatAsync");
        StringAssert.Contains(markup, "@T(\"Tydelike wagwoord\", \"Temporary password\")");
        StringAssert.Contains(markup, "@T(\"Bevestig wagwoord\", \"Confirm password\")");
        StringAssert.Contains(markup, "ValidateInvitePassword()");
        StringAssert.Contains(markup, "new SchoolInviteTeacherRequest(InviteEmail, InviteDisplayName, InvitePassword)");
        StringAssert.Contains(markup, "RemoveAdminSchoolSeatAsync");
        StringAssert.Contains(markup, "GetAdminSchoolSeatStatsAsync");
        StringAssert.Contains(markup, "@T(\"Nooi gebruiker\", \"Invite user\")");
        StringAssert.Contains(markup, "@T(\"Plek-statistiek\", \"Seat stats\")");
        StringAssert.Contains(markup, "OpenSeatStatsAsync(seat)");
        StringAssert.Contains(markup, "RemoveSeatAsync(seat.SchoolSeatId)");
        StringAssert.Contains(markup, "StatsModalSeat");
        StringAssert.Contains(markup, "SeatStats.RecentStories");
        StringAssert.Contains(css, ".admin-schools-seat-list");
        StringAssert.Contains(css, ".admin-schools-seat-stats-grid");
        StringAssert.Contains(css, ".admin-schools-modal-backdrop");
    }

    private static string GetRepoPath(params string[] segments)
    {
        var parts = new[]
        {
            Path.GetDirectoryName(GetSourceFilePath())!,
            ".."
        }.Concat(segments).ToArray();

        return Path.GetFullPath(Path.Combine(parts));
    }

    private static string GetSourceFilePath([CallerFilePath] string path = "") => path;

    private static void AssertLessThan(int left, int right)
    {
        Assert.AreNotEqual(-1, left, "Expected left marker to exist.");
        Assert.AreNotEqual(-1, right, "Expected right marker to exist.");
        Assert.IsLessThan(right, left, $"Expected {left} to appear before {right}.");
    }
}
