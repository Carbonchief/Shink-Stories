using Shink.Mobile.Services;

namespace Shink.Mobile.Pages;

internal static class MobileMenuSheet
{
    private const double TabletMenuMaximumWidth = 720;
    private const double DrawerMaximumWidth = 420;
    private const uint DrawerOpenDurationMilliseconds = 280;
    private const uint DrawerCloseDurationMilliseconds = 220;
    private const string CloseIconGlyph = "\uf00d";

    public static async Task<string?> ShowFromRightAsync(Page hostPage, string title, params string[] actions)
    {
        if (actions.Length == 0)
        {
            return null;
        }

        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sheetPage = new ContentPage
        {
            BackgroundColor = Colors.Transparent,
            SafeAreaEdges = SafeAreaEdges.None
        };

        RightDrawerVisuals? drawer = null;
        var isCompleting = false;
        async Task CompleteAsync(string? choice)
        {
            if (isCompleting)
            {
                return;
            }

            isCompleting = true;
            if (drawer is not null)
            {
                await AnimateDrawerClosedAsync(drawer);
            }

            try
            {
                await sheetPage.Navigation.PopModalAsync(animated: false);
            }
            catch
            {
                // The menu may already be gone if the host page navigated away.
            }

            completion.TrySetResult(choice);
        }

        drawer = BuildRightDrawer(title, CompleteAsync, actions);
        sheetPage.Content = drawer.Overlay;
        sheetPage.Disappearing += (_, _) =>
        {
            if (!isCompleting)
            {
                completion.TrySetResult(null);
            }
        };

        await hostPage.Navigation.PushModalAsync(sheetPage, animated: false);
        await AnimateDrawerOpenAsync(drawer);
        return await completion.Task;
    }

    public static async Task<string?> ShowAsync(Page hostPage, string title, params string[] actions)
    {
        if (actions.Length == 0)
        {
            return null;
        }

        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sheetPage = new ContentPage
        {
            BackgroundColor = Colors.Transparent,
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
                await sheetPage.Navigation.PopModalAsync(animated: false);
            }
            catch
            {
                // The menu may already be gone if the host page navigated away.
            }

            completion.TrySetResult(choice);
        }

        sheetPage.Content = BuildOverlay(title, CompleteAsync, actions);

        await hostPage.Navigation.PushModalAsync(sheetPage, animated: false);
        return await completion.Task;
    }

    public static Grid BuildOverlay(
        string title,
        Func<string?, Task> onSelection,
        params string[] actions)
    {
        var actionGrid = new Grid
        {
            RowSpacing = 11,
            ColumnSpacing = 12
        };

        void ArrangeActions(bool useTabletLayout)
        {
            actionGrid.Children.Clear();
            actionGrid.RowDefinitions.Clear();
            actionGrid.ColumnDefinitions.Clear();

            var columnCount = useTabletLayout ? 2 : 1;
            for (var column = 0; column < columnCount; column++)
            {
                actionGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            }

            for (var index = 0; index < actions.Length; index++)
            {
                var action = actions[index];
                var row = index / columnCount;
                while (actionGrid.RowDefinitions.Count <= row)
                {
                    actionGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                }

                var button = BuildActionButton(action, () => onSelection(action));
                actionGrid.Children.Add(button);
                Grid.SetRow(button, row);
                Grid.SetColumn(button, index % columnCount);
            }
        }

        var closeButton = BuildCloseButton(() => onSelection(null));
        var heading = new Grid
        {
            Children =
            {
                new VerticalStackLayout
                {
                    Spacing = 8,
                    Children =
                    {
                        new Image
                        {
                            Source = "oortjies_01.png",
                            HeightRequest = 70,
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
                        }
                    }
                },
                closeButton
            }
        };

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
            Content = new ScrollView
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Never,
                Content = new VerticalStackLayout
                {
                    Spacing = 14,
                    Children =
                    {
                        heading,
                        actionGrid
                    }
                }
            }
        };

        var dismissLayer = new BoxView
        {
            Color = Colors.Transparent
        };

        var cardHost = new Grid
        {
            VerticalOptions = LayoutOptions.End,
            Children =
            {
                card
            }
        };

        var overlay = new Grid
        {
            BackgroundColor = Color.FromRgba(4, 47, 50, 0.42),
            Padding = new Thickness(0),
            Children =
            {
                dismissLayer,
                cardHost
            }
        };

        var usingTabletLayout = false;
        void ApplyResponsiveLayout(double width)
        {
            var resolvedWidth = MobileResponsiveLayout.ResolveWidth(width);
            var useTabletLayout = DeviceInfo.Current.Idiom == DeviceIdiom.Tablet ||
                                  MobileResponsiveLayout.IsWide(resolvedWidth);

            if (useTabletLayout != usingTabletLayout || actionGrid.Children.Count == 0)
            {
                usingTabletLayout = useTabletLayout;
                ArrangeActions(useTabletLayout);
            }

            if (useTabletLayout)
            {
                card.WidthRequest = Math.Min(TabletMenuMaximumWidth, Math.Max(320, resolvedWidth - 64));
                card.MaximumWidthRequest = TabletMenuMaximumWidth;
                card.HorizontalOptions = LayoutOptions.Center;
                card.Margin = new Thickness(0);
                cardHost.VerticalOptions = LayoutOptions.Center;
                return;
            }

            var phoneCardWidth = Math.Max(280, resolvedWidth - 40);
            card.WidthRequest = phoneCardWidth;
            card.MaximumWidthRequest = phoneCardWidth;
            card.HorizontalOptions = LayoutOptions.Center;
            card.Margin = new Thickness(20, 0, 20, 22);
            cardHost.VerticalOptions = LayoutOptions.End;
        }

        ApplyResponsiveLayout(DeviceDisplay.MainDisplayInfo.Width / DeviceDisplay.MainDisplayInfo.Density);
        overlay.SizeChanged += (_, _) => ApplyResponsiveLayout(overlay.Width);

        var dismissTap = new TapGestureRecognizer();
        dismissTap.Tapped += async (_, _) => await onSelection(null);
        dismissLayer.GestureRecognizers.Add(dismissTap);
        return overlay;
    }

    private static RightDrawerVisuals BuildRightDrawer(
        string title,
        Func<string?, Task> onSelection,
        IReadOnlyList<string> actions)
    {
        var actionGrid = new Grid
        {
            RowSpacing = 11
        };
        actionGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        for (var index = 0; index < actions.Count; index++)
        {
            actionGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var action = actions[index];
            var button = BuildActionButton(action, () => onSelection(action));
            actionGrid.Children.Add(button);
            Grid.SetRow(button, index);
        }

        var closeButton = BuildCloseButton(() => onSelection(null));
        var heading = new Grid
        {
            Children =
            {
                new VerticalStackLayout
                {
                    Spacing = 8,
                    Children =
                    {
                        new Image
                        {
                            Source = "oortjies_01.png",
                            HeightRequest = 70,
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
                        }
                    }
                },
                closeButton
            }
        };

        var panel = new Border
        {
            AutomationId = "mobile-menu-drawer",
            BackgroundColor = Color.FromArgb("#FFF7E8"),
            Stroke = Color.FromArgb("#F8E9C9"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 0 },
            Padding = new Thickness(24, 22, 24, 24),
            Margin = Thickness.Zero,
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Fill,
            Shadow = new Shadow
            {
                Brush = Brush.Black,
                Offset = new Point(-10, 0),
                Radius = 26,
                Opacity = 0.22f
            },
            Content = new ScrollView
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Never,
                Content = new VerticalStackLayout
                {
                    Spacing = 14,
                    VerticalOptions = LayoutOptions.Center,
                    Children =
                    {
                        heading,
                        actionGrid
                    }
                }
            }
        };
        SemanticProperties.SetDescription(panel, "Menu");

        var scrim = new BoxView
        {
            Color = Color.FromRgba(4, 47, 50, 0.42),
            Opacity = 0
        };
        var dismissTap = new TapGestureRecognizer();
        dismissTap.Tapped += async (_, _) => await onSelection(null);
        scrim.GestureRecognizers.Add(dismissTap);

        var overlay = new Grid
        {
            BackgroundColor = Colors.Transparent,
            Children =
            {
                scrim,
                panel
            }
        };

        var visuals = new RightDrawerVisuals(overlay, scrim, panel);
        void ApplyResponsiveWidth(double width)
        {
            var resolvedWidth = MobileResponsiveLayout.ResolveWidth(width);
            panel.WidthRequest = Math.Min(DrawerMaximumWidth, Math.Max(280, resolvedWidth - 36));
            if (panel.TranslationX > 0.5)
            {
                panel.TranslationX = visuals.ClosedTranslation;
            }
        }

        ApplyResponsiveWidth(DeviceDisplay.MainDisplayInfo.Width / DeviceDisplay.MainDisplayInfo.Density);
        panel.TranslationX = visuals.ClosedTranslation;
        overlay.SizeChanged += (_, _) => ApplyResponsiveWidth(overlay.Width);
        return visuals;
    }

    private static async Task AnimateDrawerOpenAsync(RightDrawerVisuals drawer)
    {
        drawer.Panel.CancelAnimations();
        drawer.Scrim.CancelAnimations();
        drawer.Scrim.Opacity = 0;
        drawer.Panel.TranslationX = drawer.ClosedTranslation;
        await Task.Yield();
        await Task.WhenAll(
            drawer.Scrim.FadeToAsync(1, 190, Easing.CubicOut),
            drawer.Panel.TranslateToAsync(0, 0, DrawerOpenDurationMilliseconds, Easing.CubicOut));
    }

    private static async Task AnimateDrawerClosedAsync(RightDrawerVisuals drawer)
    {
        drawer.Panel.CancelAnimations();
        drawer.Scrim.CancelAnimations();
        await Task.WhenAll(
            drawer.Scrim.FadeToAsync(0, 170, Easing.CubicIn),
            drawer.Panel.TranslateToAsync(
                drawer.ClosedTranslation,
                0,
                DrawerCloseDurationMilliseconds,
                Easing.CubicIn));
    }

    private static Border BuildCloseButton(Func<Task> onTap)
    {
        var button = new Border
        {
            AutomationId = "mobile-menu-close",
            BackgroundColor = Color.FromArgb("#F4E9D1"),
            Stroke = Color.FromArgb("#E8DEC8"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 21 },
            WidthRequest = 42,
            HeightRequest = 42,
            Padding = 0,
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Start,
            Content = new Label
            {
                Text = CloseIconGlyph,
                FontFamily = "FontAwesomeSolid",
                FontSize = 18,
                TextColor = Color.FromArgb("#27313A"),
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center,
                InputTransparent = true
            }
        };
        SemanticProperties.SetDescription(button, "Maak menu toe");
        AttachTapHandler(button, onTap);
        return button;
    }

    private static Border BuildActionButton(string text, Func<Task> onTap)
    {
        var button = new Border
        {
            BackgroundColor = Color.FromArgb("#383A48"),
            Stroke = Color.FromArgb("#30323F"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 18 },
            HeightRequest = 58,
            Padding = new Thickness(18, 0),
            Content = new Label
            {
                Text = text,
                FontSize = 20,
                FontAttributes = FontAttributes.Bold,
                TextColor = Colors.White,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center,
                InputTransparent = true
            }
        };
        AttachTapHandler(button, onTap);
        return button;
    }

    private static void AttachTapHandler(Border button, Func<Task> onTap)
    {
        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) =>
        {
            button.IsEnabled = false;
            button.Opacity = 0.72;
            SafeHapticFeedback.TryPerform(HapticFeedbackType.Click);
            try
            {
                await onTap();
            }
            finally
            {
                button.IsEnabled = true;
                button.Opacity = 1;
            }
        };
        button.GestureRecognizers.Add(tap);
    }

    private sealed record RightDrawerVisuals(Grid Overlay, BoxView Scrim, Border Panel)
    {
        public double ClosedTranslation => Panel.WidthRequest + 24;
    }
}
