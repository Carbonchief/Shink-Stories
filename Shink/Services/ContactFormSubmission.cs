namespace Shink.Services;

public sealed record ContactFormSubmission(
    string Name,
    string Email,
    string Subject,
    string Message);
