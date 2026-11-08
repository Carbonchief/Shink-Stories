using System.Security.Claims;
using System.Text.Json.Serialization;

namespace Shink.Services;

public interface IStoreCustomerDetailsService
{
    Task<StoreCustomerDetails?> GetAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default);
    Task<bool> SaveAsync(ClaimsPrincipal user, StoreCustomerDetails details, CancellationToken cancellationToken = default);
}

public sealed record StoreCustomerDetails(
    [property: JsonPropertyName("customer_name")] string? Name = null,
    [property: JsonPropertyName("customer_email")] string? Email = null,
    [property: JsonPropertyName("customer_phone")] string? Phone = null,
    [property: JsonPropertyName("delivery_address_line_1")] string? AddressLine1 = null,
    [property: JsonPropertyName("delivery_address_line_2")] string? AddressLine2 = null,
    [property: JsonPropertyName("delivery_suburb")] string? Suburb = null,
    [property: JsonPropertyName("delivery_city")] string? City = null,
    [property: JsonPropertyName("delivery_postal_code")] string? PostalCode = null)
{
    public static string? AccountEmail(ClaimsPrincipal? user) => user?.Identity?.IsAuthenticated == true
        ? Clean(user.FindFirstValue(ClaimTypes.Email), 254)?.ToLowerInvariant() : null;

    public static StoreCustomerDetails Prefill(ClaimsPrincipal? user, SubscriberProfile? profile, StoreCustomerDetails? saved)
    {
        var email = AccountEmail(user);
        if (email is null) return new();
        // A provider fallback must never leak another account's profile into checkout.
        if (!string.Equals(profile?.Email?.Trim(), email, StringComparison.OrdinalIgnoreCase)) profile = null;
        var fullName = Clean($"{profile?.FirstName} {profile?.LastName}", 120)
            ?? Clean($"{user?.FindFirstValue("first_name")} {user?.FindFirstValue("last_name")}", 120);
        var name = Clean(saved?.Name, 120) ?? fullName ?? Clean(profile?.DisplayName, 120)
            ?? Clean(user?.FindFirstValue("display_name"), 120) ?? Clean(user?.Identity?.Name, 120);
        if (string.Equals(name, email, StringComparison.OrdinalIgnoreCase)) name = null;
        return new(name, Clean(saved?.Email, 254) ?? email,
            Clean(saved?.Phone, 40) ?? Clean(profile?.MobileNumber, 40) ?? Clean(user?.FindFirstValue("mobile_number"), 40),
            Clean(saved?.AddressLine1, 250), Clean(saved?.AddressLine2, 250), Clean(saved?.Suburb, 120),
            Clean(saved?.City, 120), Clean(saved?.PostalCode, 20));
    }

    public static StoreCustomerDetails FromOrder(StoreOrderDraft order) => new(order.CustomerName, order.CustomerEmail,
        order.CustomerPhone, order.DeliveryAddressLine1, order.DeliveryAddressLine2, order.DeliverySuburb,
        order.DeliveryCity, order.DeliveryPostalCode);

    private static string? Clean(string? value, int limit) => string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, limit)];
}

public static class StoreCheckoutPreferences
{
    public static async Task<string?> SaveIfRequestedAsync(ClaimsPrincipal user, IFormCollection form,
        StoreOrderDraft order, IStoreCustomerDetailsService service, CancellationToken cancellationToken = default)
    {
        if (form["saveDetails"] != "ja") return null;
        var email = StoreCustomerDetails.AccountEmail(user);
        if (email is null) return "Teken asseblief in om jou besonderhede vir volgende keer te stoor.";
        if (!string.Equals(email, form["expectedAccountEmail"].ToString().Trim(), StringComparison.OrdinalIgnoreCase))
            return "Jou rekening het verander. Herlaai die betaalpunt en gaan jou besonderhede na.";
        return await service.SaveAsync(user, StoreCustomerDetails.FromOrder(order), cancellationToken) ? null
            : "Ons kon nie jou besonderhede stoor nie. Probeer weer, of ontmerk die stoor-opsie om verder te betaal.";
    }
}
