using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Shink.Components.Content;
using Shink.Services;

namespace Shink.Components.Pages;

public partial class Winkel
{
    [Inject] private NavigationManager NavigationManager { get; set; } = default!;
    [Inject] private IStoreProductCatalogService StoreProductCatalogService { get; set; } = default!;
    [Inject] private IStoreOrderService StoreOrderService { get; set; } = default!;
    [Inject] private StoreReceiptAccess ReceiptAccess { get; set; } = default!;
    [Inject] private IHttpContextAccessor HttpContextAccessor { get; set; } = default!;

    [Parameter] public string? ProductSlug { get; set; }
    [SupplyParameterFromQuery(Name = "produk")] public string? LegacyProductSlug { get; set; }
    [SupplyParameterFromQuery(Name = "betaling")] public string? PaymentStatus { get; set; }
    [SupplyParameterFromQuery(Name = "verwysing")] public string? OrderReference { get; set; }
    [SupplyParameterFromQuery(Name = "boodskap")] public string? Message { get; set; }

    private IReadOnlyList<StoreProduct> Products { get; set; } = [];
    private StoreProduct? Product { get; set; }
    private StoreOrderRecord? VerifiedOrder { get; set; }
    private string? CompletedCheckoutId { get; set; }
    private string View { get; set; } = "catalog";
    private bool HasPaymentStatus => !string.IsNullOrWhiteSpace(PaymentStatus);
    private bool IsVerifiedPaid => string.Equals(VerifiedOrder?.PaymentStatus, "paid", StringComparison.OrdinalIgnoreCase);
    private string PageHeading => VerifiedOrder is not null ? "Jou bestelling" : View switch
    {
        "cart" => "Jou mandjie", "checkout" => "Betaalpunt",
        "product" => Product?.Name ?? "Produk nie beskikbaar nie", _ => "Winkel"
    };
    private string CanonicalUrl => NavigationManager.ToAbsoluteUri(Product is not null && View == "product"
        ? $"/winkel/produk/{Uri.EscapeDataString(Product.Slug)}" : View switch
        {
            "cart" => "/winkel/mandjie", "checkout" => "/winkel/afreken", _ => "/winkel"
        }).ToString();
    private string ImageUrl => NavigationManager.ToAbsoluteUri(Product?.ImagePath ?? "/branding/winkel/storie-tjommie-panda.png").ToString();
    private string MetaDescription => Product?.Description ?? "Ontdek Schink se StorieTjommies. Kies jou maatjies, voeg hulle by jou mandjie en betaal veilig met Paystack.";
    private string? PaymentMessage => PaymentStatus?.Trim().ToLowerInvariant() switch
    {
        // Only a protected receipt can confirm payment; a query parameter is not proof.
        "sukses" => "Jou betaalstatus kon nie op hierdie toestel bevestig word nie. Kontak ons met jou bestelverwysing as jy hulp nodig het.",
        "gekanselleer" => "Die betaling is gekanselleer. Jou mandjie is nog hier; jy kan weer probeer.",
        "misluk" => string.IsNullOrWhiteSpace(Message) ? "Ons kon nie die betaling voltooi nie. Probeer asseblief weer." : Message[..Math.Min(Message.Length, 400)],
        "verwerk" => "Ons wag nog vir bevestiging van jou betaling. Hou jou bestelverwysing byderhand.", _ => null
    };

    protected override async Task OnParametersSetAsync()
    {
        Products = (await StoreProductCatalogService.GetEnabledProductsAsync())
            .Where(product => product.IsEnabled).OrderBy(product => product.SortOrder)
            .ThenBy(product => product.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        var path = new Uri(NavigationManager.Uri).AbsolutePath.TrimEnd('/');
        View = path.Equals("/winkel/mandjie", StringComparison.OrdinalIgnoreCase) ? "cart"
            : path.Equals("/winkel/afreken", StringComparison.OrdinalIgnoreCase) ? "checkout"
            : path.StartsWith("/winkel/produk/", StringComparison.OrdinalIgnoreCase) || (!HasPaymentStatus && !string.IsNullOrWhiteSpace(LegacyProductSlug)) ? "product" : "catalog";
        Product = Products.FirstOrDefault(product => string.Equals(product.Slug, ProductSlug ?? LegacyProductSlug, StringComparison.OrdinalIgnoreCase));
        VerifiedOrder = null;
        CompletedCheckoutId = null;
        var context = HttpContextAccessor.HttpContext;
        if (HasPaymentStatus)
        {
            context?.Response.Headers.TryAdd("Cache-Control", "private, no-store");
            if (ReceiptAccess.CanRead(context, OrderReference))
            {
                VerifiedOrder = await StoreOrderService.GetOrderByReferenceAsync(OrderReference);
                CompletedCheckoutId = ReceiptAccess.GetCheckoutId(context, OrderReference);
            }
        }
        if (View is "cart" or "checkout") context?.Response.Headers.TryAdd("Cache-Control", "private, no-store");
        if (View == "product" && Product is null && context is not null) context.Response.StatusCode = StatusCodes.Status404NotFound;
    }

    private MarkupString StoreJsonLd => new(JsonSerializer.Serialize(
        Product is not null && View == "product" ? ProductSchema(Product) : new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org", ["@type"] = "CollectionPage", ["name"] = "Schink Stories | Winkel",
            ["description"] = MetaDescription, ["url"] = CanonicalUrl, ["inLanguage"] = "af-ZA",
            ["mainEntity"] = new Dictionary<string, object?>
            {
                ["@type"] = "ItemList", ["itemListElement"] = Products.Select((product, index) => new Dictionary<string, object?>
                {
                    ["@type"] = "ListItem", ["position"] = index + 1, ["item"] = ProductSchema(product)
                }).ToArray()
            }
        }));

    private Dictionary<string, object?> ProductSchema(StoreProduct product) => new()
    {
        ["@context"] = "https://schema.org", ["@type"] = "Product", ["name"] = product.Name,
        ["description"] = product.Description, ["image"] = NavigationManager.ToAbsoluteUri(product.ImagePath).ToString(),
        ["url"] = NavigationManager.ToAbsoluteUri($"/winkel/produk/{Uri.EscapeDataString(product.Slug)}").ToString(),
        ["offers"] = new Dictionary<string, object?>
        {
            ["@type"] = "Offer", ["price"] = product.UnitPriceDisplay, ["priceCurrency"] = "ZAR",
            ["url"] = NavigationManager.ToAbsoluteUri($"/winkel/produk/{Uri.EscapeDataString(product.Slug)}").ToString()
        }
    };
}
