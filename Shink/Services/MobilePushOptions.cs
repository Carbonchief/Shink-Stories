namespace Shink.Services;

public sealed class MobilePushOptions
{
    public const string SectionName = "MobilePush";
    public bool Enabled { get; set; }
    public string AppleTeamId { get; set; } = string.Empty;
    public string AppleKeyId { get; set; } = string.Empty;
    public string ApplePrivateKey { get; set; } = string.Empty;
    public string AppleBundleId { get; set; } = "com.schink.stories.mobile";
    public string FirebaseProjectId { get; set; } = string.Empty;
    public string FirebaseServiceAccountJson { get; set; } = string.Empty;

    public bool Supports(string platform) => Enabled && (platform switch
    {
        "apns" => !string.IsNullOrWhiteSpace(AppleTeamId) && !string.IsNullOrWhiteSpace(AppleKeyId)
                  && !string.IsNullOrWhiteSpace(ApplePrivateKey) && !string.IsNullOrWhiteSpace(AppleBundleId),
        "fcm" => !string.IsNullOrWhiteSpace(FirebaseProjectId) && !string.IsNullOrWhiteSpace(FirebaseServiceAccountJson),
        _ => false
    });
}

public sealed record MobilePushRegistrationRequest(
    Guid InstallationId, string InstallationSecret, string Platform, string Environment, string Token)
{
    public bool IsValid => InstallationId != Guid.Empty && MobilePushRevocationRequest.IsValidSecret(InstallationSecret)
        && Platform is "apns" or "fcm" && Environment is "production" or "sandbox"
        && (Platform != "fcm" || Environment == "production")
        && !string.IsNullOrEmpty(Token) && Token.Length is >= 16 and <= 2048 && !Token.Any(char.IsControl)
        && (Platform != "apns" || Token.Length <= 512 && Token.Length % 2 == 0 && Token.All(Uri.IsHexDigit));
}

public sealed record MobilePushRevocationRequest(Guid InstallationId, string InstallationSecret)
{
    public bool IsValid => InstallationId != Guid.Empty && IsValidSecret(InstallationSecret);
    public static bool IsValidSecret(string? value) => value?.Length == 64 && value.All(Uri.IsHexDigit);
}

public sealed record MobilePushDelivery(
    Guid DeliveryId, Guid LeaseId, Guid InstallationId, Guid RegistrationId, Guid NotificationId,
    string Platform, string Environment, string Token, string Title, string? Body, string NotificationType, string? Href);

public sealed record MobilePushSendResult(string Result, string? Error = null, int RetrySeconds = 60);
