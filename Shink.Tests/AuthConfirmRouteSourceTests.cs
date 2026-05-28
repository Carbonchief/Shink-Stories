using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Shink.Tests;

[TestClass]
public class AuthConfirmRouteSourceTests
{
    [TestMethod]
    public void Program_MapsSupabaseTokenHashConfirmRouteToPasswordResetPage()
    {
        var programPath = FindRepositoryFile("Shink", "Program.cs");
        var source = File.ReadAllText(programPath);

        StringAssert.Contains(source, "app.MapGet(\"/auth/confirm\"");
        StringAssert.Contains(source, "ExchangeRecoveryTokenHashAsync");
        StringAssert.Contains(source, "BuildPasswordRecoverySessionRedirectPath");
        StringAssert.Contains(source, "\"/account/update-password\"");
        StringAssert.Contains(source, "\"/herstel-wagwoord\"");
    }

    [TestMethod]
    public void Program_RequiresGoogleStateCookieForImplicitCallback()
    {
        var programPath = FindRepositoryFile("Shink", "Program.cs");
        var source = File.ReadAllText(programPath);

        StringAssert.Contains(source, "UseImplicitFlow: useImplicitFlow");
        StringAssert.Contains(source, "bool UseImplicitFlow");
        StringAssert.Contains(source, "!payload.UseImplicitFlow");
        StringAssert.Contains(source, "BuildPublicAbsoluteRequestUri(siteOptions.Value, httpContext.Request)");
    }

    private static string FindRepositoryFile(params string[] pathParts)
    {
        var testDirectory = Path.GetDirectoryName(GetSourceFilePath())!;
        var candidate = Path.GetFullPath(Path.Combine([testDirectory, "..", .. pathParts]));
        if (File.Exists(candidate))
        {
            return candidate;
        }

        Assert.Fail($"Could not find repository file: {Path.Combine(pathParts)}");
        return string.Empty;
    }

    private static string GetSourceFilePath([CallerFilePath] string path = "") => path;
}
