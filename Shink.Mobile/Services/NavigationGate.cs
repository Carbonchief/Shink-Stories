namespace Shink.Mobile.Services;

internal sealed class NavigationGate
{
    private bool _isNavigating;

    public async Task RunAsync(Func<Task> navigateAsync)
    {
        if (_isNavigating)
        {
            return;
        }

        _isNavigating = true;
        try
        {
            await navigateAsync();
        }
        finally
        {
            _isNavigating = false;
        }
    }
}
