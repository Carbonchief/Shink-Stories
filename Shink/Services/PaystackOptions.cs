namespace Shink.Services;

public sealed class PaystackOptions
{
    public const string SectionName = "Paystack";

    public string SecretKey { get; set; } = string.Empty;

    public string InitializeUrl { get; set; } = "https://api.paystack.co/transaction/initialize";

    public string VerifyUrl { get; set; } = "https://api.paystack.co/transaction/verify";

    public string ChargeAuthorizationUrl { get; set; } = "https://api.paystack.co/transaction/charge_authorization";

    public string CallbackUrlPath { get; set; } = "/opsies";

    public string WebhookUrlPath { get; set; } = "/api/paystack/webhook";

    public string PublicBaseUrl { get; set; } = string.Empty;

    public Dictionary<string, string> PlanCodes { get; set; } = [];
}
