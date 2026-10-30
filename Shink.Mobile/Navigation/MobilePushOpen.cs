namespace Shink.Mobile.Services;

public sealed record MobilePushOpen(Guid NotificationId, Guid RegistrationId, string Type, string? Href)
{
    public static MobilePushOpen? FromValues(string? notificationId, string? registrationId, string? type, string? href) =>
        Guid.TryParse(notificationId, out var id) && id != Guid.Empty
        && Guid.TryParse(registrationId, out var registration) && registration != Guid.Empty
            ? new(id, registration, type ?? string.Empty, href) : null;
}
