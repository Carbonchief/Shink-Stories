using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Shink.Tests;

[TestClass]
public class SecurityHardeningSourceTests
{
    [TestMethod]
    public void DevUiErrorDiagnosticsAreDevelopmentOnly()
    {
        var program = File.ReadAllText(GetRepoPath("Shink", "Program.cs"));

        StringAssert.Contains(program, "if (builder.Environment.IsDevelopment())");
        StringAssert.Contains(program, "builder.Services.AddSingleton<UiErrorDiagnosticsStore>();");
        StringAssert.Contains(program, "builder.Services.AddSingleton<ILoggerProvider, UiErrorDiagnosticsLoggerProvider>();");
        StringAssert.Contains(program, "if (app.Environment.IsDevelopment())");
        StringAssert.Contains(program, "app.MapGet(\"/api/dev/ui-error\"");

        var servicesIndex = program.IndexOf("builder.Services.AddSingleton<UiErrorDiagnosticsStore>();", StringComparison.Ordinal);
        var serviceGuardIndex = program.LastIndexOf("if (builder.Environment.IsDevelopment())", servicesIndex, StringComparison.Ordinal);
        Assert.AreNotEqual(-1, serviceGuardIndex, "The diagnostics services must be inside a Development-only guard.");

        var endpointIndex = program.IndexOf("app.MapGet(\"/api/dev/ui-error\"", StringComparison.Ordinal);
        var endpointGuardIndex = program.LastIndexOf("if (app.Environment.IsDevelopment())", endpointIndex, StringComparison.Ordinal);
        Assert.AreNotEqual(-1, endpointGuardIndex, "The diagnostics endpoint must be inside a Development-only guard.");
    }

    [TestMethod]
    public void AuthAndRecoveryAbsoluteUrlsUseConfiguredPublicBaseUrl()
    {
        var program = File.ReadAllText(GetRepoPath("Shink", "Program.cs"));
        var appsettings = File.ReadAllText(GetRepoPath("Shink", "appsettings.json"));

        StringAssert.Contains(program, "builder.Services.Configure<SiteOptions>(builder.Configuration.GetSection(SiteOptions.SectionName));");
        StringAssert.Contains(program, "BuildPublicAbsoluteUrl(siteOptions.Value, \"/auth/callback\")");
        StringAssert.Contains(program, "BuildPublicAbsoluteUrl(siteOptions.Value, recoveryPath)");
        StringAssert.Contains(program, "BuildPublicAbsoluteUrl(siteOptions.Value, callbackPath)");
        StringAssert.Contains(program, "BuildPublicAbsoluteRequestUri(siteOptions.Value, httpContext.Request)");
        Assert.IsFalse(program.Contains("static string BuildAbsoluteUrl(HttpRequest request, string path)", StringComparison.Ordinal));
        Assert.IsFalse(program.Contains("BuildAbsoluteUrl(httpContext.Request, \"/auth/callback\")", StringComparison.Ordinal));

        StringAssert.Contains(appsettings, "\"Site\"");
        StringAssert.Contains(appsettings, "\"PublicBaseUrl\": \"https://www.schink.co.za\"");
        Assert.IsFalse(appsettings.Contains("\"AllowedHosts\": \"*\"", StringComparison.Ordinal));
    }

    [TestMethod]
    public void SensitiveOperationalExportsAreAbsentAndIgnored()
    {
        var gitignore = File.ReadAllText(GetRepoPath(".gitignore"));

        StringAssert.Contains(gitignore, "/tmp_*.json");
        StringAssert.Contains(gitignore, "/recovered-revenue-accounts-*.csv");

        string[] sensitiveFiles =
        [
            "tmp_events.json",
            "tmp_subs_today.json",
            "tmp_cancels_today.json",
            "tmp_disables_today.json",
            "recovered-revenue-accounts-2026-05-20.csv"
        ];

        foreach (var file in sensitiveFiles)
        {
            Assert.IsFalse(File.Exists(GetRepoPath(file)), $"{file} must not be present in the repository root.");
        }
    }

    [TestMethod]
    public void SignupDiscountRedemptionRpcIsServiceRoleOnly()
    {
        var migration = File.ReadAllText(GetRepoPath("Shink", "Database", "migrations", "20260528_security_hardening.sql"));
        var service = File.ReadAllText(GetRepoPath("Shink", "Services", "SupabaseSubscriptionLedgerService.DiscountCodes.cs"));

        StringAssert.Contains(migration, "create or replace function public.redeem_signup_discount_code(");
        StringAssert.Contains(migration, "for update");
        StringAssert.Contains(migration, "insert into public.subscription_discount_code_redemptions");
        StringAssert.Contains(migration, "revoke all on function public.redeem_signup_discount_code(text, text, text) from public, anon, authenticated;");
        StringAssert.Contains(migration, "grant execute on function public.redeem_signup_discount_code(text, text, text) to service_role;");
        StringAssert.Contains(service, "rest/v1/rpc/redeem_signup_discount_code");
        Assert.IsFalse(service.Contains("Signup discount code redemption history insert failed after access grant", StringComparison.Ordinal));
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
