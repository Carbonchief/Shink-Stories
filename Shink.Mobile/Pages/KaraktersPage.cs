using Microsoft.Maui.Layouts;
using Shink.Mobile.Models;
using Shink.Mobile.Services;

namespace Shink.Mobile.Pages;

public sealed class KaraktersPage : ContentPage, IQueryAttributable
{
    private const double FloatingTopBarContentInset = 92;
    private const double BottomBarContentInset = 216;
    private const double BottomBarOverlayHeight = MobileBottomBar.NavigationHeight;
    private const string PoppinsFontFamily = "Poppins";
    private const string PoppinsSemiBoldFontFamily = "PoppinsSemiBold";
    private const string PoppinsBoldFontFamily = "PoppinsBold";
    private const string FontAwesomeSolidFontFamily = "FontAwesomeSolid";
    private const string FontAwesomePlayGlyph = "\uf04b";
    private const string FontAwesomeVolumeHighGlyph = "\uf028";
    private static bool IsAndroid => DeviceInfo.Current.Platform == DevicePlatform.Android;
    private static bool IsIOS => DeviceInfo.Current.Platform == DevicePlatform.iOS;
    private readonly MobileApiClient _apiClient;
    private readonly SessionState _sessionState;
    private readonly IAudioPlaybackService _audioPlaybackService;
    private readonly StoryPlaybackSession _storyPlaybackSession;
    private readonly Grid _rootLayout;
    private readonly Grid _topBarOverlay;
    private readonly Border _floatingTopBarHost;
    private readonly Grid _bottomBarOverlay;
    private readonly ContentView _bottomBarHost;
    private readonly CollectionView _charactersView;
    private readonly GridItemsLayout _charactersGridLayout;
    private readonly RefreshView _refreshView;
    private readonly Grid _profileOverlay;
    private MobileCharactersResponse? _response;
    private string? _pendingCharacterSlug;
    private bool _isPageActive;
    private bool _isResolvingPendingCharacter;
    private CancellationTokenSource? _imageWarmupCancellation;
    private CancellationTokenSource? _loadCancellation;
    private double _lastResponsiveWidth = -1;

    public KaraktersPage(
        MobileApiClient apiClient,
        SessionState sessionState,
        IAudioPlaybackService audioPlaybackService,
        StoryPlaybackSession storyPlaybackSession)
    {
        _apiClient = apiClient;
        _sessionState = sessionState;
        _audioPlaybackService = audioPlaybackService;
        _storyPlaybackSession = storyPlaybackSession;
        Title = "Karakters";
        BackgroundColor = Color.FromArgb("#46969E");
        SafeAreaEdges = SafeAreaEdges.None;
        Shell.SetNavBarIsVisible(this, false);

        _charactersView = new CollectionView
        {
            AutomationId = "characters-grid",
            Background = Brush.Transparent,
            ItemsSource = Array.Empty<MobileCharacterCard>(),
            ItemSizingStrategy = ItemSizingStrategy.MeasureFirstItem,
            SelectionMode = SelectionMode.None,
            // The character gallery is deliberately dense on phones, matching the
            // three-card layout from the product artwork rather than the two-card
            // story grid used elsewhere in the app.
            ItemsLayout = new GridItemsLayout(3, ItemsLayoutOrientation.Vertical)
            {
                HorizontalItemSpacing = 8,
                VerticalItemSpacing = 10
            },
            ItemTemplate = new DataTemplate(BuildCharacterItemView),
            Margin = new Thickness(16, 0, 16, 0),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Never,
            VerticalScrollBarVisibility = ScrollBarVisibility.Never
        };

        _refreshView = new RefreshView
        {
            SafeAreaEdges = SafeAreaEdges.None,
            Background = Brush.Transparent,
            Content = _charactersView,
            Command = new Command(async () => await LoadAsync(forceRefresh: true))
        };
        _charactersGridLayout = (GridItemsLayout)_charactersView.ItemsLayout;

        _profileOverlay = new Grid
        {
            IsVisible = false,
            InputTransparent = true,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            ZIndex = 100,
            AutomationId = "character-profile-overlay"
        };
        _topBarOverlay = new Grid
        {
            SafeAreaEdges = new SafeAreaEdges(
                SafeAreaRegions.Container,
                SafeAreaRegions.Container,
                SafeAreaRegions.Container,
                SafeAreaRegions.None),
            BackgroundColor = Colors.Transparent,
            Padding = new Thickness(0, 0, 0, 16),
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Start,
            InputTransparent = false,
            ZIndex = 100
        };
        var topBarBackdropLayer = MobileTopBar.BuildStoriesBackdropLayer(_topBarOverlay);
        _floatingTopBarHost = new Border
        {
            BackgroundColor = Colors.Transparent,
            StrokeThickness = 0,
            Padding = 0,
            Margin = Thickness.Zero,
            HeightRequest = 62,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Start,
            InputTransparent = false,
            ZIndex = 101,
            Content = MobileTopBar.BuildStoriesTopBar(
                this,
                _apiClient,
                _sessionState.Current,
                notificationAction: OpenNotificationsAsync)
        };
        _topBarOverlay.Children.Add(_floatingTopBarHost);

        _bottomBarOverlay = new Grid
        {
            SafeAreaEdges = SafeAreaEdges.None,
            BackgroundColor = Colors.Transparent,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.End,
            HeightRequest = BottomBarOverlayHeight,
            InputTransparent = false,
            ZIndex = 100
        };
        _bottomBarHost = new ContentView
        {
            Content = MobileBottomBar.Build(this, "characters", OpenStoriesSearchAsync),
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.End,
            HeightRequest = BottomBarOverlayHeight,
            ZIndex = 101
        };
        _bottomBarOverlay.Children.Add(_bottomBarHost);

        var nowPlayingBar = new PersistentNowPlayingBar(_storyPlaybackSession)
        {
            Margin = new Thickness(10, 0, 10, 124),
            ZIndex = 120
        };

        _rootLayout = new Grid
        {
            SafeAreaEdges = SafeAreaEdges.None,
            Children =
            {
                _refreshView,
                topBarBackdropLayer,
                _topBarOverlay,
                _bottomBarOverlay,
                nowPlayingBar,
                _profileOverlay
            }
        };

        Content = _rootLayout;
        RenderLoadingState();
        SizeChanged += (_, _) => ApplyResponsiveLayout();
        ApplyResponsiveLayout();
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("karakter", out var value))
        {
            var slug = Uri.UnescapeDataString(value?.ToString() ?? string.Empty).Trim();
            _pendingCharacterSlug = string.IsNullOrWhiteSpace(slug) ? null : slug;
            if (_isPageActive && _response is not null)
            {
                _ = ResolvePendingCharacterAsync();
            }
        }
    }

    internal async Task OpenCharacterFromNotificationAsync(string characterSlug)
    {
        var slug = Uri.UnescapeDataString(characterSlug ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(slug))
        {
            return;
        }

        _pendingCharacterSlug = slug;
        if (!_isPageActive)
        {
            return;
        }

        if (_response is null)
        {
            await LoadAsync();
            return;
        }

        await ResolvePendingCharacterAsync();
    }

    internal async Task PreloadCachedContentAsync(CancellationToken cancellationToken)
    {
        if (_response is not null)
        {
            return;
        }

        var cachedResponse = await _apiClient.GetCachedCharactersAsync(cancellationToken);
        if (cachedResponse is null || cancellationToken.IsCancellationRequested)
        {
            return;
        }

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            if (_response is not null || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            _response = cachedResponse;
            RenderCharacters(cachedResponse);
            StartImageWarmup(cachedResponse);
        });
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _isPageActive = true;
        if (_response is null)
        {
            await LoadAsync();
        }
        else
        {
            await ResolvePendingCharacterAsync();
        }
    }

    protected override void OnDisappearing()
    {
        _isPageActive = false;
        _loadCancellation?.Cancel();
        _imageWarmupCancellation?.Cancel();
        CloseCharacterProfile();
        base.OnDisappearing();
    }

    private async Task LoadAsync(bool forceRefresh = false)
    {
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = new CancellationTokenSource();
        var cancellationToken = _loadCancellation.Token;
        _imageWarmupCancellation?.Cancel();
        var renderedCachedData = _response is not null;
        if (!renderedCachedData)
        {
            RenderLoadingState();
        }

        if (!forceRefresh && !renderedCachedData)
        {
            var cachedResponse = await _apiClient.GetCachedCharactersAsync(cancellationToken);
            if (cachedResponse is not null && !cancellationToken.IsCancellationRequested)
            {
                _response = cachedResponse;
                RenderCharacters(cachedResponse);
                StartImageWarmup(cachedResponse);
                renderedCachedData = true;
            }
        }

        try
        {
            var response = await _apiClient.GetCharactersAsync(cancellationToken);
            if (cancellationToken.IsCancellationRequested || !_isPageActive)
            {
                return;
            }

            if (response is null)
            {
                if (!renderedCachedData)
                {
                    RenderState("Kon nie die karakterblad laai nie.");
                }
                return;
            }

            _response = response;
            RenderCharacters(response);
            StartImageWarmup(response);
            await TryOpenPendingCharacterAsync();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (!renderedCachedData && _isPageActive)
            {
                RenderState(BuildLoadErrorMessage(ex), isError: true);
            }
        }
        finally
        {
            _refreshView.IsRefreshing = false;
        }
    }

    private void RenderCharacters(MobileCharactersResponse response)
    {
        _charactersView.Header = BuildPageHeader(response);
        _charactersView.ItemsSource = response.Characters;
        _charactersView.Footer = response.Characters.Count == 0
            ? BuildState("Geen karakters is nog beskikbaar nie.")
            : new BoxView { HeightRequest = BottomBarContentInset, Color = Colors.Transparent };
        ApplyResponsiveLayout();
    }

    private void ApplyResponsiveLayout()
    {
        var width = MobileResponsiveLayout.ResolveWidth(Width);
        if (DeviceInfo.Current.Platform == DevicePlatform.Android)
        {
            var phoneChromeWidth = Math.Max(280, width - 36);
            _floatingTopBarHost.WidthRequest = phoneChromeWidth;
            _floatingTopBarHost.MaximumWidthRequest = phoneChromeWidth;
            _floatingTopBarHost.HorizontalOptions = LayoutOptions.Center;
        }
        else
        {
            MobileResponsiveLayout.ApplyStoriesTopBar(_floatingTopBarHost, width, 1040);
        }

        var span = MobileResponsiveLayout.ResolveCharacterGridSpan(width);
        var widthChanged = _lastResponsiveWidth < 0 || Math.Abs(width - _lastResponsiveWidth) > 24;
        var spanChanged = _charactersGridLayout.Span != span;
        _lastResponsiveWidth = width;

        if (!spanChanged)
        {
            if (widthChanged && _response is not null)
            {
                RefreshCharacterItemsForLayout();
            }

            return;
        }

        _charactersGridLayout.Span = span;
        if (_response is not null)
        {
            RefreshCharacterItemsForLayout();
        }
    }

    private void RefreshCharacterItemsForLayout()
    {
        if (_response is null)
        {
            return;
        }

        _charactersView.ItemsSource = Array.Empty<MobileCharacterCard>();
        _charactersView.ItemsSource = _response.Characters;
        _charactersView.Header = BuildPageHeader(_response);
    }

    private View BuildCharacterItemView() => new ReusableCharacterCardView(this);

    private View BuildPageHeader(MobileCharactersResponse response) =>
        BuildPageHeaderContent(response);

    private View BuildPageHeaderContent(MobileCharactersResponse response)
    {
        var hero = BuildHero(response);
        MobileResponsiveLayout.ApplyCenteredContent(hero, Width, 780);
        return new VerticalStackLayout
        {
            Padding = new Thickness(0, FloatingTopBarContentInset, 0, 16),
            Children =
            {
                hero
            }
        };
    }

    private void RenderLoadingState()
    {
        _charactersView.Header = new VerticalStackLayout
        {
            Padding = new Thickness(0, FloatingTopBarContentInset, 0, 16),
            Children =
            {
                new ActivityIndicator
                {
                    IsRunning = true,
                    Color = Color.FromArgb("#146D69"),
                    Margin = new Thickness(0, 40, 0, 0)
                }
            }
        };
        _charactersView.ItemsSource = Array.Empty<MobileCharacterCard>();
        _charactersView.Footer = null;
    }

    private void RenderState(string message, bool isError = false)
    {
        _charactersView.Header = new VerticalStackLayout
        {
            Padding = new Thickness(0, FloatingTopBarContentInset, 0, 16),
            Children =
            {
                BuildState(message, isError)
            }
        };
        _charactersView.ItemsSource = Array.Empty<MobileCharacterCard>();
        _charactersView.Footer = null;
    }

    private View BuildHero(MobileCharactersResponse response)
    {
        var summary = new VerticalStackLayout
        {
            Spacing = 0,
            HorizontalOptions = LayoutOptions.Center,
            Children =
            {
                new Label
                {
                    FormattedText = response.IsSignedIn
                        ? BuildUnlockProgressText(response)
                        : new FormattedString
                        {
                            Spans =
                            {
                                new Span { Text = "Teken in om julle ontsluitings te sien." }
                            }
                        },
                    FontSize = 17,
                    FontFamily = PoppinsSemiBoldFontFamily,
                    TextColor = Colors.White,
                    HorizontalTextAlignment = TextAlignment.Center
                }
            }
        };

        if (!response.IsSignedIn)
        {
            var signInButton = new Button
            {
                Text = "Teken in",
                BackgroundColor = Colors.Transparent,
                TextColor = Colors.White,
                FontFamily = PoppinsBoldFontFamily,
                Padding = new Thickness(10, 2),
                HeightRequest = 30
            };
            signInButton.Clicked += async (_, _) => await Shell.Current.GoToAsync(nameof(AccountPage), animate: true);
            summary.Children.Add(signInButton);
        }

        return new VerticalStackLayout
        {
            Spacing = 0,
            HorizontalOptions = LayoutOptions.Fill,
            Children =
            {
                new Image
                {
                    Source = "schink_character_lineup.png",
                    HeightRequest = 112,
                    Aspect = Aspect.AspectFit,
                    HorizontalOptions = LayoutOptions.Fill,
                    Margin = new Thickness(0, 0, 0, -6)
                },
                new Image
                {
                    Source = "karakters_title.png",
                    WidthRequest = 300,
                    HeightRequest = 62,
                    Aspect = Aspect.AspectFill,
                    HorizontalOptions = LayoutOptions.Center,
                    Margin = new Thickness(0, -4, 0, -6)
                },
                new ContentView
                {
                    Margin = new Thickness(0, 7, 0, 0),
                    HorizontalOptions = LayoutOptions.Center,
                    Content = summary
                }
            }
        };
    }

    private static FormattedString BuildUnlockProgressText(MobileCharactersResponse response) =>
        new()
        {
            Spans =
            {
                new Span
                {
                    Text = $"{response.UnlockedCount} van {response.TotalCount}",
                    FontFamily = PoppinsBoldFontFamily,
                    FontAttributes = FontAttributes.Bold
                },
                new Span { Text = " oopgesluit" }
            }
        };

    private sealed class ReusableCharacterCardView : ContentView
    {
        private const double HeadingMinimumHeight = 34;
        private const double SummaryMinimumHeight = 48;
        private const double StoryActionSlotHeight = 31;
        private readonly KaraktersPage _owner;
        private readonly Border _card;
        private readonly Grid _media;
        private readonly Border _imageFrame;
        private readonly ProgressiveCachedImage _image;
        private readonly Border _lockButton;
        private readonly Border _speakerButton;
        private readonly Label _speakerIcon;
        private readonly Label _heading;
        private readonly Label _summary;
        private readonly Button _storyButton;
        private readonly Grid _storyButtonSlot;
        private MobileCharacterCard? _character;
        private string? _imageKey;
        private double _lastWidth = -1;

        public ReusableCharacterCardView(KaraktersPage owner)
        {
            _owner = owner;
            _image = new ProgressiveCachedImage(owner._apiClient)
            {
                Aspect = Aspect.AspectFit,
                HorizontalOptions = LayoutOptions.Fill,
                VerticalOptions = LayoutOptions.Fill,
                AutomationId = "character-image"
            };
            _imageFrame = new Border
            {
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = 15 },
                Padding = 4,
                Content = _image
            };
            _lockButton = BuildCharacterIconButton(
                new LockDrawable(),
                Color.FromArgb("#F39A32"),
                Color.FromArgb("#1B1207"),
                "Nog gesluit",
                CharacterIconPlacement.TopRight);
            _speakerIcon = new Label
            {
                Text = FontAwesomeVolumeHighGlyph,
                FontFamily = FontAwesomeSolidFontFamily,
                FontSize = 12,
                TextColor = Color.FromArgb("#103C49"),
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center,
                InputTransparent = true
            };
            _speakerButton = BuildCharacterFontIconButton(
                _speakerIcon,
                Color.FromArgb("#F5FAFB"),
                "Speel karakterstem",
                CharacterIconPlacement.TopRight);
            _media = new Grid
            {
                Children =
                {
                    _imageFrame,
                    _lockButton,
                    _speakerButton
                }
            };
            _heading = new Label
            {
                MinimumHeightRequest = HeadingMinimumHeight,
                Margin = new Thickness(1, 4, 1, 0),
                FontSize = 10.5,
                FontFamily = PoppinsBoldFontFamily,
                FontAttributes = FontAttributes.Bold,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center,
                MaxLines = 2,
                LineBreakMode = LineBreakMode.TailTruncation
            };
            _summary = new Label
            {
                MinimumHeightRequest = SummaryMinimumHeight,
                Margin = new Thickness(2, 0),
                FontSize = 8.5,
                FontFamily = PoppinsFontFamily,
                LineHeight = 1.1,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center,
                MaxLines = 3,
                LineBreakMode = LineBreakMode.TailTruncation
            };
            _storyButton = new Button
            {
                Text = FontAwesomePlayGlyph,
                HeightRequest = 28,
                WidthRequest = 28,
                Margin = new Thickness(2, 3, 2, 0),
                Padding = 0,
                CornerRadius = 14,
                FontSize = 10,
                FontFamily = FontAwesomeSolidFontFamily,
                HorizontalOptions = LayoutOptions.Center,
                AutomationId = "character-primary-story",
                IsVisible = false
            };
            _storyButtonSlot = new Grid
            {
                HeightRequest = StoryActionSlotHeight,
                Children =
                {
                    _storyButton
                }
            };
            _storyButton.Clicked += async (_, _) =>
            {
                if (_character?.PrimaryStory is null)
                {
                    return;
                }

                SafeHapticFeedback.TryPerform(HapticFeedbackType.Click);
                await _owner.OpenPrimaryStoryAsync(_character);
            };

            _card = new Border
            {
                StrokeThickness = 1,
                StrokeShape = new RoundRectangle { CornerRadius = 18 },
                Padding = new Thickness(6, 6, 6, 8),
                Shadow = IsAndroid
                    ? null!
                    : new Shadow
                    {
                        Brush = Brush.Black,
                        Offset = new Point(0, 5),
                        Radius = 10,
                        Opacity = 0.12f
                    },
                Content = new VerticalStackLayout
                {
                    Spacing = 2,
                    Children =
                    {
                        _media,
                        _heading,
                        _summary,
                        _storyButtonSlot
                    }
                }
            };
            var cardTap = new TapGestureRecognizer();
            cardTap.Tapped += async (_, _) =>
            {
                if (_character is null)
                {
                    return;
                }

                if (_character.IsUnlocked)
                {
                    await _owner.ShowCharacterProfileAsync(_character);
                    return;
                }

                await ShakeLockedCardAsync(_card);
            };
            _card.GestureRecognizers.Add(cardTap);

            var speakerTap = new TapGestureRecognizer();
            speakerTap.Tapped += async (_, _) =>
            {
                if (_character is null || !_character.IsUnlocked || _character.PreviewAudioClips.Count == 0)
                {
                    return;
                }

                SafeHapticFeedback.TryPerform(HapticFeedbackType.Click);
                var animation = AnimateSpeakerTapAsync();
                await _owner.PlayCharacterAudioAsync(_character);
                await animation;
            };
            _speakerButton.GestureRecognizers.Add(speakerTap);
            Content = _card;
        }

        protected override void OnBindingContextChanged()
        {
            base.OnBindingContextChanged();
            if (BindingContext is not MobileCharacterCard character)
            {
                _character = null;
                _imageKey = null;
                _image.Request = null;
                IsVisible = false;
                return;
            }

            if (ReferenceEquals(_character, character) && Math.Abs(_lastWidth - _owner.Width) < 1)
            {
                return;
            }

            IsVisible = true;
            _character = character;
            _lastWidth = _owner.Width;
            var mediaSize = _owner.ResolveCharacterMediaSize();
            var imageSize = Math.Max(1, mediaSize - 8);
            _media.HeightRequest = mediaSize;
            _imageFrame.HeightRequest = mediaSize;
            _image.WidthRequest = imageSize;
            _image.HeightRequest = imageSize;
            var imageKey = character.ImageUrl.Trim();
            if (!string.Equals(_imageKey, imageKey, StringComparison.Ordinal))
            {
                _image.SetImage(
                    character.ImageUrl,
                    character.PreviewImageUrl,
                    "schink_background.jpeg");
                _imageKey = imageKey;
            }

            SemanticProperties.SetDescription(_image, character.DisplayName);
            _imageFrame.BackgroundColor = character.IsUnlocked
                ? Color.FromArgb("#DDE9EA")
                : Color.FromArgb("#1B4651");
            _card.BackgroundColor = character.IsUnlocked
                ? Color.FromArgb("#F7FBFB")
                : Color.FromArgb("#1D4A56");
            _card.Stroke = character.IsUnlocked
                ? Color.FromArgb("#334E7680")
                : Color.FromArgb("#356C9AAA");
            _heading.Text = character.Heading;
            _heading.TextColor = character.IsUnlocked
                ? Color.FromArgb("#203236")
                : Color.FromArgb("#F4F8F9");
            _summary.Text = character.SummaryText;
            _summary.TextColor = character.IsUnlocked
                ? Color.FromArgb("#3D5359")
                : Color.FromArgb("#E1EEF0");
            _lockButton.IsVisible = !character.IsUnlocked;
            _speakerButton.IsVisible = character.IsUnlocked && character.PreviewAudioClips.Count > 0;
            SemanticProperties.SetDescription(_speakerButton, $"Speel {character.DisplayName} se stem");

            var showStoryButton = character.PrimaryStory is not null &&
                !string.IsNullOrWhiteSpace(character.CallToActionLabel);
            _storyButton.IsVisible = showStoryButton;
            SemanticProperties.SetDescription(_storyButton, character.CallToActionLabel);
            _storyButton.BackgroundColor = character.IsUnlocked
                ? Color.FromArgb("#F6A227")
                : Color.FromArgb("#8DCD65");
            _storyButton.TextColor = character.IsUnlocked
                ? Color.FromArgb("#243023")
                : Color.FromArgb("#153718");
        }

        private async Task AnimateSpeakerTapAsync()
        {
            _speakerButton.CancelAnimations();
            _speakerIcon.CancelAnimations();
            _speakerButton.BackgroundColor = Color.FromArgb("#F39A32");
            _speakerIcon.TextColor = Color.FromArgb("#1D1306");

            try
            {
                for (var pulse = 0; pulse < 3; pulse++)
                {
                    await Task.WhenAll(
                        _speakerButton.ScaleToAsync(1.08, 100, Easing.CubicOut),
                        _speakerIcon.RotateToAsync(pulse % 2 == 0 ? -9 : 9, 100, Easing.CubicOut));
                    await Task.WhenAll(
                        _speakerButton.ScaleToAsync(0.98, 100, Easing.CubicInOut),
                        _speakerIcon.RotateToAsync(pulse % 2 == 0 ? 7 : -7, 100, Easing.CubicInOut));
                }
            }
            finally
            {
                _speakerButton.Scale = 1;
                _speakerIcon.Rotation = 0;
                _speakerButton.BackgroundColor = Color.FromArgb("#F5FAFB");
                _speakerIcon.TextColor = Color.FromArgb("#103C49");
            }
        }
    }

    private double ResolveCharacterMediaSize()
    {
        return MobileResponsiveLayout.ResolveCharacterMediaSize(Width, _charactersGridLayout.Span);
    }

    private static Task OpenStoriesSearchAsync() =>
        Shell.Current.GoToAsync(nameof(SearchPage), animate: false);

    private static Task OpenNotificationsAsync() =>
        Shell.Current.GoToAsync(nameof(KennisgewingsPage), animate: false);

    private static Border BuildCharacterIconButton(
        IDrawable drawable,
        Color backgroundColor,
        Color iconColor,
        string automationName,
        CharacterIconPlacement placement)
    {
        var button = new Border
        {
            BackgroundColor = backgroundColor,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 999 },
            WidthRequest = 26,
            HeightRequest = 26,
            Margin = 5,
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = placement == CharacterIconPlacement.TopRight
                ? LayoutOptions.Start
                : LayoutOptions.End,
            AutomationId = automationName,
            Shadow = IsAndroid
                ? null!
                : new Shadow
                {
                    Brush = Brush.Black,
                    Offset = new Point(0, 3),
                    Radius = 6,
                    Opacity = 0.14f
                },
            Content = new GraphicsView
            {
                Drawable = new TintedDrawable(drawable, iconColor),
                WidthRequest = 14,
                HeightRequest = 14,
                Margin = 6,
                InputTransparent = true
            }
        };
        return button;
    }

    private static Border BuildCharacterFontIconButton(
        View icon,
        Color backgroundColor,
        string automationName,
        CharacterIconPlacement placement)
    {
        return new Border
        {
            BackgroundColor = backgroundColor,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 999 },
            WidthRequest = 26,
            HeightRequest = 26,
            Margin = 5,
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = placement == CharacterIconPlacement.TopRight
                ? LayoutOptions.Start
                : LayoutOptions.End,
            AutomationId = automationName,
            Shadow = IsAndroid
                ? null!
                : new Shadow
                {
                    Brush = Brush.Black,
                    Offset = new Point(0, 3),
                    Radius = 6,
                    Opacity = 0.14f
                },
            Content = icon
        };
    }

    private async Task ShowCharacterProfileAsync(MobileCharacterCard character)
    {
        if (!character.IsUnlocked)
        {
            return;
        }

        var closeButton = new Button
        {
            Text = "✕",
            FontSize = 18,
            BackgroundColor = Color.FromArgb("#F7FFFFFF"),
            TextColor = Color.FromArgb("#C63B36"),
            CornerRadius = 22,
            WidthRequest = 44,
            HeightRequest = 44,
            Padding = 0,
            HorizontalOptions = LayoutOptions.End,
            AutomationId = "Maak karakterprofiel toe"
        };
        var wideLayout = MobileResponsiveLayout.IsWide(Width);
        var profileImage = new ProgressiveCachedImage(
            _apiClient,
            new ProgressiveImageRequest(
                character.ImageUrl,
                character.PreviewImageUrl,
                "schink_background.jpeg"))
        {
            HeightRequest = wideLayout ? 340 : 260,
            Aspect = Aspect.AspectFit,
            HorizontalOptions = LayoutOptions.Fill
        };
        var imageButton = new Border
        {
            BackgroundColor = Color.FromArgb("#F3E6CC"),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 22 },
            Padding = 10,
            Content = profileImage
        };
        if (character.PreviewAudioClips.Count > 0)
        {
            var imageTap = new TapGestureRecognizer();
            imageTap.Tapped += async (_, _) =>
            {
                SafeHapticFeedback.TryPerform(HapticFeedbackType.Click);
                await imageButton.ScaleToAsync(1.04, 100, Easing.CubicOut);
                await imageButton.ScaleToAsync(1, 140, Easing.CubicIn);
                await PlayCharacterAudioAsync(character);
            };
            imageButton.GestureRecognizers.Add(imageTap);
        }

        var profileContent = new VerticalStackLayout
        {
            Padding = new Thickness(16, 14, 16, 28),
            Spacing = 14,
            Children =
            {
                imageButton,
                new Label
                {
                    Text = character.DisplayName,
                    FontSize = 30,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Color.FromArgb("#243238"),
                    HorizontalTextAlignment = TextAlignment.Center
                }
            }
        };

        if (!string.IsNullOrWhiteSpace(character.Tagline))
        {
            profileContent.Children.Add(new Label
            {
                Text = character.Tagline,
                FontSize = 15,
                TextColor = Color.FromArgb("#52605C"),
                HorizontalTextAlignment = TextAlignment.Center,
                LineBreakMode = LineBreakMode.WordWrap
            });
        }

        AddProfilePanel(profileContent,
            ("Tipe", character.Species),
            ("Blyplek", character.Habitat),
            ("Sê-ding", character.Catchphrase),
            ("Gunsteling-ding", character.FavoriteThing),
            ("Eerste verskyning", character.FirstAppearance));
        AddProfilePanel(profileContent,
            ("Kenmerk", character.CharacterTrait),
            ("Goue Les", character.GoldenLesson),
            ("Kernwaarde", character.CoreValue));
        AddProfilePanel(profileContent,
            ("Vragie", character.ReflectionQuestion),
            ("Uitdaging vir jou", character.ChallengeText));

        var friends = BuildFriends(character);
        if (friends is not null)
        {
            profileContent.Children.Add(friends);
        }

        var relatedStories = BuildRelatedStories(character);
        if (relatedStories is not null)
        {
            profileContent.Children.Add(relatedStories);
        }

        var pageWidth = Width > 0
            ? Width
            : DeviceDisplay.MainDisplayInfo.Width / DeviceDisplay.MainDisplayInfo.Density;
        var pageHeight = Height > 0
            ? Height
            : DeviceDisplay.MainDisplayInfo.Height / DeviceDisplay.MainDisplayInfo.Density;
        var profileScroll = new ScrollView
        {
            VerticalOptions = LayoutOptions.Fill,
            VerticalScrollBarVisibility = ScrollBarVisibility.Never,
            Content = profileContent
        };
        var closeHeader = new Grid
        {
            Padding = new Thickness(16, 14, 16, 4),
            HorizontalOptions = LayoutOptions.Fill,
            Children =
            {
                closeButton
            }
        };
        var profileLayout = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Star }
            },
            Children =
            {
                closeHeader,
                profileScroll
            }
        };
        Grid.SetRow(profileScroll, 1);
        var profileCard = new Border
        {
            WidthRequest = Math.Min(wideLayout ? 560 : 420, pageWidth - 28),
            HeightRequest = Math.Min(wideLayout ? 860 : 760, pageHeight - 56),
            Margin = new Thickness(14, 28),
            Padding = 0,
            BackgroundColor = Colors.White,
            Stroke = Color.FromArgb("#26103945"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 28 },
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            ZIndex = 1,
            Shadow = new Shadow
            {
                Brush = Brush.Black,
                Offset = new Point(0, 18),
                Radius = 34,
                Opacity = 0.3f
            },
            Content = profileLayout
        };
        var backdrop = new BoxView
        {
            Color = Color.FromArgb("#88073742"),
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill
        };
        var backdropTap = new TapGestureRecognizer();
        backdropTap.Tapped += (_, _) => CloseCharacterProfile();
        backdrop.GestureRecognizers.Add(backdropTap);
        closeButton.Clicked += (_, _) => CloseCharacterProfile();

        _profileOverlay.Children.Clear();
        _profileOverlay.Children.Add(backdrop);
        _profileOverlay.Children.Add(profileCard);
        _refreshView.InputTransparent = true;
        _topBarOverlay.InputTransparent = true;
        _profileOverlay.InputTransparent = false;
        _profileOverlay.Opacity = 1;
        profileCard.Scale = 0.94;
        profileCard.Opacity = 0;
        _profileOverlay.IsVisible = true;
        await Task.WhenAll(
            profileCard.FadeToAsync(1, 120, Easing.CubicOut),
            profileCard.ScaleToAsync(1, 180, Easing.CubicOut));
    }

    private void CloseCharacterProfile()
    {
        _profileOverlay.InputTransparent = true;
        _profileOverlay.IsVisible = false;
        _profileOverlay.Opacity = 1;
        _profileOverlay.Children.Clear();
        _refreshView.InputTransparent = false;
        _topBarOverlay.InputTransparent = false;
    }

    private static void AddProfilePanel(
        VerticalStackLayout content,
        params (string Label, string? Value)[] fields)
    {
        var panel = new VerticalStackLayout { Spacing = 10 };
        foreach (var field in fields.Where(field => !string.IsNullOrWhiteSpace(field.Value)))
        {
            panel.Children.Add(new VerticalStackLayout
            {
                Spacing = 2,
                Children =
                {
                    new Label
                    {
                        Text = field.Label,
                        FontSize = 13,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = Color.FromArgb("#183D49")
                    },
                    new Label
                    {
                        Text = field.Value!.Trim(),
                        FontSize = 15,
                        TextColor = Color.FromArgb("#26373E"),
                        LineBreakMode = LineBreakMode.WordWrap
                    }
                }
            });
        }

        if (panel.Children.Count == 0)
        {
            return;
        }

        content.Children.Add(new Border
        {
            BackgroundColor = Color.FromArgb("#FFFDF7"),
            Stroke = Color.FromArgb("#E7D1A2"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 18 },
            Padding = 15,
            Content = panel
        });
    }

    private View? BuildFriends(MobileCharacterCard character)
    {
        var friendNames = SplitFriendNames(character.Friends);
        if (friendNames.Count == 0 || _response is null)
        {
            return null;
        }

        var layout = new FlexLayout
        {
            Direction = FlexDirection.Row,
            Wrap = FlexWrap.Wrap,
            JustifyContent = FlexJustify.SpaceAround,
            AlignItems = FlexAlignItems.Start
        };

        foreach (var friendName in friendNames)
        {
            var friend = _response.Characters.FirstOrDefault(candidate =>
                string.Equals(NormalizeFriendToken(candidate.DisplayName), NormalizeFriendToken(friendName), StringComparison.Ordinal) ||
                string.Equals(NormalizeFriendToken(candidate.Slug.Replace("-", " ")), NormalizeFriendToken(friendName), StringComparison.Ordinal));
            if (friend is null)
            {
                layout.Children.Add(BuildUnmatchedFriend(friendName));
                continue;
            }

            var tile = BuildFriendTile(friend);
            if (friend.IsUnlocked)
            {
                var tap = new TapGestureRecognizer();
                tap.Tapped += async (_, _) =>
                {
                    CloseCharacterProfile();
                    await ShowCharacterProfileAsync(friend);
                };
                tile.GestureRecognizers.Add(tap);
            }
            layout.Children.Add(tile);
        }

        return BuildSection("Maats", layout);
    }

    private View BuildFriendTile(MobileCharacterCard friend)
    {
        var tile = new VerticalStackLayout
        {
            WidthRequest = 92,
            Padding = 6,
            Spacing = 5,
            Children =
            {
                new ProgressiveCachedImage(
                    _apiClient,
                    new ProgressiveImageRequest(
                        friend.ImageUrl,
                        friend.PreviewImageUrl,
                        "schink_background.jpeg"))
                {
                    HeightRequest = 72,
                    WidthRequest = 72,
                    Aspect = Aspect.AspectFit
                },
                new Label
                {
                    Text = friend.Heading,
                    FontSize = 12,
                    TextColor = Color.FromArgb("#26373E"),
                    HorizontalTextAlignment = TextAlignment.Center,
                    LineBreakMode = LineBreakMode.WordWrap
                }
            }
        };
        return tile;
    }

    private static View BuildUnmatchedFriend(string name)
    {
        var tile = new VerticalStackLayout
        {
            WidthRequest = 92,
            Padding = 6,
            Spacing = 5,
            Children =
            {
                new Label
                {
                    Text = "?",
                    FontSize = 34,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Color.FromArgb("#7B6553"),
                    HeightRequest = 72,
                    HorizontalTextAlignment = TextAlignment.Center,
                    VerticalTextAlignment = TextAlignment.Center
                },
                new Label
                {
                    Text = name,
                    FontSize = 12,
                    TextColor = Color.FromArgb("#26373E"),
                    HorizontalTextAlignment = TextAlignment.Center
                }
            }
        };
        return tile;
    }

    private View? BuildRelatedStories(MobileCharacterCard character)
    {
        if (character.RelatedStories.Count == 0)
        {
            return null;
        }

        var stories = new HorizontalStackLayout { Spacing = 12 };
        foreach (var story in character.RelatedStories)
        {
            var tile = new VerticalStackLayout
            {
                WidthRequest = 132,
                Spacing = 7,
                Children =
                {
                    new Border
                    {
                        BackgroundColor = Color.FromArgb("#F3E6CC"),
                        StrokeThickness = 0,
                        StrokeShape = new RoundRectangle { CornerRadius = 12 },
                        HeightRequest = 150,
                        Content = new ProgressiveCachedImage(
                            _apiClient,
                            new ProgressiveImageRequest(story.ImageUrl, FallbackFile: "schink_background.jpeg"))
                        {
                            Aspect = Aspect.AspectFill
                        }
                    },
                    new Label
                    {
                        Text = story.Title,
                        FontSize = 13,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = Color.FromArgb("#243238"),
                        LineBreakMode = LineBreakMode.WordWrap
                    }
                }
            };
            var tap = new TapGestureRecognizer();
            tap.Tapped += async (_, _) => await OpenStoryAsync(story);
            tile.GestureRecognizers.Add(tap);
            stories.Children.Add(tile);
        }

        return BuildSection(
            "Stories met hierdie karakter",
            new ScrollView { Orientation = ScrollOrientation.Horizontal, Content = stories });
    }

    private static View BuildSection(string heading, View content) =>
        new Border
        {
            BackgroundColor = Color.FromArgb("#FFFDF7"),
            Stroke = Color.FromArgb("#E7D1A2"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 18 },
            Padding = 14,
            Content = new VerticalStackLayout
            {
                Spacing = 10,
                Children =
                {
                    new Label
                    {
                        Text = heading,
                        FontSize = 16,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = Color.FromArgb("#183D49")
                    },
                    content
                }
            }
        };

    private async Task PlayCharacterAudioAsync(MobileCharacterCard character)
    {
        if (!character.IsUnlocked || character.PreviewAudioClips.Count == 0)
        {
            return;
        }

        var clip = character.PreviewAudioClips[Random.Shared.Next(character.PreviewAudioClips.Count)];
        try
        {
            _storyPlaybackSession.Stop();
            var playbackUrl = await _apiClient.PrepareAudioPlaybackSourceAsync(
                clip.AudioUrl,
                character.Slug,
                "karakter");
            await _audioPlaybackService.PlayAsync(
                playbackUrl,
                new AudioPlaybackMetadata(
                    character.DisplayName,
                    "Schink Stories Karakters",
                    _apiClient.BuildImageUrl(character.ImageUrl)));
            _ = _apiClient.TrackCharacterProfileListenAsync(character.Slug, clip.StreamSlug);
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Kon nie karakterklank speel nie", ex.Message, "Maak toe");
        }
    }

    private async Task OpenPrimaryStoryAsync(MobileCharacterCard character)
    {
        if (character.PrimaryStory is not null)
        {
            await OpenStoryAsync(character.PrimaryStory);
        }
    }

    private async Task OpenStoryAsync(MobileCharacterStoryLink story)
    {
        CloseCharacterProfile();

        var parameters = new ShellNavigationQueryParameters
        {
            ["slug"] = story.Slug,
            ["source"] = story.Source
        };
        await Shell.Current.GoToAsync(
            nameof(StoryDetailPage),
            animate: false,
            parameters);
    }

    private async Task<bool> TryOpenPendingCharacterAsync()
    {
        if (!_isPageActive || string.IsNullOrWhiteSpace(_pendingCharacterSlug) || _response is null)
        {
            return false;
        }

        var character = _response.Characters.FirstOrDefault(candidate =>
            string.Equals(candidate.Slug, _pendingCharacterSlug, StringComparison.OrdinalIgnoreCase));
        if (character is null || !character.IsUnlocked)
        {
            return false;
        }

        await ShowCharacterProfileAsync(character);
        _pendingCharacterSlug = null;
        return true;
    }

    private async Task ResolvePendingCharacterAsync()
    {
        if (_isResolvingPendingCharacter || string.IsNullOrWhiteSpace(_pendingCharacterSlug))
        {
            return;
        }

        _isResolvingPendingCharacter = true;
        try
        {
            if (await TryOpenPendingCharacterAsync() || string.IsNullOrWhiteSpace(_pendingCharacterSlug))
            {
                return;
            }

            // Character unlock notifications can arrive while this singleton page
            // still holds the pre-unlock response. Refresh before deciding that the
            // requested character is unavailable or still locked.
            await LoadAsync(forceRefresh: true);
        }
        catch
        {
            // Keep the slug pending so a later appearance or manual refresh can retry.
        }
        finally
        {
            _isResolvingPendingCharacter = false;
        }
    }

    private static async Task ShakeLockedCardAsync(VisualElement card)
    {
        SafeHapticFeedback.TryPerform(HapticFeedbackType.Click);
        foreach (var offset in new[] { -10d, 10d, -8d, 8d, -4d, 4d, 0d })
        {
            await card.TranslateToAsync(offset, 0, 55, Easing.CubicInOut);
        }
    }

    private static IReadOnlyList<string> SplitFriendNames(string? friends)
    {
        if (string.IsNullOrWhiteSpace(friends))
        {
            return Array.Empty<string>();
        }

        return friends
            .Replace(" en ", ",", StringComparison.OrdinalIgnoreCase)
            .Replace("&", ",", StringComparison.OrdinalIgnoreCase)
            .Replace("/", ",", StringComparison.OrdinalIgnoreCase)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string NormalizeFriendToken(string value) =>
        new(value.Trim().ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    private static View BuildState(string message, bool isError = false) =>
        new Border
        {
            BackgroundColor = Colors.White,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 22 },
            Padding = 18,
            Content = new Label
            {
                Text = message,
                FontSize = 15,
                TextColor = isError ? Color.FromArgb("#B42318") : Color.FromArgb("#52605C"),
                HorizontalTextAlignment = TextAlignment.Center,
                LineBreakMode = LineBreakMode.WordWrap
            }
        };

    private void StartImageWarmup(MobileCharactersResponse response)
    {
        _imageWarmupCancellation?.Cancel();
        _imageWarmupCancellation?.Dispose();
        _imageWarmupCancellation = new CancellationTokenSource();
        var token = _imageWarmupCancellation.Token;
        var imageUrls = response.Characters
            .Select(character => character.PreviewImageUrl)
            .Concat(response.Characters.Select(character => character.ImageUrl))
            .Concat(response.Characters.SelectMany(character =>
                character.RelatedStories.Select(story => story.ImageUrl)))
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (imageUrls.Length == 0)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                // Match Luister's cached-first path: allow the first layout to settle,
                // then prepare every gallery portrait before secondary story artwork.
                await Task.Delay(TimeSpan.FromMilliseconds(750), token);
                await _apiClient.CacheImagesAsync(
                    imageUrls,
                    token,
                    maxImages: 64,
                    maxDegreeOfParallelism: IsAndroid || IsIOS ? 1 : 3);
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
                // Character artwork warmup is best-effort; remote image sources remain available.
            }
        }, token);
    }

    private static string BuildLoadErrorMessage(Exception ex)
    {
        if (!string.IsNullOrWhiteSpace(ex.Message) &&
            ex.Message.Contains("Karakters-data", StringComparison.OrdinalIgnoreCase))
        {
            return ex.Message;
        }

        return "Kon nie die Karakters-data laai nie. Probeer asseblief weer.";
    }

    private enum CharacterIconPlacement
    {
        TopRight
    }

    private sealed class TintedDrawable(IDrawable drawable, Color color) : IDrawable
    {
        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            canvas.StrokeColor = color;
            canvas.FillColor = color;
            drawable.Draw(canvas, dirtyRect);
        }
    }

    private sealed class LockDrawable : IDrawable
    {
        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            canvas.StrokeSize = 2.2f;
            canvas.StrokeLineCap = LineCap.Round;
            canvas.StrokeLineJoin = LineJoin.Round;
            var shackle = new PathF();
            shackle.MoveTo(dirtyRect.Width * 0.30f, dirtyRect.Height * 0.47f);
            shackle.LineTo(dirtyRect.Width * 0.30f, dirtyRect.Height * 0.35f);
            shackle.CurveTo(
                dirtyRect.Width * 0.30f,
                dirtyRect.Height * 0.12f,
                dirtyRect.Width * 0.70f,
                dirtyRect.Height * 0.12f,
                dirtyRect.Width * 0.70f,
                dirtyRect.Height * 0.35f);
            shackle.LineTo(dirtyRect.Width * 0.70f, dirtyRect.Height * 0.47f);
            canvas.DrawPath(shackle);
            canvas.FillRoundedRectangle(
                new RectF(
                    dirtyRect.Width * 0.20f,
                    dirtyRect.Height * 0.42f,
                    dirtyRect.Width * 0.60f,
                    dirtyRect.Height * 0.45f),
                3);
        }
    }

}
