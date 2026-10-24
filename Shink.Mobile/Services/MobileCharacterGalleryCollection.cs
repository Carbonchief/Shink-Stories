using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Shink.Mobile.Models;

namespace Shink.Mobile.Services;

/// <summary>Keeps native gallery cells alive across cached/network refreshes.</summary>
public sealed class MobileCharacterGalleryCollection : ObservableCollection<MobileCharacterCard>
{
    public void Synchronize(IReadOnlyList<MobileCharacterCard> next)
    {
        var sameOrder = Count == next.Count;
        for (var index = 0; sameOrder && index < Count; index++)
            sameOrder = this[index].CharacterId == next[index].CharacterId;

        if (sameOrder)
        {
            for (var index = 0; index < Count; index++)
                if (!HasSameContent(this[index], next[index]))
                    this[index] = next[index];
            return;
        }

        // Real additions/removals/reordering need one reset, not a reset for
        // every item. Ordinary refreshes take the per-item path above.
        Items.Clear();
        foreach (var character in next) Items.Add(character);
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    private static bool HasSameContent(MobileCharacterCard previous, MobileCharacterCard next)
    {
        if (ReferenceEquals(previous, next)) return true;
        // Records compare list references. Fresh JSON creates new lists even
        // when their contents match, so compare those two lists by value too.
        return previous with
        {
            PreviewAudioClips = next.PreviewAudioClips,
            RelatedStories = next.RelatedStories
        } == next &&
            previous.PreviewAudioClips.SequenceEqual(next.PreviewAudioClips) &&
            previous.RelatedStories.SequenceEqual(next.RelatedStories);
    }
}
