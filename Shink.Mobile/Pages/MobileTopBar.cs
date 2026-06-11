using Shink.Mobile.Models;
using Shink.Mobile.Services;

namespace Shink.Mobile.Pages;

internal static class MobileTopBar
{
    public static View Build(Page hostPage, MobileApiClient apiClient, MobileSession session, Thickness? margin = null)
    {
        var menuButton = BuildCircleButton("☰", 22, Colors.White, Color.FromArgb("#123F3F"));
        var menuTap = new TapGestureRecognizer();
        menuTap.Tapped += async (_, _) => await ShowMenuAsync(hostPage);
        menuButton.GestureRecognizers.Add(menuTap);

        var profileButton = BuildProfileButton(apiClient, session);
        var profileTap = new TapGestureRecognizer();
        profileTap.Tapped += async (_, _) => await OpenAccountAsync();
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
                menuButton,
                profileButton
            }
        };

        Grid.SetColumn(profileButton, 2);
        return grid;
    }

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
        var choice = await hostPage.DisplayActionSheetAsync("Menu", "Kanselleer", null, "Settings", "Manage Account");
        switch (choice)
        {
            case "Settings":
                await hostPage.DisplayAlertAsync("Settings", "Instellings kom binnekort.", "Reg so");
                break;
            case "Manage Account":
                await OpenAccountAsync();
                break;
        }
    }

    private static Task OpenAccountAsync()
    {
        if (Shell.Current?.CurrentItem is TabBar tabs)
        {
            var accountTab = tabs.Items.FirstOrDefault(item =>
                string.Equals(item.Title, "Rekening", StringComparison.OrdinalIgnoreCase));
            if (accountTab is not null)
            {
                tabs.CurrentItem = accountTab;
            }
        }

        return Task.CompletedTask;
    }

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
}
