using System.Collections.Specialized;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shink.Mobile.Models;
using Shink.Mobile.Services;

namespace Shink.Tests;

[TestClass]
public sealed class MobileCharacterGalleryCollectionTests
{
    [TestMethod]
    public void EquivalentNetworkPayloadKeepsExistingRowsAndEmitsNoReset()
    {
        var original = Card("roomie");
        var gallery = new MobileCharacterGalleryCollection();
        gallery.Synchronize([original]);
        var actions = Observe(gallery);
        gallery.Synchronize([original with
        {
            PreviewAudioClips = original.PreviewAudioClips.ToArray(),
            RelatedStories = original.RelatedStories.ToArray()
        }]);

        Assert.AreEqual(0, actions.Count);
        Assert.AreSame(original, gallery[0]);
    }

    [TestMethod]
    public void UnlockChangeUpdatesOneRowWithoutRecreatingOtherRows()
    {
        var locked = Card("roomie") with { IsUnlocked = false };
        var other = Card("piesang");
        var gallery = new MobileCharacterGalleryCollection();
        gallery.Synchronize([locked, other]);
        var actions = Observe(gallery);
        var unlocked = locked with { IsUnlocked = true, Heading = "Roomie" };
        gallery.Synchronize([unlocked, other]);

        CollectionAssert.AreEqual(new[] { NotifyCollectionChangedAction.Replace }, actions);
        Assert.AreSame(unlocked, gallery[0]);
        Assert.AreSame(other, gallery[1]);
    }

    [TestMethod]
    public void RenewedAudioAuthorizationAndStoryMetadataReachExistingPosition()
    {
        var original = Card("roomie");
        var gallery = new MobileCharacterGalleryCollection();
        gallery.Synchronize([original]);
        var actions = Observe(gallery);
        var refreshed = original with
        {
            PreviewAudioClips = [original.PreviewAudioClips[0] with { AudioUrl = "/preview?token=fresh" }],
            RelatedStories = [original.RelatedStories[0] with { Title = "Nuwe titel" }]
        };
        gallery.Synchronize([refreshed]);

        CollectionAssert.AreEqual(new[] { NotifyCollectionChangedAction.Replace }, actions);
        Assert.AreEqual("/preview?token=fresh", gallery[0].PreviewAudioClips[0].AudioUrl);
        Assert.AreEqual("Nuwe titel", gallery[0].RelatedStories[0].Title);
    }

    [TestMethod]
    public void ReorderedOrRemovedCharactersUseOneResetAndKeepCorrectOrder()
    {
        var first = Card("roomie");
        var second = Card("piesang");
        var gallery = new MobileCharacterGalleryCollection();
        gallery.Synchronize([first, second]);
        var actions = Observe(gallery);
        gallery.Synchronize([second, first]);
        CollectionAssert.AreEqual(new[] { NotifyCollectionChangedAction.Reset }, actions);
        Assert.AreSame(second, gallery[0]);
        actions.Clear();
        gallery.Synchronize([first]);
        CollectionAssert.AreEqual(new[] { NotifyCollectionChangedAction.Reset }, actions);
        Assert.AreSame(first, gallery[0]);
        Assert.AreEqual(1, gallery.Count);
    }

    private static List<NotifyCollectionChangedAction> Observe(MobileCharacterGalleryCollection gallery)
    {
        var actions = new List<NotifyCollectionChangedAction>();
        gallery.CollectionChanged += (_, args) => actions.Add(args.Action);
        return actions;
    }

    private static MobileCharacterCard Card(string slug) => new(
        CharacterId: Guid.NewGuid(), Slug: slug, DisplayName: slug, Heading: slug,
        SummaryText: "Karakter", ImageUrl: $"/characters/{slug}.png",
        MatchImageUrl: null, MysteryImageUrl: null, ImageAlt: slug,
        IsUnlocked: true, DisplayOrder: 0, Tagline: null, Species: null,
        Habitat: null, Catchphrase: null, FavoriteThing: null, CharacterTrait: null,
        GoldenLesson: null, CoreValue: null, FirstAppearance: null, Friends: null,
        ReflectionQuestion: null, ChallengeText: null,
        PreviewAudioClips: [new("Stem", slug, "/preview?token=old")],
        PrimaryStory: null, RelatedStories: [new(slug, "Storie", "luister", "/story.png")],
        CallToActionLabel: "Luister");
}
