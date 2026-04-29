namespace Shink.Services;

public static class PaystackSubscriptionCodeResolver
{
    public static string? ResolveSubscriptionCode(
        string? provider,
        string? sourceSystem,
        string? providerPaymentId,
        string? providerTransactionId)
    {
        if (!string.Equals(provider, "paystack", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var paymentId = Normalize(providerPaymentId);
        if (IsSubscriptionCode(paymentId))
        {
            return paymentId;
        }

        var transactionId = Normalize(providerTransactionId);
        if (string.Equals(sourceSystem, "wordpress_pmpro", StringComparison.OrdinalIgnoreCase) &&
            IsSubscriptionCode(transactionId))
        {
            return transactionId;
        }

        return null;
    }

    public static string? ResolveImportedProviderTransactionId(
        string? provider,
        string? subscriptionTransactionId,
        string? orderSubscriptionTransactionId,
        string? orderPaymentTransactionId)
    {
        var subscriptionCode = Normalize(subscriptionTransactionId);
        var orderSubscriptionCode = Normalize(orderSubscriptionTransactionId);
        var paymentTransactionId = Normalize(orderPaymentTransactionId);

        if (!string.Equals(provider, "paystack", StringComparison.OrdinalIgnoreCase))
        {
            return subscriptionCode ?? paymentTransactionId ?? orderSubscriptionCode;
        }

        if (IsSubscriptionCode(subscriptionCode))
        {
            return subscriptionCode;
        }

        if (IsSubscriptionCode(orderSubscriptionCode))
        {
            return orderSubscriptionCode;
        }

        return paymentTransactionId ?? subscriptionCode ?? orderSubscriptionCode;
    }

    public static bool IsSubscriptionCode(string? value) =>
        value?.Trim().StartsWith("SUB_", StringComparison.OrdinalIgnoreCase) == true;

    private static string? Normalize(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }
}
