using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Runtime.CompilerServices;

namespace Shink.Tests;

[TestClass]
public class ConsoleNoiseCleanupTests
{
    [TestMethod]
    public void DevelopmentFormActionSourcesDoNotUseBracketedIpv6Loopback()
    {
        var program = File.ReadAllText(GetRepoPath("Shink", "Program.cs"));

        StringAssert.Contains(program, "sources.Add($\"{scheme}://localhost:{port.Value}\");");
        StringAssert.Contains(program, "sources.Add($\"{scheme}://127.0.0.1:{port.Value}\");");
        Assert.IsFalse(
            program.Contains("[::1]", StringComparison.Ordinal),
            "Chrome ignores bracketed IPv6 loopback hosts in the CSP form-action source list and logs a warning.");
    }

    [TestMethod]
    public void BlazorRuntimeStartsWithWarningLogLevel()
    {
        var appShell = File.ReadAllText(GetRepoPath("Shink", "wwwroot", "js", "app-shell.js"));

        StringAssert.Contains(appShell, "script.setAttribute(\"autostart\", \"false\");");
        StringAssert.Contains(appShell, "window.Blazor.start({ logLevel: 3 })");
    }

    [TestMethod]
    public void LuisterPlaylistPagesDoNotUseNativeLazyImages()
    {
        var playlist = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "LuisterPlaylist.razor"));
        var showcase = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "LuisterPlaylistShowcase.razor"));

        Assert.IsFalse(
            playlist.Contains("loading=\"lazy\"", StringComparison.Ordinal),
            "Native lazy images on the playlist page create browser intervention messages in DevTools.");
        Assert.IsFalse(
            showcase.Contains("loading=\"lazy\"", StringComparison.Ordinal),
            "Native lazy images on the showcase page create browser intervention messages in DevTools.");
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
