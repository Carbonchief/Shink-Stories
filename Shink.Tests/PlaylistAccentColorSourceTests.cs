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
        StringAssert.Contains(adminMarkup, "PlaylistEditor.AccentColorEndHex");
        StringAssert.Contains(adminMarkup, "PlaylistEditor.FontColorHex");
        StringAssert.Contains(adminMarkup, "@T(\"Luister kleur 1\", \"Luister color 1\")");
        StringAssert.Contains(adminMarkup, "@T(\"Luister kleur 2\", \"Luister color 2\")");
        StringAssert.Contains(adminMarkup, "@T(\"Showcase font kleur\", \"Showcase font color\")");
        StringAssert.Contains(adminMarkup, "type=\"color\"");
        StringAssert.Contains(adminMarkup, "OnPlaylistAccentColorPickerChanged");
        StringAssert.Contains(adminMarkup, "OnPlaylistAccentEndColorPickerChanged");
        StringAssert.Contains(adminMarkup, "OnPlaylistFontColorPickerChanged");
        StringAssert.Contains(adminContract, "string? AccentColorHex");
        StringAssert.Contains(adminContract, "string? AccentColorEndHex");
        StringAssert.Contains(adminContract, "string? FontColorHex");
        StringAssert.Contains(adminService, "[\"accent_color_hex\"] = normalizedAccentColorHex");
        StringAssert.Contains(adminService, "[\"accent_color_end_hex\"] = normalizedAccentColorEndHex");
        StringAssert.Contains(adminService, "[\"font_color_hex\"] = normalizedFontColorHex");
        StringAssert.Contains(adminService, "[JsonPropertyName(\"accent_color_hex\")]");
        StringAssert.Contains(adminService, "[JsonPropertyName(\"accent_color_end_hex\")]");
        StringAssert.Contains(adminService, "[JsonPropertyName(\"font_color_hex\")]");
        StringAssert.Contains(adminService, "Gebruik asseblief 'n geldige hex-kleurkode soos #FFAA00.");
        StringAssert.Contains(adminService, "Gebruik asseblief 'n geldige tweede hex-kleurkode soos #88CCFF.");
        StringAssert.Contains(adminService, "Gebruik asseblief 'n geldige font hex-kleurkode soos #222222.");
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
        var showcasePage = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "LuisterPlaylistShowcase.razor"));
        var showcaseCss = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "LuisterPlaylistShowcase.razor.css"));
        var migration = File.ReadAllText(GetRepoPath(
            "Shink",
            "Database",
            "migrations",
            "20260526_story_playlist_accent_color.sql"));
        var gradientMigration = File.ReadAllText(GetRepoPath(
            "Shink",
            "Database",
            "migrations",
            "20260531_story_playlist_gradient_accent_color.sql"));
        var fontMigration = File.ReadAllText(GetRepoPath(
            "Shink",
            "Database",
            "migrations",
            "20260603_story_playlist_font_color.sql"));

        StringAssert.Contains(playlistModel, "string? AccentColorHex = null");
        StringAssert.Contains(playlistModel, "string? AccentColorEndHex = null");
        StringAssert.Contains(playlistModel, "string? FontColorHex = null");
        StringAssert.Contains(catalogService, "accent_color_hex");
        StringAssert.Contains(catalogService, "accent_color_end_hex");
        StringAssert.Contains(catalogService, "font_color_hex");
        StringAssert.Contains(catalogService, "AccentColorHex: NormalizePlaylistAccentColorHex");
        StringAssert.Contains(catalogService, "AccentColorEndHex: NormalizePlaylistAccentColorHex");
        StringAssert.Contains(catalogService, "FontColorHex: NormalizePlaylistAccentColorHex");
        StringAssert.Contains(luister, "BuildPlaylistAccentStyle(playlist)");
        StringAssert.Contains(luisterCss, "--playlist-accent-color");
        StringAssert.Contains(playlistPage, "BuildPlaylistAccentStyle(CurrentPlaylist)");
        StringAssert.Contains(playlistCss, "--playlist-accent-color");
        StringAssert.Contains(showcasePage, "BuildShowcasePageStyle(CurrentPlaylist)");
        StringAssert.Contains(showcasePage, "--playlist-showcase-font-color");
        StringAssert.Contains(showcaseCss, "--playlist-showcase-background-start");
        StringAssert.Contains(showcaseCss, "--playlist-showcase-background-end");
        StringAssert.Contains(showcaseCss, "--playlist-showcase-font-color");
        StringAssert.Contains(migration, "accent_color_hex");
        StringAssert.Contains(gradientMigration, "accent_color_end_hex");
        StringAssert.Contains(fontMigration, "font_color_hex");
    }

    [TestMethod]
    public void AdminPlaylistLookupFallsBackWhenFontColorColumnIsMissing()
    {
        var adminService = File.ReadAllText(GetRepoPath("Shink", "Services", "SupabaseAdminManagementService.cs"));

        StringAssert.Contains(adminService, "includeFontColor = true");
        StringAssert.Contains(adminService, "body.Contains(\"font_color_hex\", StringComparison.OrdinalIgnoreCase)");
        StringAssert.Contains(adminService, "includeFontColor: false");
        StringAssert.Contains(adminService, "FetchPlaylistByIdAsync(baseUri, apiKey, id, cancellationToken, includeGradientEndColor, includeFontColor: false)");
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
