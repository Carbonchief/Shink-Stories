using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Shink.Tests;

[TestClass]
public class GratisSubscriberEmailSeriesSourceTests
{
    [TestMethod]
    public void GratisSignupUsesAnExplicitOptionalEmailConsentThatDefaultsToOn()
    {
        var signup = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "Signup.razor"));
        var program = File.ReadAllText(GetRepoPath("Shink", "Program.cs"));

        StringAssert.Contains(signup, "<InputCheckbox id=\"signup-email-consent\"");
        StringAssert.Contains(signup, "Ontvang Schink-e-posse");
        StringAssert.Contains(signup, "SelectedPlan is null && SignUpForm.MarketingConsent");
        StringAssert.Contains(signup, "public bool MarketingConsent { get; set; } = true;");
        var consentPropertyIndex = signup.IndexOf("public bool MarketingConsent", StringComparison.Ordinal);
        var consentPropertyWindow = signup[
            Math.Max(0, consentPropertyIndex - 160)..
            Math.Min(signup.Length, consentPropertyIndex + 120)];
        Assert.IsFalse(
            consentPropertyWindow.Contains("[Required", StringComparison.Ordinal),
            "The marketing checkbox must remain optional so a person can opt out.");

        StringAssert.Contains(program, "request.MarketingConsent &&");
        StringAssert.Contains(program, "string.IsNullOrWhiteSpace(request.SelectedTierCode)");
        StringAssert.Contains(program, "HasActivePaidSubscriptionAsync(signedInEmail");
        StringAssert.Contains(program, "gratisSubscriberEmailSequenceService.TryStartAsync(");
    }

    [TestMethod]
    public void PaidActivationPathsStopTheGratisEmailSeries()
    {
        var program = File.ReadAllText(GetRepoPath("Shink", "Program.cs"));
        var mobileEntitlement = File.ReadAllText(GetRepoPath("Shink", "Services", "MobileStoreEntitlementService.cs"));

        Assert.IsGreaterThanOrEqualTo(
            CountOccurrences(program, ".MarkPaidAsync("),
            6,
            "Paystack callbacks, PayFast, recovered checkouts and access codes must all stop the series.");
        StringAssert.Contains(mobileEntitlement, "_gratisSubscriberEmailSequenceService.MarkPaidAsync(email");
    }

    [TestMethod]
    public void EveryGratisEmailTemplateHasOneClickUnsubscribeCopy()
    {
        var templateDirectory = GetRepoPath("resend-templates");
        var templateFiles = Directory.GetFiles(templateDirectory, "oortjies-gratis-*.html");

        Assert.HasCount(6, templateFiles);
        foreach (var templateFile in templateFiles)
        {
            var html = File.ReadAllText(templateFile);
            StringAssert.Contains(html, "href=\"{{{RESEND_UNSUBSCRIBE_URL}}}\"");
            StringAssert.Contains(html, "Teken met een klik uit");
        }
    }

    private static int CountOccurrences(string value, string search)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(search, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += search.Length;
        }

        return count;
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
