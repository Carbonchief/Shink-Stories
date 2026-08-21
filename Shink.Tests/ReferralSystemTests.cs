using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shink.Services;

namespace Shink.Tests;

[TestClass]
public class ReferralSystemTests
{
    [TestMethod]
    public void GeneratedReferralCodes_AreRandomLookingAndRoundTripThroughNormalization()
    {
        var code = ReferralCodeRules.Generate();

        Assert.AreEqual(ReferralCodeRules.CodeLength, code.Length);
        Assert.IsTrue(code.StartsWith("RF", StringComparison.Ordinal));
        Assert.AreEqual(code, ReferralCodeRules.Normalize($"  {code.ToLowerInvariant()}  "));
    }

    [TestMethod]
    public void ReferralCodeNormalization_RejectsInvalidCodes()
    {
        foreach (var code in new[] { string.Empty, "RF123", "RF123456789!", "XX1234567890" })
        {
            Assert.IsNull(ReferralCodeRules.Normalize(code));
        }
    }

    [TestMethod]
    public void SignupCarriesReferralCodeIntoSupabaseAuthMetadata()
    {
        var signup = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "Signup.razor"));
        var program = File.ReadAllText(GetRepoPath("Shink", "Program.cs"));
        var authService = File.ReadAllText(GetRepoPath("Shink", "Services", "SupabaseAuthService.cs"));

        StringAssert.Contains(signup, "[SupplyParameterFromQuery(Name = \"ref\")]");
        StringAssert.Contains(signup, "ReferralCode: ReferralCode");
        StringAssert.Contains(program, "ReferralCode = ReferralCodeRules.Normalize(request.ReferralCode)");
        StringAssert.Contains(authService, "[property: JsonPropertyName(\"referral_code\")] string? ReferralCode");
    }

    [TestMethod]
    public void ReferralMigration_LocksOneReferralToEachNewAuthUser()
    {
        var sql = File.ReadAllText(GetRepoPath("Shink", "Database", "migrations", "20260820_referrals.sql"));

        StringAssert.Contains(sql, "create table if not exists public.referral_codes");
        StringAssert.Contains(sql, "create table if not exists public.referral_signups");
        StringAssert.Contains(sql, "references auth.users(id)");
        StringAssert.Contains(sql, "unique (referred_user_id)");
        StringAssert.Contains(sql, "security definer");
        StringAssert.Contains(sql, "set search_path = public, pg_temp");
        StringAssert.Contains(sql, "after insert on auth.users");
        StringAssert.Contains(sql, "on conflict (referred_user_id) do nothing");
        StringAssert.Contains(sql, "alter table public.referral_codes enable row level security");
        StringAssert.Contains(sql, "revoke all on table public.referral_codes, public.referral_signups from anon, authenticated");
        StringAssert.Contains(sql, "grant execute on function public.admin_referral_codes_summary() to service_role");
    }

    [TestMethod]
    public void AdminPageIncludesLocalizedReferralsPanel()
    {
        var admin = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "Admin.razor"));
        var panel = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "AdminReferralsPanel.razor"));
        var css = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "AdminReferralsPanel.razor.css"));

        StringAssert.Contains(admin, "<MudTabPanel Text='@T(\"Verwysings\", \"Referrals\")'>");
        StringAssert.Contains(admin, "<AdminReferralsPanel AdminEmail=\"@CurrentAdminEmail\" LanguageCode=\"@CurrentLanguageCode\" />");
        StringAssert.Contains(panel, "@T(\"Nuwe verwysingskakel\", \"New referral link\")");
        StringAssert.Contains(panel, "@T(\"E-pos opsioneel\", \"Email optional\")");
        StringAssert.Contains(panel, "BuildReferralLink(referral.Code)");
        StringAssert.Contains(panel, "ref={Uri.EscapeDataString(code)}");
        StringAssert.Contains(css, ".admin-referrals-panel.admin-section");
        StringAssert.Contains(css, "background: var(--admin-surface)");
        StringAssert.Contains(css, "background: var(--admin-surface-soft)");
        StringAssert.Contains(css, "background: var(--admin-input-bg)");
        StringAssert.Contains(css, ".admin-referrals-form-grid");
        StringAssert.Contains(css, "@media (max-width: 760px)");
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
}
