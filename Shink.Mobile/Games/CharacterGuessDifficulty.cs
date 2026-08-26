namespace Shink.Mobile.Games;

public enum CharacterGuessDifficulty
{
    Beginner,
    Kenner,
    Meester
}

public sealed record CharacterGuessDifficultyOption(
    CharacterGuessDifficulty Difficulty,
    string DisplayName,
    int TotalRounds,
    string ImageSource);

public static class CharacterGuessDifficultyCatalog
{
    public static IReadOnlyList<CharacterGuessDifficultyOption> Options { get; } =
    [
        new(CharacterGuessDifficulty.Beginner, "BEGINNER", 10, "karakter_raai_beginner.png"),
        new(CharacterGuessDifficulty.Kenner, "KENNER", 20, "karakter_raai_kenner.png"),
        new(CharacterGuessDifficulty.Meester, "MEESTER", 30, "karakter_raai_meester.png")
    ];

    public static CharacterGuessDifficultyOption FromRoundCount(int roundCount) =>
        Options.FirstOrDefault(option => option.TotalRounds == roundCount)
        ?? Options[0];
}
