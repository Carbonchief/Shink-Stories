namespace Shink.Mobile.Pages;

internal sealed class FullScreenSplashPage : ContentPage
{
    public FullScreenSplashPage()
    {
        SafeAreaEdges = SafeAreaEdges.None;
        BackgroundColor = Color.FromArgb("#13AFC1");

        Content = new Grid
        {
            SafeAreaEdges = SafeAreaEdges.None,
            Children =
            {
                new Image
                {
                    Source = "schink_stories_full_splash_runtime.png",
                    Aspect = Aspect.AspectFill,
                    HorizontalOptions = LayoutOptions.Fill,
                    VerticalOptions = LayoutOptions.Fill
                }
            }
        };
    }
}
