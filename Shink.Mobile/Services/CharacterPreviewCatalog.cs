using Shink.Mobile.Models;

namespace Shink.Mobile.Services;

internal static class CharacterPreviewCatalog
{
    // Gallery models can be cached for days; their signed audio URLs last two hours.
    // Always select from a freshly authorized response, never the displayed model.
    public static MobileCharacterAudioClip SelectClip(Models.MobileCharactersResponse? freshCatalog, string slug)
    {
        var character = freshCatalog?.Characters.FirstOrDefault(candidate =>
            string.Equals(candidate.Slug, slug, StringComparison.OrdinalIgnoreCase));
        var clips = character is { IsUnlocked: true }
            ? character.PreviewAudioClips.Where(clip => !string.IsNullOrWhiteSpace(clip.AudioUrl)).ToArray()
            : [];
        if (clips.Length == 0)
        {
            throw new InvalidOperationException("Geen karakterklank is tans beskikbaar nie. Probeer asseblief weer.");
        }

        return clips[Random.Shared.Next(clips.Length)];
    }
}
