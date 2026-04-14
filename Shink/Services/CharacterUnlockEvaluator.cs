namespace Shink.Services;

public static class CharacterUnlockEvaluator
{
    public const string MatchModeAny = "any";
    public const string MatchModeAll = "all";
    public const string RuleTypeStoryListenSeconds = "story_listen_seconds";
    public const string RuleTypeStoryRepeatCount = "story_repeat_count";
    public const string RuleTypeStoryCount = "story_count";
    public const string RuleTypeUnlockedCharacterCount = "unlocked_character_count";
    public const string RuleTypeProfileListenCount = "profile_listen_count";

    public static IReadOnlyDictionary<Guid, bool> EvaluateUnlockStates(
        IReadOnlyList<StoryCharacterItem> characters,
        IReadOnlyDictionary<string, UserStoryProgressItem> progressLookup,
        IReadOnlyDictionary<string, int> profileListenCountsByCharacterSlug)
    {
        var orderedCharacters = characters
            .OrderBy(character => character.SortOrder)
            .ThenBy(character => character.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var unlockStates = orderedCharacters.ToDictionary(character => character.CharacterId, _ => false);
        var changed = true;
        var safetyCounter = 0;

        while (changed && safetyCounter++ < orderedCharacters.Length + 2)
        {
            changed = false;
            foreach (var character in orderedCharacters)
            {
                var isUnlocked = IsCharacterUnlocked(character, progressLookup, profileListenCountsByCharacterSlug, unlockStates);
                if (unlockStates[character.CharacterId] == isUnlocked)
                {
                    continue;
                }

                unlockStates[character.CharacterId] = isUnlocked;
                changed = true;
            }
        }

        return unlockStates;
    }

    public static bool IsCharacterUnlocked(
        StoryCharacterItem character,
        IReadOnlyDictionary<string, UserStoryProgressItem> progressLookup,
        IReadOnlyDictionary<string, int> profileListenCountsByCharacterSlug,
        IReadOnlyDictionary<Guid, bool>? unlockStates = null)
    {
        var rules = GetEffectiveUnlockRules(character);
        if (rules.Count == 0)
        {
            return false;
        }

        return rules.Any(rule => EvaluateRule(character, rule, progressLookup, profileListenCountsByCharacterSlug, unlockStates));
    }

    public static IReadOnlyList<CharacterUnlockRuleItem> GetEffectiveUnlockRules(StoryCharacterItem character)
    {
        var normalizedRules = NormalizeRules(character.UnlockRules);
        if (normalizedRules.Count > 0)
        {
            return normalizedRules;
        }

        var legacyTargets = new List<string>();
        if (!string.IsNullOrWhiteSpace(character.UnlockStorySlug))
        {
            legacyTargets.Add(character.UnlockStorySlug);
        }

        legacyTargets.AddRange(character.RelatedStorySlugs);
        var normalizedTargets = NormalizeSlugs(legacyTargets);
        if (normalizedTargets.Count == 0)
        {
            return Array.Empty<CharacterUnlockRuleItem>();
        }

        return
        [
            new CharacterUnlockRuleItem(
                RuleType: RuleTypeStoryListenSeconds,
                TargetSlugs: normalizedTargets,
                TargetMatchMode: MatchModeAny,
                MinimumCount: 0,
                MinimumSeconds: Math.Max(1, character.UnlockThresholdSeconds))
        ];
    }

    public static IReadOnlyList<string> GetRelevantStorySlugs(StoryCharacterItem character)
    {
        var slugs = new List<string>();
        if (!string.IsNullOrWhiteSpace(character.UnlockStorySlug))
        {
            slugs.Add(character.UnlockStorySlug);
        }

        slugs.AddRange(character.RelatedStorySlugs);
        slugs.AddRange(
            GetEffectiveUnlockRules(character)
                .Where(rule => IsStoryRuleType(rule.RuleType))
                .SelectMany(rule => rule.TargetSlugs));

        return NormalizeSlugs(slugs);
    }

    public static bool HasStoryDrivenUnlockRule(StoryCharacterItem character) =>
        GetEffectiveUnlockRules(character).Any(rule => IsStoryRuleType(rule.RuleType));

    private static bool EvaluateRule(
        StoryCharacterItem character,
        CharacterUnlockRuleItem rule,
        IReadOnlyDictionary<string, UserStoryProgressItem> progressLookup,
        IReadOnlyDictionary<string, int> profileListenCountsByCharacterSlug,
        IReadOnlyDictionary<Guid, bool>? unlockStates)
    {
        var normalizedRuleType = NormalizeRuleType(rule.RuleType);
        if (string.IsNullOrWhiteSpace(normalizedRuleType))
        {
            return false;
        }

        var normalizedTargets = NormalizeSlugs(rule.TargetSlugs);
        var matchAll = string.Equals(rule.TargetMatchMode, MatchModeAll, StringComparison.OrdinalIgnoreCase);

        return normalizedRuleType switch
        {
            RuleTypeStoryListenSeconds => EvaluateStoryListenSecondsRule(normalizedTargets, matchAll, rule.MinimumSeconds, progressLookup),
            RuleTypeStoryRepeatCount => EvaluateStoryRepeatCountRule(normalizedTargets, matchAll, rule.MinimumCount, progressLookup),
            RuleTypeStoryCount => CountListenedStories(progressLookup) >= Math.Max(1, rule.MinimumCount),
            RuleTypeUnlockedCharacterCount => CountUnlockedCharacters(character.CharacterId, unlockStates) >= Math.Max(1, rule.MinimumCount),
            RuleTypeProfileListenCount => EvaluateProfileListenCountRule(character, normalizedTargets, matchAll, rule.MinimumCount, profileListenCountsByCharacterSlug),
            _ => false
        };
    }

    private static bool EvaluateStoryListenSecondsRule(
        IReadOnlyList<string> storySlugs,
        bool matchAll,
        int minimumSeconds,
        IReadOnlyDictionary<string, UserStoryProgressItem> progressLookup)
    {
        var requiredSeconds = Math.Max(1, minimumSeconds);
        if (storySlugs.Count == 0)
        {
            return false;
        }

        return matchAll
            ? storySlugs.All(storySlug => HasStoryListenSeconds(storySlug, requiredSeconds, progressLookup))
            : storySlugs.Any(storySlug => HasStoryListenSeconds(storySlug, requiredSeconds, progressLookup));
    }

    private static bool EvaluateStoryRepeatCountRule(
        IReadOnlyList<string> storySlugs,
        bool matchAll,
        int minimumCount,
        IReadOnlyDictionary<string, UserStoryProgressItem> progressLookup)
    {
        var requiredCount = Math.Max(1, minimumCount);
        if (storySlugs.Count == 0)
        {
            return false;
        }

        return matchAll
            ? storySlugs.All(storySlug => HasStoryRepeatCount(storySlug, requiredCount, progressLookup))
            : storySlugs.Any(storySlug => HasStoryRepeatCount(storySlug, requiredCount, progressLookup));
    }

    private static bool EvaluateProfileListenCountRule(
        StoryCharacterItem character,
        IReadOnlyList<string> targetSlugs,
        bool matchAll,
        int minimumCount,
        IReadOnlyDictionary<string, int> profileListenCountsByCharacterSlug)
    {
        var requiredCount = Math.Max(1, minimumCount);
        var effectiveTargets = targetSlugs.Count == 0
            ? [character.Slug.Trim().ToLowerInvariant()]
            : targetSlugs;

        return matchAll
            ? effectiveTargets.All(targetSlug => GetProfileListenCount(targetSlug, profileListenCountsByCharacterSlug) >= requiredCount)
            : effectiveTargets.Any(targetSlug => GetProfileListenCount(targetSlug, profileListenCountsByCharacterSlug) >= requiredCount);
    }

    private static bool HasStoryListenSeconds(
        string storySlug,
        int minimumSeconds,
        IReadOnlyDictionary<string, UserStoryProgressItem> progressLookup)
    {
        return progressLookup.TryGetValue(storySlug, out var progress) &&
               (progress.IsCompleted || progress.TotalListenedSeconds >= minimumSeconds);
    }

    private static bool HasStoryRepeatCount(
        string storySlug,
        int minimumCount,
        IReadOnlyDictionary<string, UserStoryProgressItem> progressLookup)
    {
        return progressLookup.TryGetValue(storySlug, out var progress) &&
               progress.ListenCount >= minimumCount;
    }

    private static int CountListenedStories(IReadOnlyDictionary<string, UserStoryProgressItem> progressLookup) =>
        progressLookup.Values.Count(progress => progress.IsCompleted || progress.TotalListenedSeconds > 0m);

    private static int CountUnlockedCharacters(Guid currentCharacterId, IReadOnlyDictionary<Guid, bool>? unlockStates)
    {
        if (unlockStates is null || unlockStates.Count == 0)
        {
            return 0;
        }

        return unlockStates.Count(entry => entry.Key != currentCharacterId && entry.Value);
    }

    private static int GetProfileListenCount(
        string targetSlug,
        IReadOnlyDictionary<string, int> profileListenCountsByCharacterSlug)
    {
        return profileListenCountsByCharacterSlug.TryGetValue(targetSlug, out var count)
            ? count
            : 0;
    }

    private static IReadOnlyList<CharacterUnlockRuleItem> NormalizeRules(IReadOnlyList<CharacterUnlockRuleItem>? rules)
    {
        return (rules ?? Array.Empty<CharacterUnlockRuleItem>())
            .Select(rule =>
            {
                var normalizedRuleType = NormalizeRuleType(rule.RuleType);
                if (string.IsNullOrWhiteSpace(normalizedRuleType))
                {
                    return null;
                }

                return new CharacterUnlockRuleItem(
                    RuleType: normalizedRuleType,
                    TargetSlugs: NormalizeSlugs(rule.TargetSlugs),
                    TargetMatchMode: NormalizeMatchMode(rule.TargetMatchMode),
                    MinimumCount: Math.Max(0, rule.MinimumCount),
                    MinimumSeconds: Math.Max(0, rule.MinimumSeconds));
            })
            .Where(static rule => rule is not null)
            .Cast<CharacterUnlockRuleItem>()
            .ToArray();
    }

    private static string? NormalizeRuleType(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            RuleTypeStoryListenSeconds => RuleTypeStoryListenSeconds,
            RuleTypeStoryRepeatCount => RuleTypeStoryRepeatCount,
            RuleTypeStoryCount => RuleTypeStoryCount,
            RuleTypeUnlockedCharacterCount => RuleTypeUnlockedCharacterCount,
            RuleTypeProfileListenCount => RuleTypeProfileListenCount,
            _ => null
        };
    }

    private static bool IsStoryRuleType(string? ruleType) =>
        string.Equals(ruleType, RuleTypeStoryListenSeconds, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(ruleType, RuleTypeStoryRepeatCount, StringComparison.OrdinalIgnoreCase);

    private static string NormalizeMatchMode(string? value) =>
        string.Equals(value, MatchModeAll, StringComparison.OrdinalIgnoreCase)
            ? MatchModeAll
            : MatchModeAny;

    private static IReadOnlyList<string> NormalizeSlugs(IEnumerable<string>? values)
    {
        return (values ?? Array.Empty<string>())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim().ToLowerInvariant())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
