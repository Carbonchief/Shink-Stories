using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Runtime.CompilerServices;

namespace Shink.Tests;

[TestClass]
public class PlaylistAccentColorSourceTests
{
    [TestMethod]
    public void AdminPlaylistEditorPersistsAccentColorHex()
    {
        var adminMarkup = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "Admin.razor"));
        var adminService = File.ReadAllText(GetRepoPath("Shink", "Services", "SupabaseAdminManagementService.cs"));
        var adminContract = File.ReadAllText(GetRepoPath("Shink", "Services", "IAdminManagementService.cs"));

        StringAssert.Contains(adminMarkup, "PlaylistEditor.AccentColorHex");
        StringAssert.Contains(adminMarkup, "@T(\"Luister kleur\", \"Luister color\")");
        StringAssert.Contains(adminMarkup, "type=\"color\"");
        StringAssert.Contains(adminMarkup, "OnPlaylistAccentColorPickerChanged");
        StringAssert.Contains(adminContract, "string? AccentColorHex");
        StringAssert.Contains(adminService, "[\"accent_color_hex\"] = NormalizePlaylistAccentColorHex(request.AccentColorHex)");
        StringAssert.Contains(adminService, "[JsonPropertyName(\"accent_color_hex\")]");
        StringAssert.Contains(adminService, "Gebruik asseblief 'n geldige hex-kleurkode soos #FFAA00.");
    }

    [TestMethod]
    public void LuisterPagesRenderPlaylistAccentColor()
    {
        var playlistModel = File.ReadAllText(GetRepoPath("Shink", "Components", "Content", "StoryPlaylist.cs"));
        var catalogService = File.ReadAllText(GetRepoPath("Shink", "Services", "SupabaseStoryCatalogService.cs"));
        var luister = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "Luister.razor"));
        var luisterCss = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "Luister.razor.css"));
        var playlistPage = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "LuisterPlaylist.razor"));
        var playlistCss = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "LuisterPlaylist.razor.css"));
        var migration = File.ReadAllText(GetRepoPath(
            "Shink",
            "Database",
            "migrations",
            "20260526_story_playlist_accent_color.sql"));

        StringAssert.Contains(playlistModel, "string? AccentColorHex = null");
        StringAssert.Contains(catalogService, "accent_color_hex");
        StringAssert.Contains(catalogService, "AccentColorHex: NormalizePlaylistAccentColorHex");
        StringAssert.Contains(luister, "BuildPlaylistAccentStyle(playlist)");
        StringAssert.Contains(luisterCss, "--playlist-accent-color");
        StringAssert.Contains(playlistPage, "BuildPlaylistAccentStyle(CurrentPlaylist)");
        StringAssert.Contains(playlistCss, "--playlist-accent-color");
        StringAssert.Contains(migration, "accent_color_hex");
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
