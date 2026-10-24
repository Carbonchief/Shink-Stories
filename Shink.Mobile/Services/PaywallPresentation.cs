using System.Security.Cryptography;

namespace Shink.Mobile.Services;

public static class PaywallPresentation
{
    public static string Period(int months) => months >= 12 ? "per jaar" : "per maand";

    public static string PurchaseLabel(string? localizedPrice, int months, bool signedIn) =>
        string.IsNullOrWhiteSpace(localizedPrice) ? "Tans nie beskikbaar nie" :
        !signedIn ? "Teken in om voort te gaan" :
        $"Kry toegang • {localizedPrice} {Period(months)}";

    public static string RenewalDisclosure(string? localizedPrice, int months, bool apple)
    {
        if (string.IsNullOrWhiteSpace(localizedPrice))
            return "Kies 'n beskikbare plan om die prys en hernuwing te sien.";
        var cancellation = apple
            ? "Kanselleer minstens 24 uur voor hernuwing in jou App Store-intekeninge."
            : "Bestuur of kanselleer in Google Play.";
        return $"Betaling by bevestiging. Hernu outomaties teen {localizedPrice} {Period(months)} totdat jy kanselleer. {cancellation}";
    }

    public static bool IsAnnualBetterValue(long? monthlyMicros, string? monthlyCurrency,
        long? annualMicros, string? annualCurrency) =>
        monthlyMicros is > 0 && annualMicros is > 0 &&
        !string.IsNullOrWhiteSpace(monthlyCurrency) &&
        string.Equals(monthlyCurrency, annualCurrency, StringComparison.OrdinalIgnoreCase) &&
        (decimal)annualMicros.Value < (decimal)monthlyMicros.Value * 12;
}

// A fresh reading challenge protects the parent area; it is never a persisted
// account permission or a substitute for the store's purchase confirmation.
public sealed class PaywallParentChallenge
{
    private static readonly string[] Words = ["nul", "een", "twee", "drie", "vier", "vyf", "ses", "sewe", "agt", "nege"];
    private readonly string _answer;
    public string Instruction { get; }

    public PaywallParentChallenge(int first, int second, int third, int fourth)
    {
        int[] digits = [first, second, third, fourth];
        if (digits.Any(d => d is < 0 or > 9)) throw new ArgumentOutOfRangeException(nameof(first));
        _answer = string.Concat(digits);
        Instruction = "Hierdie area is vir ouers. Tik die volgende syfers in volgorde: " +
            string.Join(" · ", digits.Select(d => Words[d])) + ".";
    }

    public static PaywallParentChallenge Create() => new(
        RandomNumberGenerator.GetInt32(1, 10), RandomNumberGenerator.GetInt32(1, 10),
        RandomNumberGenerator.GetInt32(1, 10), RandomNumberGenerator.GetInt32(1, 10));

    public bool IsCorrect(string? response) => string.Equals(response?.Trim(), _answer, StringComparison.Ordinal);
}
