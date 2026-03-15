namespace Shink.Services;

public interface ISupabaseAuthService
{
    Task<SupabaseSignInResult> SignInWithPasswordAsync(string email, string password, CancellationToken cancellationToken = default);
    Task<SupabaseSignInResult> SignUpWithPasswordAsync(
        string email,
        string password,
        SignUpProfileData? profileData = null,
        CancellationToken cancellationToken = default);
}

public sealed record SignUpProfileData(
    string? FirstName,
    string? LastName,
    string? DisplayName,
    string? MobileNumber);

public sealed record SupabaseSignInResult(bool IsSuccess, string? UserEmail, string? ErrorMessage)
{
    public static SupabaseSignInResult Success(string? userEmail) => new(true, userEmail, null);

    public static SupabaseSignInResult Failure(string errorMessage) => new(false, null, errorMessage);
}
