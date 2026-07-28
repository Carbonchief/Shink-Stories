namespace Shink.Services;

public interface ISchoolSeatNotificationEmailService
{
    Task SendSeatAssignedEmailAsync(
        SchoolSeatAssignmentEmailRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record SchoolSeatAssignmentEmailRequest(
    string RecipientEmail,
    string? RecipientName,
    string? SchoolName,
    string PasswordSetupUrl);
