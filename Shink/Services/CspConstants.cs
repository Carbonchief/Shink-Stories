using System.Security.Cryptography;

namespace Shink.Services;

internal static class CspConstants
{
    public const string NonceItemKey = "shink:csp_nonce";

    public static string GetOrCreateNonce(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        if (GetNonce(httpContext) is { } existingNonce)
        {
            return existingNonce;
        }

        Span<byte> buffer = stackalloc byte[16];
        RandomNumberGenerator.Fill(buffer);
        var nonce = Convert.ToBase64String(buffer);
        httpContext.Items[NonceItemKey] = nonce;
        return nonce;
    }

    public static string? GetNonce(HttpContext? httpContext)
    {
        if (httpContext?.Items.TryGetValue(NonceItemKey, out var rawValue) == true &&
            rawValue is string nonce &&
            !string.IsNullOrWhiteSpace(nonce))
        {
            return nonce;
        }

        return null;
    }
}
