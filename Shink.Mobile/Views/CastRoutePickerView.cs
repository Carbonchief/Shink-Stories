namespace Shink.Mobile.Views;

public sealed class CastRoutePickerView : ContentView
{
    internal event EventHandler? PickerOpenRequested;

    public CastRoutePickerView()
    {
        Content = new Border
        {
            BackgroundColor = Color.FromArgb("#1B302D"),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 24 },
            Padding = new Thickness(18, 0),
            Content = new Label
            {
                Text = "Soek toestelle",
                FontSize = 16,
                FontAttributes = FontAttributes.Bold,
                TextColor = Color.FromArgb("#F7FBF7"),
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center
            }
        };
    }

    public void OpenPicker() => PickerOpenRequested?.Invoke(this, EventArgs.Empty);
}
