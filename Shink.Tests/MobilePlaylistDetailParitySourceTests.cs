using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Shink.Tests;

[TestClass]
public sealed class MobilePlaylistDetailParitySourceTests
{
    [TestMethod]
    public void PlaylistTapOpensStoriesShowcaseBeforeThePlayerRoute()
    {
        var luister = File.ReadAllText(FindRepoFile("Shink.Mobile", "Pages", "LuisterPage.cs"));
        var shell = File.ReadAllText(FindRepoFile("Shink.Mobile", "AppShell.xaml.cs"));
        var program = File.ReadAllText(FindRepoFile("Shink.Mobile", "MauiProgram.cs"));

        StringAssert.Contains(luister, "nameof(PlaylistStoriesPage)");
        StringAssert.Contains(luister, "[\"playlist\"] = playlist");
        StringAssert.Contains(shell, "Routing.RegisterRoute(nameof(PlaylistStoriesPage), typeof(PlaylistStoriesPage));");
        StringAssert.Contains(program, "builder.Services.AddTransient<PlaylistStoriesPage>();");
        StringAssert.Contains(shell, "Routing.RegisterRoute(nameof(PlaylistDetailPage), typeof(PlaylistDetailPage));");
        StringAssert.Contains(program, "builder.Services.AddTransient<PlaylistDetailPage>();");
    }

    [TestMethod]
    public void PlaylistStoriesPageMatchesTheWebShowcaseStructure()
    {
        var source = File.ReadAllText(FindRepoFile("Shink.Mobile", "Pages", "PlaylistStoriesPage.cs"));

        StringAssert.Contains(source, "Speel Speellys");
        StringAssert.Contains(source, "Kies 'n Storie");
        StringAssert.Contains(source, "Stories in hierdie speellys");
        StringAssert.Contains(source, "BuildFeaturedStory(showcaseStory)");
        StringAssert.Contains(source, "new GridItemsLayout(2, ItemsLayoutOrientation.Vertical)");
        StringAssert.Contains(source, "cover.HeightRequest = card.Width * 4d / 3d;");
        StringAssert.Contains(source, "nameof(PlaylistDetailPage)");
        StringAssert.Contains(source, "nameof(StoryDetailPage)");
        StringAssert.Contains(source, "appearance.BackdropUrl");
        StringAssert.Contains(source, "appearance.LogoUrl");
        StringAssert.Contains(source, "new LinearGradientBrush(");
        StringAssert.Contains(source, "#F5F9DC");
        StringAssert.Contains(source, "storie-hoekie-cover-20260422132833");
        StringAssert.Contains(source, "storie-hoekie-thumbnail-20260422132701");
    }

    [TestMethod]
    public void PlaylistDetailMatchesTheMobileWebPlayerTreatment()
    {
        var source = File.ReadAllText(FindRepoFile("Shink.Mobile", "Pages", "PlaylistDetailPage.cs"));

        StringAssert.Contains(source, "Color.FromArgb(\"#222222\")");
        StringAssert.Contains(source, "Color.FromArgb(\"#FF135B\")");
        StringAssert.Contains(source, "HeightRequest = 340");
        StringAssert.Contains(source, "cover.HeightRequest = Math.Min(Math.Max(cover.Width, 260), 540)");
        StringAssert.Contains(source, "PreviousIconGlyph = \"\\uf048\"");
        StringAssert.Contains(source, "NextIconGlyph = \"\\uf051\"");
        StringAssert.Contains(source, "Waaroor gaan hierdie storie?");
        StringAssert.Contains(source, "Volledige speellys");
    }

    [TestMethod]
    public void PlaylistPagesKeepBackControlsVisibleInsideTheTopSafeArea()
    {
        var showcase = File.ReadAllText(FindRepoFile("Shink.Mobile", "Pages", "PlaylistStoriesPage.cs"));
        var player = File.ReadAllText(FindRepoFile("Shink.Mobile", "Pages", "PlaylistDetailPage.cs"));

        StringAssert.Contains(showcase, "BuildFixedBackButtonOverlay(\"Gaan terug na Luister\")");
        StringAssert.Contains(showcase, "SafeAreaRegions.Container");
        StringAssert.Contains(showcase, "ZIndex = 100");
        StringAssert.Contains(player, "BuildFixedBackButtonOverlay()");
        StringAssert.Contains(player, "SafeAreaRegions.Container");
        StringAssert.Contains(player, "ZIndex = 100");
    }

    [TestMethod]
    public void MobilePlaylistApiCarriesTheWebShowcaseAppearance()
    {
        var model = File.ReadAllText(FindRepoFile("Shink.Mobile", "Models", "MobileApiModels.cs"));
        var api = File.ReadAllText(FindRepoFile("Shink", "Program.cs"));

        StringAssert.Contains(model, "string? LogoUrl = null");
        StringAssert.Contains(model, "string? BackgroundStartColorHex = null");
        StringAssert.Contains(model, "string? BackgroundEndColorHex = null");
        StringAssert.Contains(model, "string? FontColorHex = null");
        StringAssert.Contains(api, "BackdropUrl: BuildMobilePlaylistBackdropUri(httpContext, playlist)");
        StringAssert.Contains(api, "LogoUrl: BuildMobilePlaylistLogoUri(httpContext, playlist)");
        StringAssert.Contains(api, "BackgroundStartColorHex: playlist.AccentColorHex");
        StringAssert.Contains(api, "BackgroundEndColorHex: playlist.AccentColorEndHex ?? playlist.AccentColorHex");
        StringAssert.Contains(api, "FontColorHex: playlist.FontColorHex");
    }

    [TestMethod]
    public void PlaylistDetailUsesVirtualizedTrackRowsAndInlinePlayback()
    {
        var source = File.ReadAllText(FindRepoFile("Shink.Mobile", "Pages", "PlaylistDetailPage.cs"));

        StringAssert.Contains(source, "private readonly ObservableCollection<PlaylistTrackItem> _tracks = [];");
        StringAssert.Contains(source, "new CollectionView");
        StringAssert.Contains(source, "new LinearItemsLayout(ItemsLayoutOrientation.Vertical)");
        StringAssert.Contains(source, "ItemSizingStrategy = ItemSizingStrategy.MeasureFirstItem");
        StringAssert.Contains(source, "await _storyPlaybackSession.PlayAsync(");
        StringAssert.Contains(source, "_storyPlaybackSession.NotifyPageHidden();");
        StringAssert.Contains(source, "await SelectStoryAsync(item.Story, autoplay: true);");
        StringAssert.Contains(source, "_playlistPlaybackState.CanAutoplayAdvance(_currentStory)");
        StringAssert.Contains(source, "Intekening nodig");
    }

    private static string FindRepoFile(params string[] pathParts)
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

        throw new FileNotFoundException($"Could not locate {string.Join('/', pathParts)}.");
    }
}
