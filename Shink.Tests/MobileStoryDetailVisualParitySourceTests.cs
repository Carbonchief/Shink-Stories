using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Shink.Tests;

[TestClass]
public sealed class MobileStoryDetailVisualParitySourceTests
{
    [TestMethod]
    public void StoryDetailUsesTheWebStoryPageBackground()
    {
        var source = File.ReadAllText(FindStoryDetailPage());

        StringAssert.Contains(source, "Background = BuildStoryPageBackground();");
        StringAssert.Contains(source, "new GradientStop(Color.FromArgb(\"#F6F3EE\"), 0)");
        StringAssert.Contains(source, "new GradientStop(Color.FromArgb(\"#ECE8E2\"), 1)");
        StringAssert.Contains(source, "Background = BuildStoryPageHighlight()");
        StringAssert.Contains(source, "new GradientStop(Color.FromArgb(\"#2E8DC66F\"), 0)");
        StringAssert.Contains(source, "private static readonly Color PlayerTextColor = Color.FromArgb(\"#1C1C1C\");");
        StringAssert.Contains(source, "BackgroundColor = Colors.Transparent,");
    }

    [TestMethod]
    public void StoryDetailPlaybackModesUseIconsWithAfrikaansAccessibilityLabels()
    {
        var source = File.ReadAllText(FindStoryDetailPage());

        StringAssert.Contains(source, "BuildPlaybackModeIconButton(");
        StringAssert.Contains(source, "AutoplayIconGlyph");
        StringAssert.Contains(source, "InfinityIconGlyph");
        StringAssert.Contains(source, "HourglassIconGlyph");
        StringAssert.Contains(source, "ShuffleIconGlyph");
        StringAssert.Contains(source, "FontFamily = \"FontAwesomeSolid\"");
        StringAssert.Contains(source, "SemanticProperties.SetDescription(button, accessibilityLabel);");
        Assert.DoesNotContain("BuildPlaybackModeButton(\n            \"Auto\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildPlaybackModeButton(\n            \"Skommel\"", source, StringComparison.Ordinal);
    }

    [TestMethod]
    public void StoryDetailTransportUsesWebStyleStepIcons()
    {
        var source = File.ReadAllText(FindStoryDetailPage());

        StringAssert.Contains(source, "BuildTransportButton(PlaybackTransportDirection.Previous)");
        StringAssert.Contains(source, "BuildTransportButton(PlaybackTransportDirection.Next)");
        StringAssert.Contains(source, "PreviousStoryIconGlyph");
        StringAssert.Contains(source, "NextStoryIconGlyph");
        StringAssert.Contains(source, "direction == PlaybackTransportDirection.Previous ? \"Vorige storie\" : \"Volgende storie\"");
        Assert.DoesNotContain("BuildTransportButton(\"|‹\")", source, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildTransportButton(\"›|\")", source, StringComparison.Ordinal);
    }

    [TestMethod]
    public void StoryDetailCoverImageTogglesAvailableAudioPlayback()
    {
        var source = File.ReadAllText(FindStoryDetailPage());

        StringAssert.Contains(source, "var coverImage = new ProgressiveCachedImage(");
        StringAssert.Contains(source, "if (!detail.RequiresSubscription && !string.IsNullOrWhiteSpace(detail.AudioUrl))");
        StringAssert.Contains(source, "coverImageTap.Tapped += (_, _) => _ = ToggleFullscreenPlaybackAsync(detail);");
        StringAssert.Contains(source, "coverImage.GestureRecognizers.Add(coverImageTap);");
    }

    [TestMethod]
    public void PlaylistQueueAppearsAfterInfoAsAStoryCarousel()
    {
        var source = File.ReadAllText(FindStoryDetailPage());
        var renderDetailStart = source.IndexOf("private void RenderDetail", StringComparison.Ordinal);
        var infoIndex = source.IndexOf("_content.Children.Add(BuildStoryInfoCard(detail));", renderDetailStart, StringComparison.Ordinal);
        var queueIndex = source.IndexOf("var playlistQueue = BuildPlaylistQueue(detail);", renderDetailStart, StringComparison.Ordinal);

        Assert.IsGreaterThanOrEqualTo(0, renderDetailStart);
        Assert.IsGreaterThan(infoIndex, queueIndex);
        StringAssert.Contains(source, "new CollectionView");
        StringAssert.Contains(source, "new ObservableCollection<MobileStorySummary>(queuedStories)");
        StringAssert.Contains(source, "new LinearItemsLayout(ItemsLayoutOrientation.Horizontal)");
        StringAssert.Contains(source, "ItemSizingStrategy = ItemSizingStrategy.MeasureFirstItem");
        StringAssert.Contains(source, "private const double StoryCarouselImageAspectRatio = 3d / 4d;");
        StringAssert.Contains(source, "BuildPlaylistQueueCarouselCard");
    }

    private static string FindStoryDetailPage()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "Shink.Mobile", "Pages", "StoryDetailPage.cs");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate Shink.Mobile/Pages/StoryDetailPage.cs.");
    }
}
