namespace Shink.Services;

public interface IContactEmailService
{
    Task SendContactEmailAsync(ContactFormSubmission submission, CancellationToken cancellationToken = default);
}
