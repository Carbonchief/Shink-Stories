using Shink.Mobile.Models;
using Shink.Mobile.Services;

namespace Shink.Mobile.Pages;

internal static class MobileTopBar
{
    // Matches the website header's Font Awesome `fa-solid fa-bell` exactly.
    private const string NotificationBellGlyph = "\uf0f3";
    private const string NotificationBellAppleFontFamily = "Font Awesome 6 Free Solid";
    private const string NotificationBellAndroidFontFamily = "FontAwesomeSolid";

    public static View BuildStoriesTopBar(
        Page hostPage,
        MobileApiClient apiClient,
        MobileSession session,
        Func<Task>? notificationAction = null,
        int notificationCount = 0) =>
        Build(
            hostPage,
            apiClient,
            session,
            title: "Schink Stories",
            notificationAction: notificationAction,
            notificationCount: notificationCount,
            backgroundColor: MobileAndroidChromePalette.TopBarBackground,
            showProfile: false,
            brandLeadingInset: 4);

    public static View Build(
        Page hostPage,
        MobileApiClient apiClient,
        MobileSession session,
        Thickness? margin = null,
        string leftAction = "menu",
        string? title = null,
        Func<Task>? searchAction = null,
        Func<Task>? notificationAction = null,
        int notificationCount = 0,
        Color? backgroundColor = null,
        bool showProfile = true,
        double brandLeadingInset = 16)
    {
        var navigationGate = new NavigationGate();
        var isBackAction = string.Equals(leftAction, "back", StringComparison.OrdinalIgnoreCase);
        var navigationButton = BuildChromeIconButton(
            isBackAction ? MobileAndroidIcon.Back : MobileAndroidIcon.Menu,
            MobileAndroidChromePalette.TopBarIcon,
            44,
            29);
        var navigationTap = new TapGestureRecognizer();
        navigationTap.Tapped += async (_, _) => await navigationGate.RunAsync(() => HandleLeftActionAsync(hostPage, leftAction));
        navigationButton.GestureRecognizers.Add(navigationTap);

        var titleLabel = new Label
        {
            Text = string.IsNullOrWhiteSpace(title) ? hostPage.Title : title,
            FontSize = 18,
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.White,
            HorizontalTextAlignment = TextAlignment.Start,
            VerticalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.TailTruncation,
            Margin = new Thickness(8, 0, 12, 0),
            InputTransparent = true
        };

        var rightActions = new HorizontalStackLayout
        {
            Spacing = 0,
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Center
        };

        if (searchAction is not null)
        {
            var searchButton = BuildChromeIconButton(MobileAndroidIcon.Search, MobileAndroidChromePalette.TopBarIcon, 42, 28);
            var searchTap = new TapGestureRecognizer();
            searchTap.Tapped += async (_, _) => await navigationGate.RunAsync(searchAction);
            searchButton.GestureRecognizers.Add(searchTap);
            rightActions.Children.Add(searchButton);
        }

        if (notificationAction is not null)
        {
            var notificationButton = BuildNotificationButton(notificationCount);
            var notificationTap = new TapGestureRecognizer();
            notificationTap.Tapped += async (_, _) => await navigationGate.RunAsync(notificationAction);
            notificationButton.GestureRecognizers.Add(notificationTap);
            rightActions.Children.Add(notificationButton);
        }

        if (showProfile)
        {
            var profileButton = BuildProfileButton(apiClient, session);
            var profileTap = new TapGestureRecognizer();
            profileTap.Tapped += async (_, _) => await navigationGate.RunAsync(OpenProfileAsync);
            profileButton.GestureRecognizers.Add(profileTap);
            rightActions.Children.Add(profileButton);

            var profileCaret = BuildChromeIconButton(MobileAndroidIcon.CaretDown, MobileAndroidChromePalette.TopBarIcon, 28, 20);
            var caretTap = new TapGestureRecognizer();
            caretTap.Tapped += async (_, _) => await navigationGate.RunAsync(OpenProfileAsync);
            profileCaret.GestureRecognizers.Add(caretTap);
            rightActions.Children.Add(profileCaret);
        }

        if (!isBackAction)
        {
            rightActions.Children.Add(navigationButton);
        }

        var grid = new Grid
        {
            HeightRequest = 62,
            ColumnSpacing = 0,
            Margin = margin ?? new Thickness(0),
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Auto }
            },
            Children =
            {
                isBackAction ? navigationButton : BuildBrandMark(),
                isBackAction ? titleLabel : new ContentView { InputTransparent = true },
                rightActions
            }
        };

        Grid.SetColumn(titleLabel, 1);
        Grid.SetColumn(rightActions, 2);
        var bar = new Border
        {
            BackgroundColor = backgroundColor ?? Colors.Transparent,
            Stroke = Colors.Transparent,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = isBackAction ? 26 : 0 },
            Padding = new Thickness(isBackAction ? 16 : brandLeadingInset, 6, 12, 6),
            Content = grid
        };

        return bar;
    }

    private static Grid BuildNotificationButton(int unreadCount)
    {
        var container = new Grid
        {
            WidthRequest = 42,
            HeightRequest = 42,
            VerticalOptions = LayoutOptions.Center,
            AutomationId = "mobile-top-notifications"
        };
        SemanticProperties.SetDescription(container, "Kennisgewings");
        container.Children.Add(new Label
        {
            Text = NotificationBellGlyph,
            FontFamily = DeviceInfo.Current.Platform == DevicePlatform.Android
                ? NotificationBellAndroidFontFamily
                : NotificationBellAppleFontFamily,
            FontAttributes = FontAttributes.None,
            FontSize = 28,
            TextColor = MobileAndroidChromePalette.TopBarIcon,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center,
            InputTransparent = true
        });

        if (unreadCount > 0)
        {
            container.Children.Add(new Border
            {
                BackgroundColor = Color.FromArgb("#E11D48"),
                Stroke = Colors.White,
                StrokeThickness = 1,
                StrokeShape = new RoundRectangle { CornerRadius = 999 },
                WidthRequest = unreadCount > 9 ? 27 : 20,
                HeightRequest = 20,
                Padding = 0,
                HorizontalOptions = LayoutOptions.End,
                VerticalOptions = LayoutOptions.Start,
                InputTransparent = true,
                Content = new Label
                {
                    Text = unreadCount > 99 ? "99+" : unreadCount.ToString(),
                    FontSize = 10,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Colors.White,
                    HorizontalTextAlignment = TextAlignment.Center,
                    VerticalTextAlignment = TextAlignment.Center
                }
            });
        }

        return container;
    }

    private static Border BuildChromeIconButton(MobileAndroidIcon icon, Color color, double touchTarget, double iconSize) =>
        new()
        {
            BackgroundColor = Colors.Transparent,
            StrokeThickness = 0,
            WidthRequest = touchTarget,
            HeightRequest = touchTarget,
            Padding = 0,
            VerticalOptions = LayoutOptions.Center,
            Content = new GraphicsView
            {
                Drawable = new MobileAndroidIconDrawable(icon, color),
                WidthRequest = iconSize,
                HeightRequest = iconSize,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
                InputTransparent = true
            }
        };

    private static ImageSource CreatePackageImageSource(string fileName) =>
        ImageSource.FromStream(_ => FileSystem.OpenAppPackageFileAsync(fileName));

    private static Image BuildBrandMark() =>
        new()
        {
            // Load the unprocessed package asset. The Android splash screen uses the
            // same source artwork and generates an opaque, teal-backed density asset
            // with the same filename; resolving the drawable by name can therefore
            // show the splash version here instead of the transparent logo.
            Source = CreatePackageImageSource("schink_stories_logo_white_raw.png"),
            BackgroundColor = Colors.Transparent,
            WidthRequest = 124,
            HeightRequest = 42,
            Aspect = Aspect.AspectFit,
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Center,
            InputTransparent = true
        };

    private static Border BuildProfileButton(MobileApiClient apiClient, MobileSession session)
    {
        var imageUrl = string.IsNullOrWhiteSpace(session.ProfileImageUrl)
            ? null
            : apiClient.BuildImageUrl(session.ProfileImageUrl);

        if (string.IsNullOrWhiteSpace(imageUrl))
        {
            return new Border
            {
                BackgroundColor = MobileAndroidChromePalette.ProfileBackground,
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = 14 },
                WidthRequest = 48,
                HeightRequest = 48,
                Padding = 0,
                VerticalOptions = LayoutOptions.Center,
                Shadow = new Shadow
                {
                    Brush = Brush.Black,
                    Offset = new Point(0, 4),
                    Radius = 10,
                    Opacity = 0.22f
                },
                Content = new Label
                {
                    Text = BuildInitials(session),
                    FontSize = 17,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Colors.White,
                    HorizontalTextAlignment = TextAlignment.Center,
                    VerticalTextAlignment = TextAlignment.Center,
                    InputTransparent = true
                }
            };
        }

        return new Border
        {
            BackgroundColor = MobileAndroidChromePalette.ProfileBackground,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 14 },
            WidthRequest = 48,
            HeightRequest = 48,
            Padding = 0,
            VerticalOptions = LayoutOptions.Center,
            Content = new Image
            {
                Source = ImageSource.FromUri(new Uri(imageUrl, UriKind.Absolute)),
                Aspect = Aspect.AspectFill,
                WidthRequest = 48,
                HeightRequest = 48
            }
        };
    }

    private static async Task ShowMenuAsync(Page hostPage)
    {
        var choice = await MobileMenuSheet.ShowAsync(
            hostPage,
            "Menu",
            "Karakters",
            "Karakter-pare",
            "Karakter Raai",
            "Afgelaai",
            "Instellings",
            "Bestuur rekening");

        try
        {
            switch (choice)
            {
                case "Karakters":
                    await Shell.Current.GoToAsync("//Karakters", animate: false);
                    break;
                case "Karakter-pare":
                    await Shell.Current.GoToAsync(nameof(KarakterPareConfigPage), animate: true);
                    break;
                case "Karakter Raai":
                    await Shell.Current.GoToAsync(nameof(KarakterRaaiConfigPage), animate: true);
                    break;
                case "Afgelaai":
                    await Shell.Current.GoToAsync(nameof(DownloadedPage), animate: true);
                    break;
                case "Instellings":
                    await Shell.Current.GoToAsync(nameof(SettingsPage), animate: true);
                    break;
                case "Bestuur rekening":
                    await OpenAccountAsync();
                    break;
            }
        }
        catch (Exception)
        {
            await hostPage.DisplayAlertAsync(
                "Kon nie oopmaak nie",
                "Dié blad kon nie nou oopmaak nie. Probeer asseblief weer.",
                "Reg so");
        }
    }

    private static Task HandleLeftActionAsync(Page hostPage, string leftAction) =>
        string.Equals(leftAction, "back", StringComparison.OrdinalIgnoreCase)
            ? OpenLuisterAsync()
            : ShowMenuAsync(hostPage);

    private static Task OpenLuisterAsync() =>
        Shell.Current.GoToAsync("//Luister", animate: false);

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

}
