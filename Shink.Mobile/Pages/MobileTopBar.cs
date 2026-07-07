using Shink.Mobile.Models;
using Shink.Mobile.Services;

namespace Shink.Mobile.Pages;

internal static class MobileTopBar
{
    public static View Build(
        Page hostPage,
        MobileApiClient apiClient,
        MobileSession session,
        Thickness? margin = null,
        string leftAction = "menu")
    {
        var navigationGate = new NavigationGate();
        var leftButton = string.Equals(leftAction, "back", StringComparison.OrdinalIgnoreCase)
            ? BuildBackCircleButton(Colors.White, Color.FromArgb("#123F3F"))
            : BuildMenuCircleButton(Colors.White, Color.FromArgb("#123F3F"));
        var leftTap = new TapGestureRecognizer();
        leftTap.Tapped += async (_, _) => await navigationGate.RunAsync(() => HandleLeftActionAsync(hostPage, leftAction));
        leftButton.GestureRecognizers.Add(leftTap);

        var profileButton = BuildProfileButton(apiClient, session);
        var profileTap = new TapGestureRecognizer();
        profileTap.Tapped += async (_, _) => await navigationGate.RunAsync(OpenProfileAsync);
        profileButton.GestureRecognizers.Add(profileTap);

        var grid = new Grid
        {
            Margin = margin ?? new Thickness(0),
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Auto }
            },
            Children =
            {
                leftButton,
                profileButton
            }
        };

        Grid.SetColumn(profileButton, 2);
        return grid;
    }

    private static Border BuildMenuCircleButton(Color lineColor, Color backgroundColor) =>
        new()
        {
            BackgroundColor = backgroundColor,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 23 },
            WidthRequest = 46,
            HeightRequest = 46,
            VerticalOptions = LayoutOptions.Center,
            Content = new VerticalStackLayout
            {
                Spacing = 4,
                WidthRequest = 18,
                HeightRequest = 14,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
                Children =
                {
                    BuildMenuLine(lineColor),
                    BuildMenuLine(lineColor),
                    BuildMenuLine(lineColor)
                }
            }
        };

    private static BoxView BuildMenuLine(Color color) =>
        new()
        {
            Color = color,
            WidthRequest = 18,
            HeightRequest = 2,
            HorizontalOptions = LayoutOptions.Center
        };

    private static Border BuildBackCircleButton(Color lineColor, Color backgroundColor) =>
        new()
        {
            BackgroundColor = backgroundColor,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 23 },
            WidthRequest = 46,
            HeightRequest = 46,
            VerticalOptions = LayoutOptions.Center,
            Content = new GraphicsView
            {
                Drawable = new DownCaretDrawable(lineColor),
                WidthRequest = 19,
                HeightRequest = 19,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center
            }
        };

    private static Border BuildCircleButton(string text, double fontSize, Color textColor, Color backgroundColor) =>
        new()
        {
            BackgroundColor = backgroundColor,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 23 },
            WidthRequest = 46,
            HeightRequest = 46,
            VerticalOptions = LayoutOptions.Center,
            Content = new Label
            {
                Text = text,
                FontSize = fontSize,
                FontAttributes = FontAttributes.Bold,
                TextColor = textColor,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center
            }
        };

    private static Border BuildProfileButton(MobileApiClient apiClient, MobileSession session)
    {
        var imageUrl = string.IsNullOrWhiteSpace(session.ProfileImageUrl)
            ? null
            : apiClient.BuildImageUrl(session.ProfileImageUrl);

        if (string.IsNullOrWhiteSpace(imageUrl))
        {
            return BuildCircleButton(BuildInitials(session), 15, Color.FromArgb("#0B3534"), Color.FromArgb("#FFD45A"));
        }

        return new Border
        {
            BackgroundColor = Color.FromArgb("#F7EAD0"),
            Stroke = Colors.White,
            StrokeThickness = 2,
            StrokeShape = new RoundRectangle { CornerRadius = 23 },
            WidthRequest = 46,
            HeightRequest = 46,
            Padding = 0,
            VerticalOptions = LayoutOptions.Center,
            Content = new Image
            {
                Source = ImageSource.FromUri(new Uri(imageUrl, UriKind.Absolute)),
                Aspect = Aspect.AspectFill,
                WidthRequest = 46,
                HeightRequest = 46
            }
        };
    }

    private static async Task ShowMenuAsync(Page hostPage)
    {
        var choice = await MobileMenuSheet.ShowAsync(hostPage, "Menu", "Instellings", "Bestuur rekening");
        switch (choice)
        {
            case "Instellings":
                await hostPage.DisplayAlertAsync("Instellings", "Instellings kom binnekort.", "Reg so");
                break;
            case "Bestuur rekening":
                await OpenAccountAsync();
                break;
        }
    }

    private static Task HandleLeftActionAsync(Page hostPage, string leftAction) =>
        string.Equals(leftAction, "back", StringComparison.OrdinalIgnoreCase)
            ? OpenLuisterAsync()
            : ShowMenuAsync(hostPage);

    private static async Task OpenLuisterAsync()
    {
        var navigation = Shell.Current.Navigation;
        if (navigation.ModalStack.Count > 0)
        {
            await navigation.PopModalAsync();
            return;
        }

        if (navigation.NavigationStack.Count > 1)
        {
            await navigation.PopAsync();
            return;
        }

        await Shell.Current.GoToAsync("//Luister", animate: true);
    }

    private static Task OpenAccountAsync() =>
        Shell.Current.GoToAsync(nameof(AccountPage), animate: true);

    private static Task OpenProfileAsync() =>
        Shell.Current.GoToAsync(nameof(ProfilePage), animate: true);

    private static string BuildInitials(MobileSession session)
    {
        var source = !string.IsNullOrWhiteSpace(session.DisplayName)
            ? session.DisplayName
            : session.Email;

        if (string.IsNullOrWhiteSpace(source))
        {
            return "S";
        }

        var localName = source.Contains('@', StringComparison.Ordinal)
            ? source[..source.IndexOf('@')]
            : source;
        var tokens = localName
            .Split([' ', '.', '_', '-'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Take(2)
            .ToArray();

        if (tokens.Length >= 2)
        {
            return $"{char.ToUpperInvariant(tokens[0][0])}{char.ToUpperInvariant(tokens[1][0])}";
        }

        if (tokens.Length == 1)
        {
            var token = tokens[0];
            return token.Length >= 2
                ? $"{char.ToUpperInvariant(token[0])}{char.ToUpperInvariant(token[1])}"
                : char.ToUpperInvariant(token[0]).ToString();
        }

        return "S";
    }

    private sealed class DownCaretDrawable(Color strokeColor) : IDrawable
    {
        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            canvas.StrokeColor = strokeColor;
            canvas.StrokeSize = 3.2f;
            canvas.StrokeLineCap = LineCap.Round;
            canvas.StrokeLineJoin = LineJoin.Round;

            var centerX = dirtyRect.Center.X;
            var centerY = dirtyRect.Center.Y + 1;
            var halfWidth = MathF.Min(dirtyRect.Width, dirtyRect.Height) * 0.28f;
            var halfHeight = MathF.Min(dirtyRect.Width, dirtyRect.Height) * 0.18f;

            var path = new PathF();
            path.MoveTo(centerX - halfWidth, centerY - halfHeight);
            path.LineTo(centerX, centerY + halfHeight);
            path.LineTo(centerX + halfWidth, centerY - halfHeight);
            canvas.DrawPath(path);
        }
    }
}
