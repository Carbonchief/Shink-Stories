namespace Shink.Mobile.Games;

public enum CharacterMatchOutcome
{
    Ignored,
    FirstCard,
    Match,
    Mismatch
}

public sealed record CharacterMatchCard(Guid Id, string PairKey);

public sealed record CharacterMatchTurn(
    CharacterMatchOutcome Outcome,
    Guid? FirstCardId,
    Guid? SecondCardId,
    bool IsComplete);

public sealed class CharacterMatchGame
{
    private readonly IReadOnlyDictionary<Guid, string> _pairKeys;
    private readonly HashSet<Guid> _faceUpCardIds = [];
    private readonly HashSet<Guid> _matchedCardIds = [];
    private Guid? _firstCardId;
    private Guid? _secondCardId;
    private bool _pendingTurnIsMatch;

    public CharacterMatchGame(IEnumerable<CharacterMatchCard> cards)
    {
        var cardList = cards.ToArray();
        if (cardList.Length == 0 || cardList.Length % 2 != 0)
        {
            throw new ArgumentException("A match game needs a non-empty, even number of cards.", nameof(cards));
        }

        if (cardList.Any(card => string.IsNullOrWhiteSpace(card.PairKey)) ||
            cardList.Select(card => card.Id).Distinct().Count() != cardList.Length ||
            cardList.GroupBy(card => card.PairKey, StringComparer.Ordinal).Any(group => group.Count() != 2))
        {
            throw new ArgumentException("Every card needs a unique id and exactly one matching pair.", nameof(cards));
        }

        _pairKeys = cardList.ToDictionary(card => card.Id, card => card.PairKey);
        PairCount = cardList.Length / 2;
    }

    public int PairCount { get; }

    public int AttemptCount { get; private set; }

    public int MatchedPairCount { get; private set; }

    public bool IsPerfectScore =>
        MatchedPairCount == PairCount &&
        AttemptCount == PairCount;

    public bool IsResolving { get; private set; }

    public bool IsFaceUp(Guid cardId) => _faceUpCardIds.Contains(cardId);

    public bool IsMatched(Guid cardId) => _matchedCardIds.Contains(cardId);

    public CharacterMatchTurn Reveal(Guid cardId)
    {
        if (!_pairKeys.ContainsKey(cardId))
        {
            throw new ArgumentOutOfRangeException(nameof(cardId), "The card is not part of this game.");
        }

        if (IsResolving || _faceUpCardIds.Contains(cardId) || _matchedCardIds.Contains(cardId))
        {
            return new CharacterMatchTurn(CharacterMatchOutcome.Ignored, _firstCardId, null, IsComplete: false);
        }

        _faceUpCardIds.Add(cardId);
        if (_firstCardId is null)
        {
            _firstCardId = cardId;
            return new CharacterMatchTurn(CharacterMatchOutcome.FirstCard, cardId, null, IsComplete: false);
        }

        _secondCardId = cardId;
        AttemptCount++;
        IsResolving = true;
        _pendingTurnIsMatch = string.Equals(
            _pairKeys[_firstCardId.Value],
            _pairKeys[_secondCardId.Value],
            StringComparison.Ordinal);

        if (_pendingTurnIsMatch)
        {
            _matchedCardIds.Add(_firstCardId.Value);
            _matchedCardIds.Add(_secondCardId.Value);
            MatchedPairCount++;
        }

        return new CharacterMatchTurn(
            _pendingTurnIsMatch ? CharacterMatchOutcome.Match : CharacterMatchOutcome.Mismatch,
            _firstCardId,
            _secondCardId,
            IsComplete: MatchedPairCount == PairCount);
    }

    public void CompleteTurn()
    {
        if (!IsResolving)
        {
            return;
        }

        if (_firstCardId is { } firstCardId)
        {
            _faceUpCardIds.Remove(firstCardId);
        }

        if (_secondCardId is { } secondCardId)
        {
            _faceUpCardIds.Remove(secondCardId);
        }

        _firstCardId = null;
        _secondCardId = null;
        _pendingTurnIsMatch = false;
        IsResolving = false;
    }
}
