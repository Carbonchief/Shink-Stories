namespace Shink.Mobile.Pages;

internal static class MobileMenuSheet
{
    public static async Task<string?> ShowAsync(Page hostPage, string title, params string[] actions)
    {
        if (actions.Length == 0)
        {
            return null;
        }

        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sheetPage = new ContentPage
        {
            BackgroundColor = Color.FromRgba(4, 47, 50, 0.42),
            SafeAreaEdges = SafeAreaEdges.None
        };

        var isCompleting = false;
        async Task CompleteAsync(string? choice)
        {
            if (isCompleting)
            {
                return;
            }

            isCompleting = true;
            try
            {
                await sheetPage.Navigation.PopModalAsync(animated: true);
            }
            catch
            {
                // The menu may already be gone if the host page navigated away.
            }

            completion.TrySetResult(choice);
        }

        var stack = new VerticalStackLayout
        {
            Spacing = 11
        };

        foreach (var action in actions)
        {
            stack.Children.Add(BuildActionButton(action, () => CompleteAsync(action)));
        }

        var cancelButton = BuildActionButton("Kanselleer", () => CompleteAsync(null), isCancel: true);
        var card = new Border
        {
            BackgroundColor = Color.FromArgb("#FFF7E8"),
            Stroke = Color.FromArgb("#F8E9C9"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 30 },
            Padding = new Thickness(24, 22, 24, 24),
            Margin = new Thickness(20, 0, 20, 22),
            Shadow = new Shadow
            {
                Brush = Brush.Black,
                Offset = new Point(0, 12),
                Radius = 26,
                Opacity = 0.2f
            },
            Content = new VerticalStackLayout
            {
                Spacing = 14,
                Children =
                {
                    new Image
                    {
                        Source = "oortjies_01.png",
                        HeightRequest = 74,
                        Aspect = Aspect.AspectFit,
                        HorizontalOptions = LayoutOptions.Center,
                        Margin = new Thickness(0, -4, 0, -2)
                    },
                    new Label
                    {
                        Text = title,
                        FontSize = 26,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = Color.FromArgb("#27313A"),
                        HorizontalTextAlignment = TextAlignment.Center
                    },
                    stack,
                    cancelButton
                }
            }
        };

        var dismissLayer = new BoxView
        {
            Color = Colors.Transparent
        };

        sheetPage.Content = new Grid
        {
            Padding = new Thickness(0),
            Children =
            {
                dismissLayer,
                new Grid
                {
                    VerticalOptions = LayoutOptions.End,
                    Children =
                    {
                        card
                    }
                }
            }
        };

        var dismissTap = new TapGestureRecognizer();
        dismissTap.Tapped += async (_, _) => await CompleteAsync(null);
        dismissLayer.GestureRecognizers.Add(dismissTap);

        await hostPage.Navigation.PushModalAsync(sheetPage, animated: true);
        return await completion.Task;
    }

    private static Border BuildActionButton(string text, Func<Task> onTap, bool isCancel = false)
    {
        var button = new Border
        {
            BackgroundColor = isCancel ? Color.FromArgb("#F4E9D1") : Color.FromArgb("#383A48"),
            Stroke = isCancel ? Color.FromArgb("#E8DEC8") : Color.FromArgb("#30323F"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 18 },
            HeightRequest = 58,
            Padding = new Thickness(18, 0),
            Content = new Label
            {
                Text = text,
                FontSize = 20,
                FontAttributes = FontAttributes.Bold,
                TextColor = isCancel ? Color.FromArgb("#27313A") : Colors.White,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center,
                InputTransparent = true
            }
        };
        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) => await onTap();
        button.GestureRecognizers.Add(tap);
        return button;
    }
}
