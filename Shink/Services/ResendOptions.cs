namespace Shink.Services;

public sealed class ResendOptions
{
    public const string SectionName = "Resend";

    public string ApiKey { get; set; } = string.Empty;
    public string FromEmail { get; set; } = string.Empty;
    public string ToEmail { get; set; } = string.Empty;
    public string BillingManageUrl { get; set; } = string.Empty;
    public ResendTemplateOptions Templates { get; set; } = new();
}

public sealed class ResendTemplateOptions
{
    public AbandonedCartRecoveryTemplateOptions AbandonedCartRecovery { get; set; } = new();
    public SubscriptionPaymentRecoveryTemplateOptions SubscriptionPaymentRecovery { get; set; } = new();
    public StoreOrderTemplateOptions StoreOrder { get; set; } = new();
}

public sealed class AbandonedCartRecoveryTemplateOptions
{
    public string Hour1TemplateId { get; set; } = string.Empty;
    public string Hour24TemplateId { get; set; } = string.Empty;
    public string Day7TemplateId { get; set; } = string.Empty;
}

public sealed class SubscriptionPaymentRecoveryTemplateOptions
{
    public string Day1TemplateId { get; set; } = string.Empty;
    public string Day3TemplateId { get; set; } = string.Empty;
    public string Day5TemplateId { get; set; } = string.Empty;
}

public sealed class StoreOrderTemplateOptions
{
    public string CustomerConfirmationTemplateId { get; set; } = "shink-store-order-confirmation";
}
