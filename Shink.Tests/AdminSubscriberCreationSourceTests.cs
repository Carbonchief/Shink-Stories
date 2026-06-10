using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Shink.Tests;

[TestClass]
public class AdminSubscriberCreationSourceTests
{
    [TestMethod]
    public void AdminCreateSubscriberRequiresPasswordAndCreatesSupabaseAuthUser()
    {
        var markup = NormalizeLineEndings(File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "Admin.razor")));
        var contract = NormalizeLineEndings(File.ReadAllText(GetRepoPath("Shink", "Services", "IAdminManagementService.cs")));
        var adminService = NormalizeLineEndings(File.ReadAllText(GetRepoPath("Shink", "Services", "SupabaseAdminManagementService.cs")));
        var authContract = NormalizeLineEndings(File.ReadAllText(GetRepoPath("Shink", "Services", "ISupabaseAuthService.cs")));
        var authService = NormalizeLineEndings(File.ReadAllText(GetRepoPath("Shink", "Services", "SupabaseAuthService.cs")));

        StringAssert.Contains(markup, "Label='@T(\"Wagwoord\", \"Password\")'");
        StringAssert.Contains(markup, "Label='@T(\"Bevestig wagwoord\", \"Confirm password\")'");
        StringAssert.Contains(markup, "@bind-Value=\"NewSubscriberPassword\"");
        StringAssert.Contains(markup, "@bind-Value=\"NewSubscriberPasswordConfirmation\"");
        StringAssert.Contains(markup, "ValidateNewSubscriberPassword()");
        StringAssert.Contains(markup, "NewSubscriberPassword,");

        StringAssert.Contains(contract, "string? Password);");
        StringAssert.Contains(authContract, "CreateConfirmedUserWithPasswordAsync");
        StringAssert.Contains(authService, "public async Task<SupabaseSignInResult> CreateConfirmedUserWithPasswordAsync");
        StringAssert.Contains(adminService, "_supabaseAuthService.CreateConfirmedUserWithPasswordAsync");
        AssertLessThan(
            adminService.IndexOf("_supabaseAuthService.CreateConfirmedUserWithPasswordAsync", StringComparison.Ordinal),
            adminService.IndexOf("rest/v1/subscribers?on_conflict=email&select=subscriber_id", StringComparison.Ordinal));
    }

    private static void AssertLessThan(int left, int right)
    {
        Assert.AreNotEqual(-1, left, "Expected left marker to exist.");
        Assert.AreNotEqual(-1, right, "Expected right marker to exist.");
        Assert.IsLessThan(right, left, $"Expected {left} to appear before {right}.");
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

    private static string NormalizeLineEndings(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string GetSourceFilePath([CallerFilePath] string path = "") => path;
}
