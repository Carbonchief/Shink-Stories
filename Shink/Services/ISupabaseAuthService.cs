namespace Shink.Services;

public interface ISupabaseAuthService
{
    Task<SupabaseSignInResult> SignInWithPasswordAsync(string email, string password, CancellationToken cancellationToken = default);
    Task<SupabaseSignInResult> SignUpWithPasswordAsync(
        string email,
        string password,
        SignUpProfileData? profileData = null,
        CancellationToken cancellationToken = default);
    Task<SupabasePasswordResetResult> SendPasswordResetEmailAsync(
        string email,
        string redirectTo,
        CancellationToken cancellationToken = default);
    Task<SupabasePasswordResetResult> UpdatePasswordAsync(
        string accessToken,
        string refreshToken,
        string newPassword,
        CancellationToken cancellationToken = default);
    Task<SupabaseOAuthStartResult> StartGoogleSignInAsync(
        string redirectTo,
        bool useImplicitFlow,
        CancellationToken cancellationToken = default);
    Task<SupabaseOAuthExchangeResult> ExchangeGoogleAuthCodeAsync(
        string authCode,
        string codeVerifier,
        CancellationToken cancellationToken = default);
    Task<SupabaseOAuthExchangeResult> ExchangeGoogleImplicitSessionAsync(
        Uri callbackUri,
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

public sealed record SupabasePasswordResetResult(bool IsSuccess, string? UserEmail, string? ErrorMessage)
{
    public static SupabasePasswordResetResult Success(string? userEmail = null) => new(true, userEmail, null);

    public static SupabasePasswordResetResult Failure(string errorMessage) => new(false, null, errorMessage);
}

public sealed record SupabaseOAuthStartResult(bool IsSuccess, Uri? RedirectUri, string? CodeVerifier, string? ErrorMessage)
{
    public static SupabaseOAuthStartResult Success(Uri redirectUri, string? codeVerifier) =>
        new(true, redirectUri, codeVerifier, null);

    public static SupabaseOAuthStartResult Failure(string errorMessage) =>
        new(false, null, null, errorMessage);
}

public sealed record SupabaseOAuthExchangeResult(
    bool IsSuccess,
    string? UserEmail,
    string? FirstName,
    string? LastName,
    string? DisplayName,
    string? ErrorMessage)
{
    public static SupabaseOAuthExchangeResult Success(
        string? userEmail,
        string? firstName,
        string? lastName,
        string? displayName) =>
        new(true, userEmail, firstName, lastName, displayName, null);

    public static SupabaseOAuthExchangeResult Failure(string errorMessage) =>
        new(false, null, null, null, null, errorMessage);
}
