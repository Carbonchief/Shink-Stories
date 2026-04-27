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
    public ContactTemplateOptions Contact { get; set; } = new();
    public SubscriptionPaymentRecoveryTemplateOptions SubscriptionPaymentRecovery { get; set; } = new();
    public SubscriptionNotificationTemplateOptions SubscriptionNotifications { get; set; } = new();
    public StoreOrderTemplateOptions StoreOrder { get; set; } = new();
    public AdminOpsTemplateOptions AdminOps { get; set; } = new();
}

public sealed class AbandonedCartRecoveryTemplateOptions
{
    public string Hour1TemplateId { get; set; } = string.Empty;
    public string Hour24TemplateId { get; set; } = string.Empty;
    public string Day7TemplateId { get; set; } = string.Empty;
}

public sealed class SubscriptionPaymentRecoveryTemplateOptions
{
    public string Day1TemplateId { get; set; } = "shink-subscription-recovery-day-1";
    public string Day3TemplateId { get; set; } = "shink-subscription-recovery-day-3";
    public string Day5TemplateId { get; set; } = "shink-subscription-recovery-day-5";
}

public sealed class ContactTemplateOptions
{
    public string AutoReplyTemplateId { get; set; } = "shink-contact-auto-reply";
}

public sealed class SubscriptionNotificationTemplateOptions
{
    public string ConfirmationTemplateId { get; set; } = "shink-subscription-confirmation";
    public string EndedTemplateId { get; set; } = "shink-subscription-ended";
}

public sealed class StoreOrderTemplateOptions
{
    public string CustomerConfirmationTemplateId { get; set; } = "shink-store-order-confirmation";
}

public sealed class AdminOpsTemplateOptions
{
    public string AlertTemplateId { get; set; } = "shink-admin-ops-alert";
}
