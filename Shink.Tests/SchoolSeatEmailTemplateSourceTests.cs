using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Shink.Tests;

[TestClass]
public class SchoolSeatEmailTemplateSourceTests
{
    [TestMethod]
    public void SchoolSeatTemplatePreservesDocxCopyAndUsesSafeResendVariables()
    {
        var html = File.ReadAllText(GetRepoPath("resend-templates", "skoolplek-toegeken.html"));
        var text = File.ReadAllText(GetRepoPath("resend-templates", "skoolplek-toegeken.txt"));

        StringAssert.Contains(html, "Geagte {{{RECIPIENT_NAME_HTML}}}");
        StringAssert.Contains(html, "https://www.schink.co.za/branding/Oortjies_School_Email_Hero.png");
        StringAssert.Contains(html, "https://www.schink.co.za/branding/Schink_Stories_Email_Logo.png");
        StringAssert.Contains(html, "alt=\"Schink Stories\"");
        StringAssert.Contains(html, "Jou rekening is reeds geskep.");
        StringAssert.Contains(html, "{{{PASSWORD_SETUP_URL_HTML}}}");
        StringAssert.Contains(html, "{{{LISTEN_URL_HTML}}}");
        StringAssert.Contains(html, "{{{RESOURCES_URL_HTML}}}");
        StringAssert.Contains(html, "Kyk uit vir Oortjies!");
        StringAssert.Contains(text, "{{{PASSWORD_SETUP_URL_TEXT}}}");
        Assert.IsFalse(html.Contains("WAGWOORD-SKAKEL", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(html.Contains("TEMPORARY_PASSWORD", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(html.Contains("temporary password", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void ResendConfigurationUsesThePublishedSchoolSeatTemplateAlias()
    {
        var config = File.ReadAllText(GetRepoPath("Shink", "appsettings.json"));
        using var document = JsonDocument.Parse(config);

        var templateId = document.RootElement
            .GetProperty("Resend")
            .GetProperty("Templates")
            .GetProperty("SchoolSeatNotifications")
            .GetProperty("SeatAssignedTemplateId")
            .GetString();

        Assert.AreEqual("shink-school-seat-assigned", templateId);
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
