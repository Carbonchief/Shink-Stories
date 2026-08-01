using AuthenticationServices;
using Microsoft.Maui.Handlers;
using Shink.Mobile.Pages;

namespace Shink.Mobile.Platforms.iOS;

public sealed class AppleSignInButtonHandler : ViewHandler<AppleSignInButton, ASAuthorizationAppleIdButton>
{
    public static readonly IPropertyMapper<AppleSignInButton, AppleSignInButtonHandler> Mapper =
        new PropertyMapper<AppleSignInButton, AppleSignInButtonHandler>(ViewHandler.ViewMapper);

    public AppleSignInButtonHandler()
        : base(Mapper)
    {
    }

    protected override ASAuthorizationAppleIdButton CreatePlatformView() =>
        new(ASAuthorizationAppleIdButtonType.SignIn, ASAuthorizationAppleIdButtonStyle.Black);

    protected override void ConnectHandler(ASAuthorizationAppleIdButton platformView)
    {
        base.ConnectHandler(platformView);
        platformView.TouchUpInside += OnTouchUpInside;
    }

    protected override void DisconnectHandler(ASAuthorizationAppleIdButton platformView)
    {
        platformView.TouchUpInside -= OnTouchUpInside;
        base.DisconnectHandler(platformView);
    }

    private void OnTouchUpInside(object? sender, EventArgs args) => VirtualView?.RaisePressed();
}
