using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shink.Mobile.Models;
using Shink.Mobile.Services;

namespace Shink.Tests;

[TestClass]
public class CharacterPreviewPlaybackTests
{
    [TestMethod]
    public void SelectsFreshSignedClipForRequestedCharacterOnly()
    {
        var refreshed = Catalog(Character("panda", true, "/media/audio/panda?token=fresh"),
            Character("krokodil", true, "/media/audio/krokodil?token=other"));
        Assert.AreEqual("/media/audio/panda?token=fresh", CharacterPreviewCatalog.SelectClip(refreshed, "PANDA").AudioUrl);
    }

    [TestMethod]
    public void RevokedAccessCannotPlayEvenIfTheGalleryStillShowsUnlocked()
    {
        var refreshed = Catalog(Character("panda", false, "/media/audio/panda?token=stale"));
        Assert.Throws<InvalidOperationException>(() => CharacterPreviewCatalog.SelectClip(refreshed, "panda"));
    }

    [TestMethod]
    public void MissingOrUnavailableCatalogDoesNotFallBackToExpiredLinks()
    {
        Assert.Throws<InvalidOperationException>(() => CharacterPreviewCatalog.SelectClip(null, "panda"));
        Assert.Throws<InvalidOperationException>(() => CharacterPreviewCatalog.SelectClip(Catalog(), "panda"));
        Assert.Throws<InvalidOperationException>(() => CharacterPreviewCatalog.SelectClip(Catalog(Character("panda", true)), "panda"));
    }

    [TestMethod]
    public void RandomSelectionExcludesBlankAudioAndStaysWithinAuthorizedClips()
    {
        var refreshed = Catalog(Character("panda", true, "", " ", "/media/audio/one?token=fresh", "/media/audio/two?token=fresh"));
        for (var i = 0; i < 100; i++)
        {
            var clip = CharacterPreviewCatalog.SelectClip(refreshed, "panda");
            Assert.IsTrue(clip.AudioUrl is "/media/audio/one?token=fresh" or "/media/audio/two?token=fresh");
        }
    }

    [TestMethod]
    public void PageCancelsStaleRequestsAndCleansUpOnlyItsOwnPreview()
    {
        var page = Source("Pages/KaraktersPage.cs");
        var prepare = page.IndexOf("var freshCatalog = await _apiClient.GetCharactersAsync(token);", StringComparison.Ordinal);
        var play = page.IndexOf("await _audioPlaybackService.PlayAsync(", prepare, StringComparison.Ordinal);
        StringAssert.Contains(page[prepare..play], "token.ThrowIfCancellationRequested();");
        StringAssert.Contains(page[prepare..play], "if (!_isPageActive) return;");
        StringAssert.Contains(page, "ownedPreview && !_storyPlaybackSession.HasActiveStory");
        StringAssert.Contains(page, "_previewCancellation?.Cancel();");
        StringAssert.Contains(page, "_owner.PreviewPlaybackChanged -= OnPreviewChanged;");
        StringAssert.Contains(page, "_friendAnimationTimer.Tick -= OnFriendAnimationTick;");
        StringAssert.Contains(page, "CharacterAnimations.Celebrate(imageButton);");
        StringAssert.Contains(page, "CharacterAnimations.Idle(_profileImage);");
        StringAssert.Contains(page, "TimeSpan.FromSeconds(3)");
    }

    [TestMethod]
    public void NativePlayersCannotStartAgainAfterTheyWereStoppedOrReplaced()
    {
        var audio = Source("Services/AudioPlaybackService.cs");
        StringAssert.Contains(audio, "if (playerItem is null || !ReferenceEquals(_player?.CurrentItem, playerItem)) return;");
        StringAssert.Contains(audio, "if (!ReferenceEquals(_player, player)) return;\n            player.Start();");
    }

    private static Shink.Mobile.Models.MobileCharactersResponse Catalog(params MobileCharacterCard[] characters) =>
        new(true, characters.Count(c => c.IsUnlocked), characters.Length, characters);

    private static MobileCharacterCard Character(string slug, bool unlocked, params string[] urls) =>
        JsonSerializer.Deserialize<MobileCharacterCard>(JsonSerializer.Serialize(new
        {
            Slug = slug, DisplayName = slug, IsUnlocked = unlocked,
            PreviewAudioClips = urls.Select((url, i) => new MobileCharacterAudioClip(slug, $"{slug}-{i}", url)).ToArray()
        }))!;

    private static string Source(string relative)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var path = Path.Combine(directory.FullName, "Shink.Mobile", relative);
            if (File.Exists(path)) return File.ReadAllText(path);
            directory = directory.Parent;
        }
        throw new FileNotFoundException(relative);
    }
}
