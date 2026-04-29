namespace Shink.Services;

public sealed class PayFastOptions
{
    public const string SectionName = "PayFast";

    public string MerchantId { get; set; } = string.Empty;

    public string MerchantKey { get; set; } = string.Empty;

    public string Passphrase { get; set; } = string.Empty;

    public string ProcessUrl { get; set; } = "https://sandbox.payfast.co.za/eng/process";

    public string ValidateUrl { get; set; } = "https://sandbox.payfast.co.za/eng/query/validate";

    public string ApiBaseUrl { get; set; } = "https://api.payfast.co.za";

    public bool UseSandboxApi { get; set; }

    public string ReturnUrlPath { get; set; } = "/opsies";

    public string CancelUrlPath { get; set; } = "/opsies";

    public string NotifyUrlPath { get; set; } = "/api/payfast/notify";

    public string PublicBaseUrl { get; set; } = string.Empty;
}
