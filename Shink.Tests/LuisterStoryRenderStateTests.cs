using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Runtime.CompilerServices;

namespace Shink.Tests;

[TestClass]
public class LuisterStoryRenderStateTests
{
    [TestMethod]
    public void LuisterStoryKeepsPlayerShellDuringInteractiveStateRestore()
    {
        var source = File.ReadAllText(GetRepoPath(
            "Shink",
            "Components",
            "Pages",
            "LuisterStory.razor"));

        StringAssert.Contains(source, "@inject PersistentComponentState PersistentComponentState");
        StringAssert.Contains(source, "@if (ShouldShowStoryLoading)");
        StringAssert.Contains(source, "private bool ShouldShowStoryLoading => IsStoryLoading && CurrentStory is null;");
        StringAssert.Contains(source, "TryRestorePersistedStoryState();");
        StringAssert.Contains(source, "IsStoryLoading = CurrentStory is null;");
        StringAssert.Contains(source, "PersistentComponentState.PersistAsJson(");
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
