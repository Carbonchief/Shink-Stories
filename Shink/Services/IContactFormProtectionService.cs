namespace Shink.Services;

public interface IContactFormProtectionService
{
    bool TryValidateSubmission(string clientId, string email, string subject, string message, out string errorMessage);
}
