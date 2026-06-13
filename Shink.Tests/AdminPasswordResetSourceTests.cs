using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Shink.Tests;

[TestClass]
public class AdminPasswordResetSourceTests
{
    [TestMethod]
    public void SubscriberPasswordResetButtonShowsToastAndIsSpamGuarded()
    {
        var markup = NormalizeLineEndings(File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "Admin.razor")));
        var program = NormalizeLineEndings(File.ReadAllText(GetRepoPath("Shink", "Program.cs")));

        StringAssert.Contains(markup, "OnClick=\"SendSubscriberPasswordResetAsync\"");
        StringAssert.Contains(markup, "Disabled=\"@(IsSavingSubscriberAction || IsSendingSubscriberPasswordReset)\"");
        StringAssert.Contains(markup, "private bool IsSendingSubscriberPasswordReset { get; set; }");
        StringAssert.Contains(markup, "if (IsSendingSubscriberPasswordReset)");
        StringAssert.Contains(markup, "IsSendingSubscriberPasswordReset = true;");
        StringAssert.Contains(markup, "IsSendingSubscriberPasswordReset = false;");
        StringAssert.Contains(markup, "HandleOperationResult(result, T(\"Wagwoordherstel is gestuur.\", \"Password reset sent.\"));");
        StringAssert.Contains(markup, "ShowToast(StatusMessage, Severity.Success);");
        StringAssert.Contains(program, "options.SnackbarConfiguration.PositionClass = Defaults.Classes.Position.TopRight;");
    }

    [TestMethod]
    public void SubscriberBulkPasswordResetButtonIsDisabledWhileSubscriberActionRuns()
    {
        var markup = NormalizeLineEndings(File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "Admin.razor")));

        StringAssert.Contains(markup, "OnClick='() => RunSelectedSubscriberBulkActionAsync(AdminSubscriberBulkAction.SendPasswordReset)'");
        StringAssert.Contains(markup, "Disabled=\"@IsSavingSubscriberAction\"");
        StringAssert.Contains(markup, "if (IsSavingSubscriberAction)");
    }

    [TestMethod]
    public void SubscriberEditModalAllowsAdminForcedPasswordChange()
    {
        var markup = NormalizeLineEndings(File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "Admin.razor")));
        var adminContract = NormalizeLineEndings(File.ReadAllText(GetRepoPath("Shink", "Services", "IAdminManagementService.cs")));
        var adminService = NormalizeLineEndings(File.ReadAllText(GetRepoPath("Shink", "Services", "SupabaseAdminManagementService.cs")));
        var authContract = NormalizeLineEndings(File.ReadAllText(GetRepoPath("Shink", "Services", "ISupabaseAuthService.cs")));

        StringAssert.Contains(markup, "Label='@T(\"Nuwe wagwoord\", \"New password\")'");
        StringAssert.Contains(markup, "Label='@T(\"Bevestig nuwe wagwoord\", \"Confirm new password\")'");
        StringAssert.Contains(markup, "OnClick=\"ForceChangeSubscriberPasswordAsync\"");
        StringAssert.Contains(markup, "private bool IsChangingSubscriberPassword { get; set; }");
        StringAssert.Contains(markup, "T(\"Wagwoord is verander.\", \"Password changed.\")");
        StringAssert.Contains(adminContract, "Task<AdminOperationResult> ForceChangeSubscriberPasswordAsync(");
        StringAssert.Contains(adminContract, "public sealed record AdminSubscriberPasswordChangeRequest(");
        StringAssert.Contains(adminService, "_supabaseAuthService.ForceUpdatePasswordByEmailAsync(");
        StringAssert.Contains(authContract, "Task<SupabasePasswordResetResult> ForceUpdatePasswordByEmailAsync(");
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
