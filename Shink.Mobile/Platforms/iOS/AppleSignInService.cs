using System.Security.Cryptography;
using System.Text;
using AuthenticationServices;
using Foundation;
using UIKit;

namespace Shink.Mobile.Platforms.iOS;

public sealed record AppleSignInResult(
    bool IsSuccess,
    bool IsCancelled,
    string? IdentityToken,
    string? Nonce,
    string? FirstName,
    string? LastName,
    string? DisplayName,
    string? ErrorMessage)
{
    public static AppleSignInResult Cancelled() =>
        new(false, true, null, null, null, null, null, null);

    public static AppleSignInResult Failure(string errorMessage) =>
        new(false, false, null, null, null, null, null, errorMessage);
}

public sealed class AppleSignInService : ASAuthorizationControllerDelegate, IASAuthorizationControllerPresentationContextProviding
{
    private TaskCompletionSource<AppleSignInResult>? _completionSource;
    private ASAuthorizationController? _authorizationController;
    private string? _rawNonce;

    public Task<AppleSignInResult> SignInAsync()
    {
        if (_completionSource is not null)
        {
            return Task.FromResult(AppleSignInResult.Failure("Apple aanmelding is reeds aan die gang. Probeer asseblief weer."));
        }

        _rawNonce = CreateNonce();
        var request = new ASAuthorizationAppleIdProvider().CreateRequest();
        request.RequestedScopes = new[]
        {
            ASAuthorizationScope.Email,
            ASAuthorizationScope.FullName
        };
        request.Nonce = HashNonce(_rawNonce);

        _completionSource = new TaskCompletionSource<AppleSignInResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _authorizationController = new ASAuthorizationController(new ASAuthorizationRequest[] { request });
        _authorizationController.Delegate = this;
        _authorizationController.PresentationContextProvider = this;
        _authorizationController.PerformRequests();
        return _completionSource.Task;
    }

    public override void DidComplete(ASAuthorizationController controller, ASAuthorization authorization)
    {
        var credential = authorization.GetCredential<ASAuthorizationAppleIdCredential>();
        if (credential is null)
        {
            Complete(AppleSignInResult.Failure("Apple aanmelding kon nie bevestig word nie. Probeer asseblief weer."));
            return;
        }

        var identityToken = credential.IdentityToken is null
            ? null
            : Encoding.UTF8.GetString(credential.IdentityToken.ToArray());
        var firstName = credential.FullName?.GivenName;
        var lastName = credential.FullName?.FamilyName;
        var displayName = $"{firstName} {lastName}".Trim();

        if (string.IsNullOrWhiteSpace(identityToken) || string.IsNullOrWhiteSpace(_rawNonce))
        {
            Complete(AppleSignInResult.Failure("Apple aanmelding kon nie bevestig word nie. Probeer asseblief weer."));
            return;
        }

        Complete(new AppleSignInResult(
            IsSuccess: true,
            IsCancelled: false,
            IdentityToken: identityToken,
            Nonce: _rawNonce,
            FirstName: firstName,
            LastName: lastName,
            DisplayName: string.IsNullOrWhiteSpace(displayName) ? null : displayName,
            ErrorMessage: null));
    }

    public override void DidComplete(ASAuthorizationController controller, NSError error)
    {
        if (error.Code == (long)ASAuthorizationError.Canceled)
        {
            Complete(AppleSignInResult.Cancelled());
            return;
        }

        Complete(AppleSignInResult.Failure(
            "Apple aanmelding kon nie voltooi word nie. Probeer asseblief weer."));
    }

    public UIWindow GetPresentationAnchor(ASAuthorizationController controller)
    {
        var window = UIApplication.SharedApplication.Windows?
            .FirstOrDefault(candidate => candidate.IsKeyWindow)
            ?? UIApplication.SharedApplication.Windows?.FirstOrDefault();

        return window ?? throw new InvalidOperationException("Geen aktiewe iOS venster vir Apple aanmelding nie.");
    }

    private void Complete(AppleSignInResult result)
    {
        var completionSource = _completionSource;
        _completionSource = null;
        _authorizationController = null;
        _rawNonce = null;
        completionSource?.TrySetResult(result);
    }

    private static string CreateNonce()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .Replace("+", "-", StringComparison.Ordinal)
            .Replace("/", "_", StringComparison.Ordinal)
            .TrimEnd('=');
    }

    private static string HashNonce(string nonce) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(nonce))).ToLowerInvariant();
}
