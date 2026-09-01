using Shink.Mobile.Models;
using Shink.Mobile.Services;

namespace Shink.Mobile.Pages;

internal static class MobileTopBar
{
    internal const double StoriesBackdropHeight = 92;
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
            brandLeadingInset: 4,
            applyMaterial: false);

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
        double brandLeadingInset = 16,
        bool applyMaterial = true)
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
            notificationTap.Tapped += (_, _) =>
            {
                _ = RunNotificationActionSafelyAsync(navigationGate, notificationAction);
            };
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
            Background = applyMaterial
                ? BuildMaterialBackdropBrush(backgroundColor)
                : Brush.Transparent,
            Stroke = Colors.Transparent,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = isBackAction ? 26 : 0 },
            Padding = new Thickness(isBackAction ? 16 : brandLeadingInset, 6, 12, 6),
            Content = grid
        };

        // Keep colourful page artwork visible beneath the shared native blur.
        // Both platforms use the same tint, transparency, and page-facing fade.
        if (applyMaterial)
        {
            MobileLiquidGlass.ApplyTopBar(
                bar,
                MobileAndroidChromePalette.TopBarNativeBlurTint);
        }

        return bar;
    }

    public static void ApplyStoriesBackdrop(View overlay, View? captureExclusion = null)
    {
        overlay.Background = BuildMaterialBackdropBrush(
            MobileAndroidChromePalette.TopBarBackground);
        MobileLiquidGlass.ApplyTopBar(
            overlay,
            MobileAndroidChromePalette.TopBarNativeBlurTint,
            captureExclusion);
    }

    public static View BuildStoriesBackdropLayer(View safeAreaOverlay)
    {
        if (DeviceInfo.Current.Platform != DevicePlatform.Android)
        {
            ApplyStoriesBackdrop(safeAreaOverlay);
            return new ContentView
            {
                IsVisible = false,
                InputTransparent = true
            };
        }

        // Android positions a Container-safe overlay below the status area.
        // Keep the controls in that safe overlay, but put the material in a
        // separate edge-to-edge layer so the status area and toolbar read as
        // one continuous glass surface, matching iOS.
        var backdropLayer = new Grid
        {
            SafeAreaEdges = SafeAreaEdges.None,
            BackgroundColor = Colors.Transparent,
            HeightRequest = StoriesBackdropHeight,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Start,
            InputTransparent = true,
            ZIndex = 99
        };
        ApplyStoriesBackdrop(backdropLayer, safeAreaOverlay);
        return backdropLayer;
    }

    private static Brush BuildMaterialBackdropBrush(Color? requestedColor)
    {
        var surfaceStart = requestedColor is not null && requestedColor.Alpha > 0
            ? requestedColor.WithAlpha(0.04f)
            : MobileAndroidChromePalette.TopBarSurfaceStartTint;
        var surfaceEnd = requestedColor is not null && requestedColor.Alpha > 0
            ? requestedColor.WithAlpha(0.08f)
            : MobileAndroidChromePalette.TopBarSurfaceEndTint;

        return new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1),
            GradientStops =
            {
                new GradientStop(surfaceStart, 0),
                new GradientStop(surfaceEnd, 0.58f),
                new GradientStop(surfaceEnd.WithAlpha(surfaceEnd.Alpha * 0.72f), 0.78f),
                new GradientStop(surfaceEnd.WithAlpha(surfaceEnd.Alpha * 0.28f), 0.92f),
                new GradientStop(Colors.Transparent, 1)
            }
        };
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
            Content = new ProgressiveCachedImage(
                apiClient,
                new ProgressiveImageRequest(imageUrl))
            {
                Aspect = Aspect.AspectFill,
                WidthRequest = 48,
                HeightRequest = 48
            }
        };
    }

    private static async Task ShowMenuAsync(Page hostPage)
    {
        var choice = await MobileMenuSheet.ShowFromRightAsync(
            hostPage,
            "Menu",
            "Karakters",
            "Karakter-pare",
            "Karakter Raai",
            "Afgelaai",
            "Instellings");

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

    private static async Task RunNotificationActionSafelyAsync(
        NavigationGate navigationGate,
        Func<Task> action)
    {
        try
        {
            await navigationGate.RunAsync(action);
        }
        catch
        {
            // A Shell transition can be superseded by another tap or a page
            // lifecycle event. Luister owns the retryable surface request.
        }
    }

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
