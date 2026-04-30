using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Runtime.CompilerServices;

namespace Shink.Tests;

[TestClass]
public class HealthCheckEndpointSourceTests
{
    [TestMethod]
    public void Program_MapsLightweightAzureHealthCheckEndpoint()
    {
        var programSource = File.ReadAllText(GetRepoPath("Shink", "Program.cs"));

        Assert.Contains("app.MapGet(\"/healthz\"", programSource);
        Assert.Contains("httpContext.Response.Headers.CacheControl = \"no-store\";", programSource);
        Assert.Contains("Results.Text(\"ok\", \"text/plain; charset=utf-8\")", programSource);
    }

    private static string GetRepoPath(params string[] segments)
    {
        var testsDirectory = Path.GetDirectoryName(GetSourceFilePath())!;
        var pathSegments = new[] { testsDirectory, ".." }.Concat(segments).ToArray();
        return Path.GetFullPath(Path.Combine(pathSegments));
    }

    private static string GetSourceFilePath([CallerFilePath] string path = "") => path;
}
