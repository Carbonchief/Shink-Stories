using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace Shink.Services;

// A reference alone must never grant access to a customer's order or clear a basket.
public sealed class StoreReceiptAccess(IDataProtectionProvider provider)
{
    private const string LegacyCookieName = "shink.store.receipt";
    private readonly ITimeLimitedDataProtector _protector = provider
        .CreateProtector("Shink.Store.Receipt.v1").ToTimeLimitedDataProtector();

    public void Grant(HttpContext context, string orderReference, string? checkoutId = null) =>
        context.Response.Cookies.Append(CookieName(orderReference), CreateToken(orderReference, checkoutId), new CookieOptions
        {
            HttpOnly = true,
            Secure = context.Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Path = "/winkel",
            MaxAge = TimeSpan.FromDays(7),
            IsEssential = true
        });

    public bool CanRead(HttpContext? context, string? orderReference) => ReadGrant(context, orderReference) is not null;

    public string? GetCheckoutId(HttpContext? context, string? orderReference) => ReadGrant(context, orderReference)?.CheckoutId;

    internal string CreateToken(string reference, string? checkoutId = null)
    {
        var normalizedId = Guid.TryParseExact(checkoutId, "N", out var id) ? id.ToString("N") : null;
        return _protector.Protect(JsonSerializer.Serialize(new ReceiptGrant(reference, normalizedId)), TimeSpan.FromDays(7));
    }

    internal bool CanReadToken(string? token, string? reference) => ReadToken(token, reference) is not null;

    private ReceiptGrant? ReadGrant(HttpContext? context, string? reference)
    {
        if (context is null || string.IsNullOrWhiteSpace(reference)) return null;
        var token = context.Request.Cookies[CookieName(reference)] ?? context.Request.Cookies[LegacyCookieName];
        return ReadToken(token, reference);
    }

    private ReceiptGrant? ReadToken(string? token, string? reference)
    {
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(reference)) return null;
        try
        {
            var payload = _protector.Unprotect(token);
            // Accept already-issued receipts, which protected only the order reference.
            if (string.Equals(payload, reference, StringComparison.Ordinal)) return new ReceiptGrant(reference, null);
            var grant = JsonSerializer.Deserialize<ReceiptGrant>(payload);
            return string.Equals(grant?.OrderReference, reference, StringComparison.Ordinal) ? grant : null;
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException)
        {
            return null;
        }
    }

    private static string CookieName(string reference) =>
        $"{LegacyCookieName}.{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(reference)))[..32]}";

    private sealed record ReceiptGrant(string OrderReference, string? CheckoutId);
}
