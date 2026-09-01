namespace Shink.Mobile.Games;

public static class CharacterMysteryImageResolver
{
    public static string? Resolve(string? imageUrl, string? mysteryImageUrl)
    {
        if (!string.IsNullOrWhiteSpace(mysteryImageUrl))
        {
            return mysteryImageUrl.Trim();
        }

        if (string.IsNullOrWhiteSpace(imageUrl))
        {
            return null;
        }

        var normalizedImageUrl = imageUrl.Trim();
        var queryIndex = normalizedImageUrl.IndexOf('?');
        var fragmentIndex = normalizedImageUrl.IndexOf('#');
        var suffixIndex = queryIndex < 0
            ? fragmentIndex
            : fragmentIndex < 0
                ? queryIndex
                : Math.Min(queryIndex, fragmentIndex);
        var path = suffixIndex < 0 ? normalizedImageUrl : normalizedImageUrl[..suffixIndex];
        var suffix = suffixIndex < 0 ? string.Empty : normalizedImageUrl[suffixIndex..];
        var extensionIndex = path.LastIndexOf('.');
        var slashIndex = Math.Max(
            path.LastIndexOf('/'),
            path.LastIndexOf('\\'));
        if (extensionIndex <= slashIndex)
        {
            return null;
        }

        var stem = path[..extensionIndex];
        if (stem.EndsWith("-mystery", StringComparison.OrdinalIgnoreCase))
        {
            return normalizedImageUrl;
        }

        return $"{stem}-mystery{path[extensionIndex..]}{suffix}";
    }
}

public enum CharacterGuessOutcome
{
    Ignored,
    Correct,
    Incorrect
}

public sealed record CharacterGuessRound(
    int RoundNumber,
    string TargetKey,
    IReadOnlyList<string> ChoiceKeys);

public sealed record CharacterGuessResult(
    CharacterGuessOutcome Outcome,
    string CorrectKey,
    int Score,
    int Streak,
    bool IsComplete);

public sealed class CharacterGuessGame
{
    private readonly IReadOnlyList<string> _characterKeys;
    private readonly Random _random;
    private bool _currentRoundAnswered;
    private string? _previousTargetKey;
    private CharacterGuessRound? _preparedRound;

    public CharacterGuessGame(
        IEnumerable<string> characterKeys,
        int totalRounds = 10,
        int desiredChoiceCount = 4,
        Random? random = null)
    {
        ArgumentNullException.ThrowIfNull(characterKeys);

        _characterKeys = characterKeys
            .Where(static key => !string.IsNullOrWhiteSpace(key))
            .Select(static key => key.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (_characterKeys.Count < 2)
        {
            throw new ArgumentException(
                "A character guessing game needs at least two unique characters.",
                nameof(characterKeys));
        }

        if (totalRounds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalRounds));
        }

        if (desiredChoiceCount < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(desiredChoiceCount));
        }

        TotalRounds = totalRounds;
        ChoiceCount = Math.Min(desiredChoiceCount, _characterKeys.Count);
        _random = random ?? Random.Shared;
    }

    public int TotalRounds { get; }

    public int ChoiceCount { get; }

    public int Score { get; private set; }

    public int Streak { get; private set; }

    public int RoundNumber { get; private set; }

    public bool IsComplete { get; private set; }

    public bool HasPerfectScore => IsComplete && Score == TotalRounds;

    public CharacterGuessRound? CurrentRound { get; private set; }

    public CharacterGuessRound StartNextRound()
    {
        if (IsComplete)
        {
            throw new InvalidOperationException("The game is already complete.");
        }

        if (CurrentRound is not null && !_currentRoundAnswered)
        {
            throw new InvalidOperationException("Answer the current round before starting another one.");
        }

        var round = _preparedRound ?? BuildRound(RoundNumber + 1, _previousTargetKey);
        _preparedRound = null;
        RoundNumber = round.RoundNumber;
        _previousTargetKey = round.TargetKey;
        _currentRoundAnswered = false;
        CurrentRound = round;
        return CurrentRound;
    }

    public CharacterGuessRound? PrepareNextRound()
    {
        if (IsComplete || RoundNumber >= TotalRounds)
        {
            return null;
        }

        if (CurrentRound is null)
        {
            throw new InvalidOperationException("Start the current round before preparing the next one.");
        }

        return _preparedRound ??= BuildRound(RoundNumber + 1, CurrentRound.TargetKey);
    }

    public CharacterGuessResult Guess(string characterKey)
    {
        if (CurrentRound is null)
        {
            throw new InvalidOperationException("Start a round before guessing.");
        }

        if (_currentRoundAnswered)
        {
            return new CharacterGuessResult(
                CharacterGuessOutcome.Ignored,
                CurrentRound.TargetKey,
                Score,
                Streak,
                IsComplete);
        }

        if (!CurrentRound.ChoiceKeys.Contains(characterKey, StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentOutOfRangeException(nameof(characterKey), "The character is not a choice in this round.");
        }

        var isCorrect = string.Equals(
            CurrentRound.TargetKey,
            characterKey,
            StringComparison.OrdinalIgnoreCase);
        _currentRoundAnswered = true;
        if (isCorrect)
        {
            Score++;
            Streak++;
        }
        else
        {
            Streak = 0;
        }

        IsComplete = RoundNumber >= TotalRounds;
        return new CharacterGuessResult(
            isCorrect ? CharacterGuessOutcome.Correct : CharacterGuessOutcome.Incorrect,
            CurrentRound.TargetKey,
            Score,
            Streak,
            IsComplete);
    }

    private CharacterGuessRound BuildRound(int roundNumber, string? previousTargetKey)
    {
        var targetCandidates = _characterKeys
            .Where(key => !string.Equals(key, previousTargetKey, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var targetKey = targetCandidates[_random.Next(targetCandidates.Length)];

        var alternatives = _characterKeys
            .Where(key => !string.Equals(key, targetKey, StringComparison.OrdinalIgnoreCase))
            .ToList();
        ShuffleInPlace(alternatives);

        var choices = alternatives
            .Take(ChoiceCount - 1)
            .Append(targetKey)
            .ToList();
        ShuffleInPlace(choices);
        return new CharacterGuessRound(roundNumber, targetKey, choices);
    }

    private void ShuffleInPlace<T>(IList<T> items)
    {
        for (var index = items.Count - 1; index > 0; index--)
        {
            var swapIndex = _random.Next(index + 1);
            (items[index], items[swapIndex]) = (items[swapIndex], items[index]);
        }
    }
}
