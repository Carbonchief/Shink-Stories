namespace Shink.Mobile.Pages;

public sealed class AppleSignInButton : View
{
    public event EventHandler? Pressed;

    internal void RaisePressed() => Pressed?.Invoke(this, EventArgs.Empty);
}
