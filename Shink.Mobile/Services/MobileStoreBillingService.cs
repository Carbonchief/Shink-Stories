using System.Security.Cryptography;
using System.Text;
using Plugin.InAppBilling;

namespace Shink.Mobile.Services;

public interface IMobileStoreBillingService
{
    Task<IReadOnlyList<MobileStoreProduct>> GetProductsAsync(
        IReadOnlyList<string> productIds,
        CancellationToken cancellationToken = default);

    Task<MobileStorePurchaseResult> PurchaseAsync(
        string productId,
        string? accountEmail,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MobileStorePurchase>> RestoreAsync(
        CancellationToken cancellationToken = default);

    Task<bool> FinalizeAsync(
        MobileStorePurchase purchase,
        CancellationToken cancellationToken = default);
}

public sealed record MobileStoreProduct(
    string ProductId,
    string Name,
    string Description,
    string LocalizedPrice,
    string? CurrencyCode,
    long? MicrosPrice);

public sealed record MobileStorePurchase(
    string Provider,
    string ProductId,
    string ProviderPaymentId,
    string? ProviderTransactionId,
    string? ProviderToken,
    string? ReceiptData,
    bool NeedsFinalization,
    bool IsRestored);

public sealed record MobileStorePurchaseResult(
    bool IsSuccess,
    bool IsCancelled,
    bool IsPending,
    MobileStorePurchase? Purchase,
    string? ErrorMessage = null);

public sealed class MobileStoreBillingService : IMobileStoreBillingService
{
    public async Task<IReadOnlyList<MobileStoreProduct>> GetProductsAsync(
        IReadOnlyList<string> productIds,
        CancellationToken cancellationToken = default)
    {
        var normalizedProductIds = productIds
            .Where(productId => !string.IsNullOrWhiteSpace(productId))
            .Select(productId => productId.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (normalizedProductIds.Length == 0)
        {
            return Array.Empty<MobileStoreProduct>();
        }

        var billing = CrossInAppBilling.Current;
        try
        {
            if (!await billing.ConnectAsync(true))
            {
                return Array.Empty<MobileStoreProduct>();
            }

            var products = await billing.GetProductInfoAsync(
                ItemType.Subscription,
                normalizedProductIds);

            return (products ?? Array.Empty<InAppBillingProduct>())
                .Where(product => !string.IsNullOrWhiteSpace(product.ProductId))
                .Select(product => new MobileStoreProduct(
                    product.ProductId,
                    product.Name ?? string.Empty,
                    product.Description ?? string.Empty,
                    product.LocalizedPrice ?? string.Empty,
                    product.CurrencyCode,
                    product.MicrosPrice))
                .ToArray();
        }
        catch (InAppBillingPurchaseException)
        {
            return Array.Empty<MobileStoreProduct>();
        }
        finally
        {
            await DisconnectAsync(billing, cancellationToken);
        }
    }

    public async Task<MobileStorePurchaseResult> PurchaseAsync(
        string productId,
        string? accountEmail,
        CancellationToken cancellationToken = default)
    {
        var normalizedProductId = productId.Trim();
        var billing = CrossInAppBilling.Current;
        try
        {
            if (!await billing.ConnectAsync(true))
            {
                return new MobileStorePurchaseResult(false, false, false, null, "Die winkel kon nie oopgemaak word nie.");
            }

            var purchase = await billing.PurchaseAsync(
                normalizedProductId,
                ItemType.Subscription,
                BuildObfuscatedAccountId(accountEmail),
                null,
                null);
            if (purchase is null)
            {
                return new MobileStorePurchaseResult(false, false, false, null, "Die aankoop kon nie voltooi word nie.");
            }

            if (purchase.State is PurchaseState.PaymentPending or PurchaseState.Deferred)
            {
                return new MobileStorePurchaseResult(false, false, true, null, "Die betaling wag nog op bevestiging.");
            }

            if (purchase.State is PurchaseState.Canceled)
            {
                return new MobileStorePurchaseResult(false, true, false, null);
            }

            if (purchase.State is not (PurchaseState.Purchased or PurchaseState.Restored))
            {
                return new MobileStorePurchaseResult(false, false, false, null, "Die aankoop kon nie bevestig word nie.");
            }

            return new MobileStorePurchaseResult(
                true,
                false,
                false,
                ToStorePurchase(billing, purchase, isRestored: purchase.State == PurchaseState.Restored));
        }
        catch (InAppBillingPurchaseException exception)
        {
            return new MobileStorePurchaseResult(
                false,
                IsCancellation(exception),
                false,
                null,
                IsCancellation(exception)
                    ? null
                    : "Die winkelbetaling kon nie nou voltooi word nie. Probeer asseblief weer.");
        }
        catch (Exception)
        {
            return new MobileStorePurchaseResult(
                false,
                false,
                false,
                null,
                "Die winkelbetaling kon nie nou voltooi word nie. Probeer asseblief weer.");
        }
        finally
        {
            await DisconnectAsync(billing, cancellationToken);
        }
    }

    public async Task<IReadOnlyList<MobileStorePurchase>> RestoreAsync(
        CancellationToken cancellationToken = default)
    {
        var billing = CrossInAppBilling.Current;
        try
        {
            if (!await billing.ConnectAsync(true))
            {
                return Array.Empty<MobileStorePurchase>();
            }

            var purchases = await billing.GetPurchasesAsync(ItemType.Subscription);
            return (purchases ?? Array.Empty<InAppBillingPurchase>())
                .Where(purchase => purchase.State is PurchaseState.Purchased or PurchaseState.Restored)
                .Select(purchase => ToStorePurchase(billing, purchase, isRestored: true))
                .ToArray();
        }
        catch (InAppBillingPurchaseException)
        {
            return Array.Empty<MobileStorePurchase>();
        }
        finally
        {
            await DisconnectAsync(billing, cancellationToken);
        }
    }

    public async Task<bool> FinalizeAsync(
        MobileStorePurchase purchase,
        CancellationToken cancellationToken = default)
    {
        if (!purchase.NeedsFinalization || string.IsNullOrWhiteSpace(purchase.ProviderTransactionId))
        {
            return true;
        }

        var billing = CrossInAppBilling.Current;
        try
        {
            if (!await billing.ConnectAsync(true))
            {
                return false;
            }

            var finalizationResults = await billing.FinalizePurchaseAsync(new[] { purchase.ProviderTransactionId });
            return finalizationResults.Any(result => result.Success);
        }
        catch (InAppBillingPurchaseException)
        {
            return false;
        }
        finally
        {
            await DisconnectAsync(billing, cancellationToken);
        }
    }

    private static MobileStorePurchase ToStorePurchase(
        IInAppBilling billing,
        InAppBillingPurchase purchase,
        bool isRestored)
    {
#if IOS
        const string provider = "apple";
        var paymentId = purchase.OriginalTransactionIdentifier
            ?? purchase.TransactionIdentifier
            ?? purchase.Id;
        var transactionId = purchase.TransactionIdentifier ?? purchase.Id;
        return new MobileStorePurchase(
            provider,
            purchase.ProductId,
            paymentId,
            transactionId,
            null,
            billing.ReceiptData,
            NeedsFinalization: false,
            isRestored);
#elif ANDROID
        const string provider = "google_play";
        var paymentId = purchase.PurchaseToken ?? purchase.Id;
        return new MobileStorePurchase(
            provider,
            purchase.ProductId,
            paymentId,
            purchase.Id,
            purchase.PurchaseToken,
            null,
            purchase.IsAcknowledged is false,
            isRestored);
#else
        throw new PlatformNotSupportedException("Mobiele winkelbetalings word net op iOS en Android ondersteun.");
#endif
    }

    private static async Task DisconnectAsync(IInAppBilling billing, CancellationToken cancellationToken)
    {
        if (billing.IsConnected)
        {
            try
            {
                await billing.DisconnectAsync();
            }
            catch
            {
                // Disconnecting is best effort after a store operation.
            }
        }
    }

    private static string BuildObfuscatedAccountId(string? accountEmail)
    {
        var normalized = string.IsNullOrWhiteSpace(accountEmail)
            ? Guid.NewGuid().ToString("N")
            : accountEmail.Trim().ToLowerInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static bool IsCancellation(InAppBillingPurchaseException exception) =>
        exception.PurchaseError == PurchaseError.UserCancelled;
}
