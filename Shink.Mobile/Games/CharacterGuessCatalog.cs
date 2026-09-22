using Shink.Mobile.Models;

namespace Shink.Mobile.Games;

public static class CharacterGuessCatalog
{
    public static IReadOnlyList<MobileCharacterCard> SelectEligibleCharacters(Models.MobileCharactersResponse? response) =>
        response?.Characters
            .Where(static character => !string.IsNullOrWhiteSpace(character.Slug))
            .Select(static character => character with
            {
                Slug = character.Slug.Trim(),
                // Locked profile images show the silhouette. The game needs the full
                // artwork for answer choices and the reveal, regardless of unlock state.
                ImageUrl = !string.IsNullOrWhiteSpace(character.MatchImageUrl)
                    ? character.MatchImageUrl
                    : character.IsUnlocked ? character.ImageUrl : string.Empty
            })
            .Where(static character => !string.IsNullOrWhiteSpace(character.DisplayName))
            .Where(static character => !string.IsNullOrWhiteSpace(character.ImageUrl))
            .Where(static character => !string.IsNullOrWhiteSpace(
                CharacterMysteryImageResolver.Resolve(character.ImageUrl, character.MysteryImageUrl)))
            .DistinctBy(static character => character.Slug, StringComparer.OrdinalIgnoreCase)
            .ToArray()
        ?? Array.Empty<MobileCharacterCard>();
}
