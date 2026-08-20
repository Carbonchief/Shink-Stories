using System.Security.Cryptography;

namespace Shink.Services;

public static class ReferralCodeRules
{
    public const int CodeLength = 12;
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public static string Generate()
    {
        Span<char> characters = stackalloc char[CodeLength];
        characters[0] = 'R';
        characters[1] = 'F';

        for (var index = 2; index < characters.Length; index++)
        {
            characters[index] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        return new string(characters);
    }

    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim().ToUpperInvariant();
        if (normalized.Length != CodeLength || !normalized.StartsWith("RF", StringComparison.Ordinal))
        {
            return null;
        }

        return normalized.All(character => Alphabet.Contains(character) || character == 'R' || character == 'F')
            ? normalized
            : null;
    }
}
