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

    private static string FindRepositoryFile(params string[] pathParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. pathParts]);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        Assert.Fail($"Could not find repository file: {Path.Combine(pathParts)}");
        return string.Empty;
    }
}
