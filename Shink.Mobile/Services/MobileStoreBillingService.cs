using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Plugin.InAppBilling;
#if IOS
using Foundation;
using StoreKit;
#endif

namespace Shink.Mobile.Services;

public interface IMobileStoreBillingService
{
    event EventHandler? PurchasesUpdated;
    Task<IReadOnlyList<MobileStorePurchase>> GetPendingPurchasesAsync(CancellationToken cancellationToken = default);

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
    string? FinalizationId,
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
    public MobileStoreBillingService()
    {
#if ANDROID
        InAppBillingImplementation.OnAndroidPurchasesUpdated = (_, _) => PurchasesUpdated?.Invoke(this, EventArgs.Empty);
#endif
    }

    private readonly SemaphoreSlim _gate = new(1, 1);
    public event EventHandler? PurchasesUpdated;
#if IOS
    private StoreTransactionObserver? _observer;
    private TaskCompletionSource<bool>? _deferred;
    private string? _purchasingProduct;

    private void InitializeAppleObserver()
    {
        if (_observer is not null) return;
        InAppBillingImplementation.FinishAllTransactions = false;
        _observer = new StoreTransactionObserver(this);
        SKPaymentQueue.DefaultQueue.AddTransactionObserver(_observer);
    }

    private sealed class StoreTransactionObserver(MobileStoreBillingService owner) : SKPaymentTransactionObserver
    {
        public override void UpdatedTransactions(SKPaymentQueue queue, SKPaymentTransaction[] transactions)
        {
            foreach (var transaction in transactions)
            {
                if (transaction.TransactionState == SKPaymentTransactionState.Failed)
                    queue.FinishTransaction(transaction);
                if (transaction.TransactionState == SKPaymentTransactionState.Deferred &&
                    transaction.Payment?.ProductIdentifier == owner._purchasingProduct)
                    owner._deferred?.TrySetResult(true);
            }
            if (transactions.Any(t => t.TransactionState is SKPaymentTransactionState.Purchased or SKPaymentTransactionState.Restored))
                owner.PurchasesUpdated?.Invoke(owner, EventArgs.Empty);
        }
    }
#endif

    private async Task<T> SerializedAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
#if IOS
            await MainThread.InvokeOnMainThreadAsync(InitializeAppleObserver);
#endif
            return await operation();
        }
        finally { _gate.Release(); }
    }

    private async Task<T> TimedAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(45));
        return await SerializedAsync(() => operation(timeout.Token), timeout.Token);
    }

    public Task<IReadOnlyList<MobileStoreProduct>> GetProductsAsync(IReadOnlyList<string> ids, CancellationToken token = default) =>
        TimedAsync(ct => GetProductsCoreAsync(ids, ct), token);
    public Task<MobileStorePurchaseResult> PurchaseAsync(string id, string? email, CancellationToken token = default) =>
        SerializedAsync(() => PurchaseCoreAsync(id, email, token), token);
    public Task<IReadOnlyList<MobileStorePurchase>> RestoreAsync(CancellationToken token = default) =>
        TimedAsync(ct => RestoreCoreAsync(ct), token);
    public Task<bool> FinalizeAsync(MobileStorePurchase purchase, CancellationToken token = default) =>
        TimedAsync(ct => FinalizeCoreAsync(purchase, ct), token);

    public Task<IReadOnlyList<MobileStorePurchase>> GetPendingPurchasesAsync(CancellationToken token = default) =>
        SerializedAsync<IReadOnlyList<MobileStorePurchase>>(async () =>
        {
#if IOS
            return await MainThread.InvokeOnMainThreadAsync(() => SKPaymentQueue.DefaultQueue.Transactions
                .Where(t => t.TransactionState is SKPaymentTransactionState.Purchased or SKPaymentTransactionState.Restored)
                .Select(t => new MobileStorePurchase("apple", t.Payment.ProductIdentifier,
                    t.OriginalTransaction?.TransactionIdentifier ?? t.TransactionIdentifier ?? string.Empty,
                    t.TransactionIdentifier, null, t.TransactionIdentifier, true,
                    t.TransactionState == SKPaymentTransactionState.Restored)).ToArray());
#else
            return (await RestoreCoreAsync(token)).Where(p => p.NeedsFinalization).ToArray();
#endif
        }, token);

    private async Task<IReadOnlyList<MobileStoreProduct>> GetProductsCoreAsync(
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

#if IOS
        var appleProducts = await GetAppleProductsAsync(normalizedProductIds, cancellationToken);
#if DEBUG
        var localStoreKitProducts = GetDebugStoreKitProducts(normalizedProductIds);
        if (localStoreKitProducts.Count > 0)
        {
            var mergedProducts = appleProducts
                .Concat(localStoreKitProducts)
                .GroupBy(product => product.ProductId, StringComparer.Ordinal)
                .Select(group => group.First())
                .ToArray();
            if (mergedProducts.Length > appleProducts.Count)
            {
                return mergedProducts;
            }
        }
#endif
        if (appleProducts.Count > 0)
        {
            return appleProducts;
        }
#endif

        var billing = CrossInAppBilling.Current;
        try
        {
            if (!await billing.ConnectAsync(true, cancellationToken))
            {
                return Array.Empty<MobileStoreProduct>();
            }

            var products = await billing.GetProductInfoAsync(
                ItemType.Subscription,
                normalizedProductIds,
                cancellationToken);

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

#if IOS
    private static async Task<IReadOnlyList<MobileStoreProduct>> GetAppleProductsAsync(
        IReadOnlyList<string> productIds,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        cancellationToken = timeout.Token;
        var completion = new TaskCompletionSource<IReadOnlyList<MobileStoreProduct>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        SKProductsRequest? request = null;
        EventHandler<SKProductsRequestResponseEventArgs>? responseHandler = null;
        EventHandler<SKRequestErrorEventArgs>? errorHandler = null;

        void Finish()
        {
            if (request is null)
            {
                return;
            }

            if (responseHandler is not null)
            {
                request.ReceivedResponse -= responseHandler;
            }

            if (errorHandler is not null)
            {
                request.RequestFailed -= errorHandler;
            }
        }

        responseHandler = (_, args) =>
        {
            Finish();
            var products = args.Response.Products
                .Select(ToMobileStoreProduct)
                .ToArray();
            completion.TrySetResult(products);
            request?.Dispose();
        };
        errorHandler = (_, _) =>
        {
            Finish();
            completion.TrySetResult(Array.Empty<MobileStoreProduct>());
            request?.Dispose();
        };

        try
        {
            request = new SKProductsRequest(new NSSet(productIds.ToArray()));
            request.ReceivedResponse += responseHandler;
            request.RequestFailed += errorHandler;
            request.Start();

            using var registration = cancellationToken.Register(() =>
            {
                request?.Cancel();
                Finish();
                completion.TrySetCanceled(cancellationToken);
            });

            return await completion.Task.ConfigureAwait(false);
        }
        catch
        {
            Finish();
            request?.Dispose();
            return Array.Empty<MobileStoreProduct>();
        }
    }

    private static MobileStoreProduct ToMobileStoreProduct(SKProduct product)
    {
        using var formatter = new NSNumberFormatter
        {
            FormatterBehavior = NSNumberFormatterBehavior.Version_10_4,
            NumberStyle = NSNumberFormatterStyle.Currency,
            Locale = product.PriceLocale
        };

        return new MobileStoreProduct(
            product.ProductIdentifier,
            product.LocalizedTitle ?? string.Empty,
            product.LocalizedDescription ?? string.Empty,
            formatter.StringFromNumber(product.Price) ?? product.Price.StringValue,
            product.PriceLocale?.CurrencyCode,
            null);
    }

#if DEBUG
    private static IReadOnlyList<MobileStoreProduct> GetDebugStoreKitProducts(
        IReadOnlyList<string> productIds)
    {
        try
        {
            var path = NSBundle.MainBundle.PathForResource("SchinkStories", "storekit");
            if (string.IsNullOrWhiteSpace(path))
            {
                return Array.Empty<MobileStoreProduct>();
            }

            var wantedProductIds = productIds.ToHashSet(StringComparer.Ordinal);
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (!document.RootElement.TryGetProperty("subscriptionGroups", out var groups))
            {
                return Array.Empty<MobileStoreProduct>();
            }

            var products = new List<MobileStoreProduct>();
            foreach (var group in groups.EnumerateArray())
            {
                if (!group.TryGetProperty("subscriptions", out var subscriptions))
                {
                    continue;
                }

                foreach (var subscription in subscriptions.EnumerateArray())
                {
                    if (!subscription.TryGetProperty("productID", out var productIdElement))
                    {
                        continue;
                    }

                    var productId = productIdElement.GetString();
                    if (string.IsNullOrWhiteSpace(productId) || !wantedProductIds.Contains(productId))
                    {
                        continue;
                    }

                    if (!subscription.TryGetProperty("displayPrice", out var priceElement))
                    {
                        continue;
                    }

                    var displayPrice = priceElement.GetString();
                    if (string.IsNullOrWhiteSpace(displayPrice))
                    {
                        continue;
                    }

                    products.Add(new MobileStoreProduct(
                        productId,
                        string.Empty,
                        string.Empty,
                        displayPrice.StartsWith("R", StringComparison.OrdinalIgnoreCase)
                            ? displayPrice
                            : $"R{displayPrice}",
                        "ZAR",
                        null));
                }
            }

            return products;
        }
        catch
        {
            return Array.Empty<MobileStoreProduct>();
        }
    }
#endif
#endif

    private async Task<MobileStorePurchaseResult> PurchaseCoreAsync(
        string productId,
        string? accountEmail,
        CancellationToken cancellationToken = default)
    {
        var normalizedProductId = productId.Trim();
        var billing = CrossInAppBilling.Current;
        try
        {
            if (!await billing.ConnectAsync(true, cancellationToken))
            {
                return new MobileStorePurchaseResult(false, false, false, null, "Die winkel kon nie oopgemaak word nie.");
            }

#if IOS
            using var purchaseCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _purchasingProduct = normalizedProductId;
            _deferred = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var purchaseTask = billing.PurchaseAsync(normalizedProductId, ItemType.Subscription,
                BuildObfuscatedAccountId(accountEmail), null, null, purchaseCancellation.Token);
            if (await Task.WhenAny(purchaseTask, _deferred.Task) != purchaseTask)
            {
                purchaseCancellation.Cancel();
                try { await purchaseTask; } catch (OperationCanceledException) { }
                return new(false, false, true, null, "Die aankoop wag op goedkeuring. Ons sal dit bevestig wanneer die goedkeuring ontvang is.");
            }
            var purchase = await purchaseTask;
#else
            var purchase = await billing.PurchaseAsync(
                normalizedProductId,
                ItemType.Subscription,
                BuildObfuscatedAccountId(accountEmail),
                null,
                null,
                cancellationToken);
#endif
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
#if IOS
            _purchasingProduct = null;
            _deferred = null;
#endif
            await DisconnectAsync(billing, cancellationToken);
        }
    }

    private async Task<IReadOnlyList<MobileStorePurchase>> RestoreCoreAsync(
        CancellationToken cancellationToken = default)
    {
        var billing = CrossInAppBilling.Current;
        try
        {
            if (!await billing.ConnectAsync(true, cancellationToken))
            {
                throw new InvalidOperationException("Die winkel kon nie vir herstel verbind word nie.");
            }

            var purchases = await billing.GetPurchasesAsync(ItemType.Subscription, cancellationToken);
            return (purchases ?? Array.Empty<InAppBillingPurchase>())
                .Where(purchase => purchase.State is PurchaseState.Purchased or PurchaseState.Restored)
                .Select(purchase => ToStorePurchase(billing, purchase, isRestored: true))
                .ToArray();
        }
        finally
        {
            await DisconnectAsync(billing, cancellationToken);
        }
    }

    private async Task<bool> FinalizeCoreAsync(
        MobileStorePurchase purchase,
        CancellationToken cancellationToken = default)
    {
        if (!purchase.NeedsFinalization || string.IsNullOrWhiteSpace(purchase.FinalizationId))
        {
            return true;
        }

#if IOS
        return await MainThread.InvokeOnMainThreadAsync(() =>
        {
            var transactions = SKPaymentQueue.DefaultQueue.Transactions.Where(t =>
                t.TransactionIdentifier == purchase.FinalizationId ||
                (t.Payment?.ProductIdentifier == purchase.ProductId &&
                 t.OriginalTransaction?.TransactionIdentifier == purchase.ProviderTransactionId)).ToArray();
            foreach (var transaction in transactions) SKPaymentQueue.DefaultQueue.FinishTransaction(transaction);
            return true;
        });
#else
        var billing = CrossInAppBilling.Current;
        try
        {
            if (!await billing.ConnectAsync(true, cancellationToken))
            {
                return false;
            }

            var finalizationResults = await billing.FinalizePurchaseAsync(
                new[] { purchase.FinalizationId },
                cancellationToken);
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
#endif
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
            FinalizationId: transactionId,
            NeedsFinalization: true,
            isRestored);
#elif ANDROID
        const string provider = "google_play";
        var paymentId = purchase.PurchaseToken ?? purchase.Id;
        var finalizationId = purchase.PurchaseToken;
        return new MobileStorePurchase(
            provider,
            purchase.ProductId,
            paymentId,
            purchase.Id,
            purchase.PurchaseToken,
            finalizationId,
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
                await billing.DisconnectAsync(cancellationToken);
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
