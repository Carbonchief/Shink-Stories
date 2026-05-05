using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Runtime.CompilerServices;

namespace Shink.Tests;

[TestClass]
public class AdminCharactersPanelSourceTests
{
    [TestMethod]
    public void CharacterBrowseViewsUseGeneratedThumbnails()
    {
        var markup = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "AdminCharactersPanel.razor"));
        var thumbnailsPath = GetRepoPath("Shink", "wwwroot", "branding", "characters", "thumbs");

        StringAssert.Contains(markup, "ResolveCharacterAdminThumbnailPath(character)");
        StringAssert.Contains(markup, "const string characterAssetPrefix = \"/branding/characters/\";");
        StringAssert.Contains(markup, "return $\"{characterAssetPrefix}thumbs/{slug}.webp\";");
        StringAssert.Contains(markup, "fetchpriority=\"low\"");
        StringAssert.Contains(markup, "width=\"56\"");
        StringAssert.Contains(markup, "height=\"56\"");
        StringAssert.Contains(markup, "width=\"192\"");
        StringAssert.Contains(markup, "height=\"192\"");
        Assert.IsFalse(markup.Contains("@character.Slug · @BuildCharacterCategoryLabel(character.CharacterCategory)", StringComparison.Ordinal));

        Assert.IsTrue(Directory.Exists(thumbnailsPath), "Expected generated character thumbnails folder to exist.");
        Assert.IsTrue(Directory.EnumerateFiles(thumbnailsPath, "*.webp").Any(), "Expected generated character thumbnail files.");
    }

    private static string GetRepoPath(params string[] segments)
    {
        var testsDirectory = Path.GetDirectoryName(GetSourceFilePath())!;
        var pathSegments = new[] { testsDirectory, ".." }.Concat(segments).ToArray();
        return Path.GetFullPath(Path.Combine(pathSegments));
    }

    private static string GetSourceFilePath([CallerFilePath] string path = "") => path;
}
