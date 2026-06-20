using Shink.Mobile.Models;
using Shink.Mobile.Services;
using Shink.Mobile.Views;
#if IOS
using iOSPage = Microsoft.Maui.Controls.PlatformConfiguration.iOSSpecific.Page;
#elif ANDROID
using AndroidWindowInsets = Android.Views.WindowInsets;
#endif

namespace Shink.Mobile.Pages;

[QueryProperty(nameof(StorySlug), "slug")]
[QueryProperty(nameof(Source), "source")]
public sealed class StoryDetailPage : ContentPage, IQueryAttributable
{
    private const double TallScreenThreshold = 820;
    private const uint CloseAnimationDurationMs = 170;
    private const double ListenFlushThresholdSeconds = 12;
    private const double ListenMaxEventSeconds = 600;
    private const double ListenMinEventSeconds = 0.2;
    private static readonly double[] PlaybackSpeedSteps = [0.75, 1.0, 1.25, 1.5];
    private static readonly Color PlayerBackgroundColor = Color.FromArgb("#061816");
    private static readonly Color PlayerPanelColor = Color.FromArgb("#102724");
    private static readonly Color PlayerPillColor = Color.FromArgb("#1B302D");
    private static readonly Color PlayerTextColor = Color.FromArgb("#F7FBF7");
    private static readonly Color PlayerMutedTextColor = Color.FromArgb("#AAB7B2");
    private static readonly Color PlayerAccentColor = Color.FromArgb("#FFFFFF");
    private static readonly Color StorySummaryCardColor = Color.FromArgb("#222222");
    private static readonly Color StorySummaryTextColor = Color.FromArgb("#F6F1EA");
    private static readonly Color StorySummaryLeadColor = Color.FromArgb("#F0DDC8");
    private static readonly Color StorySummaryGoldColor = Color.FromArgb("#D4B075");
    private static readonly Color StorySummaryPillColor = Color.FromArgb("#403A30");
    private static readonly Color StorySummaryTestButtonColor = Color.FromArgb("#F3C86D");

    private readonly MobileApiClient _apiClient;
    private readonly SessionState _sessionState;
    private readonly IAudioPlaybackService _audioPlaybackService;
    private readonly IOfflineStoryDownloadService _offlineDownloadService;
    private readonly PlaylistPlaybackState _playlistPlaybackState;
    private readonly ContinueListeningState _continueListeningState;
    private readonly IOrientationService _orientationService;
    private readonly PlayerTransitionBackdropState _transitionBackdropState;
    private readonly NavigationGate _navigationGate = new();
    private readonly Grid _root;
    private readonly Grid _playerSurface;
    private readonly Image _closeBackdrop;
    private readonly VerticalStackLayout _content;
    private Border? _castSheet;
    private BoxView? _castScrim;
    private MobileStoryDetailResponse? _currentDetail;
    private Button? _activePlayButton;
    private ProgressBar? _activeProgressBar;
    private Label? _activeCurrentTimeLabel;
    private Label? _activeDurationLabel;
    private TimeSpan? _activeCatalogDuration;
    private IDispatcherTimer? _progressTimer;
    private MobileStorySummary? _activeStory;
    private MobileStorySummary? _previewStory;
    private IReadOnlyList<MobileStorySummary> _playlistStories = Array.Empty<MobileStorySummary>();
    private string? _playlistTitle;
    private string? _playlistSlug;
    private string? _loadedKey;
    private CancellationTokenSource? _loadCts;
    private bool _isPageActive;
    private bool _isDownloadEventSubscribed;
    private bool _isPlaybackEventSubscribed;
    private bool _isClosing;
    private bool _isShowingFullscreenCover;
    private bool _wasKeepScreenOnBeforeFullscreen;
    private bool _isFavoriteRequestInFlight;
    private bool _pendingAutoplayAfterLoad;
    private bool _suppressNextPauseTracking;
    private Button? _inlinePlayButton;
    private ProgressBar? _inlineProgressBar;
    private Label? _inlineCurrentTimeLabel;
    private Label? _inlineDurationLabel;
    private Guid _trackingSessionId;
    private string? _trackingStorySlug;
    private string? _trackingSource;
    private double _pendingListenSeconds;
    private TimeSpan? _lastTrackedPosition;
    private readonly Dictionary<int, string> _selectedStoryTestOptions = new();
    private bool _isStoryTestSubmitted;
    private ContentPage? _storyTestModalPage;
    private VerticalStackLayout? _storyTestModalContent;
    private MobileStoryDetailResponse? _storyTestDetail;

    public StoryDetailPage(
        MobileApiClient apiClient,
        SessionState sessionState,
        IAudioPlaybackService audioPlaybackService,
        IOfflineStoryDownloadService offlineDownloadService,
        PlaylistPlaybackState playlistPlaybackState,
        ContinueListeningState continueListeningState,
        IOrientationService orientationService,
        PlayerTransitionBackdropState transitionBackdropState)
    {
        _apiClient = apiClient;
        _sessionState = sessionState;
        _audioPlaybackService = audioPlaybackService;
        _offlineDownloadService = offlineDownloadService;
        _playlistPlaybackState = playlistPlaybackState;
        _continueListeningState = continueListeningState;
        _orientationService = orientationService;
        _transitionBackdropState = transitionBackdropState;
        BackgroundColor = PlayerBackgroundColor;
        SafeAreaEdges = SafeAreaEdges.None;
        Shell.SetNavBarIsVisible(this, false);
        Shell.SetTabBarIsVisible(this, false);

        _content = new VerticalStackLayout
        {
            Padding = new Thickness(20, 16, 20, 20),
            Spacing = 14
        };

        var scrollView = new ScrollView
        {
            BackgroundColor = PlayerBackgroundColor,
            VerticalScrollBarVisibility = ScrollBarVisibility.Never,
            Content = _content
        };

        _playerSurface = new Grid
        {
            BackgroundColor = PlayerBackgroundColor,
            Children = { scrollView }
        };

        _closeBackdrop = new Image
        {
            Aspect = Aspect.Fill,
            IsVisible = false,
            InputTransparent = true
        };

        _root = new Grid
        {
            BackgroundColor = PlayerBackgroundColor,
            Children = { _closeBackdrop, _playerSurface }
        };

        Content = _root;
    }

    public string? StorySlug { get; set; }
    public string? Source { get; set; }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("preview", out var preview) &&
            preview is MobileStorySummary story)
        {
            _previewStory = story;
            StorySlug = story.Slug;
            Source = story.Source;
        }

        if (query.TryGetValue("playlistSlug", out var playlistSlug))
        {
            _playlistSlug = playlistSlug?.ToString();
            _playlistTitle = _playlistPlaybackState.Title;
            _playlistStories = string.Equals(_playlistPlaybackState.Slug, _playlistSlug, StringComparison.OrdinalIgnoreCase)
                ? _playlistPlaybackState.Stories
                : Array.Empty<MobileStorySummary>();
        }
        else
        {
            _playlistStories = Array.Empty<MobileStorySummary>();
            _playlistTitle = null;
            _playlistSlug = null;
        }

        if (query.TryGetValue("playlistTitle", out var playlistTitle))
        {
            _playlistTitle = playlistTitle?.ToString();
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _isPageActive = true;
        _isClosing = false;
        _closeBackdrop.IsVisible = false;
        _closeBackdrop.Source = null;
        _playerSurface.TranslationY = 0;
        SubscribeDownloadEvents();
        SubscribePlaybackEvents();

        var loadKey = $"{StorySlug}:{Source}";
        if (string.IsNullOrWhiteSpace(StorySlug) || loadKey == _loadedKey)
        {
            return;
        }

        _loadedKey = loadKey;
        if (_previewStory is not null)
        {
            RenderPreview(_previewStory);
        }

        CancelActiveLoad();
        _loadCts = new CancellationTokenSource();
        await LoadAsync(showLoading: _previewStory is null, cancellationToken: _loadCts.Token);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        if (_isShowingFullscreenCover)
        {
            return;
        }

        FlushPendingListen("pagehide", force: true);
        _suppressNextPauseTracking = true;
        _isPageActive = false;
        CancelActiveLoad();
        DismissCastPicker();
        StopProgressTimer();
        UnsubscribeDownloadEvents();
        UnsubscribePlaybackEvents();
        TryStopAudioPlayback();
        ClearActivePlaybackUi();
    }

    private void SubscribeDownloadEvents()
    {
        if (_isDownloadEventSubscribed)
        {
            return;
        }

        _offlineDownloadService.DownloadsChanged += OnDownloadsChanged;
        _isDownloadEventSubscribed = true;
    }

    private void UnsubscribeDownloadEvents()
    {
        if (!_isDownloadEventSubscribed)
        {
            return;
        }

        _offlineDownloadService.DownloadsChanged -= OnDownloadsChanged;
        _isDownloadEventSubscribed = false;
    }

    private void OnDownloadsChanged(object? sender, EventArgs args)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (_isPageActive && _currentDetail is not null)
            {
                RenderDetail(_currentDetail, trackView: false);
            }
        });
    }

    private async Task LoadAsync(bool showLoading = true, CancellationToken cancellationToken = default)
    {
        var renderedCachedDetail = false;
        if (showLoading)
        {
            renderedCachedDetail = await TryRenderCachedStoryAsync(cancellationToken);
            if (!renderedCachedDetail)
            {
                _content.Children.Clear();
                _content.Children.Add(BuildLoadingState());
            }
        }

        try
        {
            var detail = await _apiClient.GetStoryAsync(StorySlug ?? string.Empty, Source ?? "luister", cancellationToken);
            if (cancellationToken.IsCancellationRequested || !_isPageActive)
            {
                return;
            }

            if (detail is null)
            {
                _content.Children.Clear();
                _content.Children.Add(BuildMessage("Storie nie gevind nie."));
                return;
            }

            RenderDetail(detail, trackView: !renderedCachedDetail);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (cancellationToken.IsCancellationRequested || !_isPageActive)
            {
                return;
            }

            var offlineDownload = await _offlineDownloadService.GetDownloadAsync(
                StorySlug ?? string.Empty,
                Source ?? "luister",
                cancellationToken);
            if (offlineDownload is not null)
            {
                RenderOfflineDetail(offlineDownload);
                return;
            }

            if (renderedCachedDetail)
            {
                return;
            }

            _content.Children.Clear();
            _content.Children.Add(BuildMessage(ex.Message));
        }
    }

    private async Task<bool> TryRenderCachedStoryAsync(CancellationToken cancellationToken)
    {
        try
        {
            var cachedDetail = await _apiClient.GetCachedStoryAsync(
                StorySlug ?? string.Empty,
                Source ?? "luister",
                cancellationToken);
            if (cachedDetail is null || cancellationToken.IsCancellationRequested || !_isPageActive)
            {
                return false;
            }

            RenderDetail(cachedDetail);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void RenderOfflineDetail(OfflineStoryDownload download)
    {
        var detail = _offlineDownloadService.CreateOfflineDetail(download);
        _content.Children.Clear();
        Title = detail.Story.Title;
        _currentDetail = detail;
        _activeStory = detail.Story;
        SaveContinueListening(detail);
        _content.Children.Add(BuildTopBar());
        _content.Children.Add(BuildCoverArt(detail));
        _content.Children.Add(BuildStoryHeader(detail));
        _content.Children.Add(BuildActionRail(detail));

        if (detail.RequiresSubscription)
        {
            _content.Children.Add(BuildMessage("Hierdie aflaai moet weer aanlyn bevestig word."));
            return;
        }

        _content.Children.Add(BuildAudioPlayer(detail));
        _content.Children.Add(BuildStoryInfoCard(detail));
        UpdateProgressState();
    }

    private void RenderPreview(MobileStorySummary story)
    {
        var previewDetail = new MobileStoryDetailResponse(
            Story: story,
            AudioUrl: null,
            ShareUrl: story.DetailUrl,
            RequiresSubscription: story.IsLocked,
            PreviousStory: null,
            NextStory: null,
            RelatedStories: Array.Empty<MobileStorySummary>(),
            Summary: null,
            Lessons: Array.Empty<string>(),
            ValueTags: Array.Empty<string>(),
            ConversationQuestions: Array.Empty<string>(),
            Characters: Array.Empty<string>(),
            CharacterTiles: Array.Empty<MobileStoryCharacter>(),
            YouTubeUrl: null,
            TestQuestions: Array.Empty<MobileStoryTestQuestion>(),
            LoginUrl: string.Empty,
            PlansUrl: string.Empty);

        _content.Children.Clear();
        Title = story.Title;
        _currentDetail = previewDetail;
        _activeStory = story;
        _content.Children.Add(BuildTopBar());
        _content.Children.Add(BuildCoverArt(previewDetail));
        _content.Children.Add(BuildStoryHeader(previewDetail));
        _content.Children.Add(BuildActionRail(previewDetail));
        _content.Children.Add(BuildInlineLoadingState());
    }

    private void RenderDetail(MobileStoryDetailResponse detail, bool trackView = true)
    {
        _content.Children.Clear();
        Title = detail.Story.Title;
        _currentDetail = detail;
        _activeStory = detail.Story;
        SaveContinueListening(detail);
        _content.Children.Add(BuildTopBar());
        _content.Children.Add(BuildCoverArt(detail));
        _content.Children.Add(BuildStoryHeader(detail));
        _content.Children.Add(BuildActionRail(detail));
        _ = _offlineDownloadService.RefreshAccessAsync(detail);

        if (detail.RequiresSubscription)
        {
            _content.Children.Add(BuildLockedPanel(detail));
        }
        else if (!string.IsNullOrWhiteSpace(detail.AudioUrl))
        {
            if (trackView)
            {
                _ = _apiClient.TrackStoryViewAsync(detail.Story.Slug, detail.Story.Source);
            }
            _content.Children.Add(BuildAudioPlayer(detail));
            UpdateProgressState();
            EnsureCatalogDurationVisibleAsync(detail);
            TryStartPendingAutoplay(detail);
        }
        else
        {
            _content.Children.Add(BuildMessage("Geen audio is tans beskikbaar vir hierdie storie nie."));
        }

        _content.Children.Add(BuildStoryInfoCard(detail));

        var playlistQueue = BuildPlaylistQueue(detail);
        if (playlistQueue is not null)
        {
            _content.Children.Add(playlistQueue);
        }

    }

    private static View BuildLoadingState() =>
        new Grid
        {
            HeightRequest = 520,
            Children =
            {
                new ActivityIndicator
                {
                    IsRunning = true,
                    Color = PlayerAccentColor,
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.Center
                }
            }
        };

    private View BuildTopBar()
    {
        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto)
            },
            HeightRequest = 52,
            ColumnSpacing = 14
        };

        var closeButton = BuildDownCaretButton();
        var closeTap = new TapGestureRecognizer();
        closeTap.Tapped += async (_, _) => await CloseAsync(closeButton);
        closeButton.GestureRecognizers.Add(closeTap);

        var menuButton = BuildTopIconButton("⋮");
        menuButton.Clicked += async (_, _) => await ShowPlayerMenuAsync();

        grid.Children.Add(closeButton);
        if (IsNativeRoutePickerAvailable)
        {
            var castButton = BuildCastButton();
            grid.Children.Add(castButton);
            Grid.SetColumn(castButton, 2);
            Grid.SetColumn(menuButton, 3);
        }
        else
        {
            Grid.SetColumn(menuButton, 2);
        }

        grid.Children.Add(menuButton);

        return grid;
    }

    private View BuildCastButton()
    {
        var button = new Grid
        {
            WidthRequest = 44,
            HeightRequest = 44,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };

        button.Children.Add(new GraphicsView
        {
            Drawable = new CastIconDrawable(),
            WidthRequest = 30,
            HeightRequest = 30,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            InputTransparent = true
        });

        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) => await ShowCastPickerAsync();
        button.GestureRecognizers.Add(tap);

        return button;
    }

    private async Task ShowCastPickerAsync()
    {
        if (!IsNativeRoutePickerAvailable)
        {
            return;
        }

        if (_castSheet is not null)
        {
            return;
        }

        var routePicker = new CastRoutePickerView
        {
            HeightRequest = 1,
            Opacity = 0.01,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.End
        };

        _castScrim = new BoxView
        {
            Color = Color.FromArgb("#B0000000"),
            InputTransparent = false
        };
        var dismissTap = new TapGestureRecognizer();
        dismissTap.Tapped += (_, _) => DismissCastPicker();
        _castScrim.GestureRecognizers.Add(dismissTap);

        _castSheet = new Border
        {
            BackgroundColor = Color.FromArgb("#202020"),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(22, 22, 0, 0) },
            Padding = new Thickness(30, 30, 30, 26),
            HeightRequest = Math.Min(Math.Max(ScreenHeight * 0.72, 540), 660),
            VerticalOptions = LayoutOptions.End,
            Content = new VerticalStackLayout
            {
                Spacing = 0,
                Children =
                {
                    new Label
                    {
                        Text = "Playing",
                        FontSize = 34,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = Color.FromArgb("#BEBEBE"),
                        Margin = new Thickness(0, 0, 0, 44)
                    },
                    BuildCastPlayingRow(),
                    BuildCastCurrentDeviceRow(),
                    new BoxView
                    {
                        HeightRequest = 1,
                        Color = Color.FromArgb("#353535"),
                        Margin = new Thickness(-30, 24, -30, 24)
                    },
                    BuildCastAvailableControlsHeader(),
                    BuildCastPickerDeviceRow(
                        CastSheetIconKind.AirPlay,
                        "AirPlay and Bluetooth devices",
                        () => routePicker.OpenPicker()),
                    routePicker
                }
            }
        };

        var swipeDown = new SwipeGestureRecognizer { Direction = SwipeDirection.Down };
        swipeDown.Swiped += (_, _) => DismissCastPicker();
        _castSheet.GestureRecognizers.Add(swipeDown);

        _root.Children.Add(_castScrim);
        _root.Children.Add(_castSheet);

        await Task.Delay(220);
    }

    private View BuildCastPlayingRow()
    {
        var story = _activeStory ?? _previewStory;
        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star)
            },
            ColumnSpacing = 18,
            Margin = new Thickness(0, 0, 0, 34)
        };

        grid.Children.Add(new Border
        {
            WidthRequest = 76,
            HeightRequest = 76,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 4 },
            Content = new Image
            {
                Source = string.IsNullOrWhiteSpace(story?.ImageUrl)
                    ? "schink_background.jpeg"
                    : _apiClient.BuildImageUrl(story.ImageUrl),
                Aspect = Aspect.AspectFill
            }
        });

        var text = new VerticalStackLayout
        {
            Spacing = 8,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Label
                {
                    Text = story?.Title ?? Title ?? "Schink Stories",
                    FontSize = 23,
                    TextColor = Colors.White,
                    MaxLines = 1,
                    LineBreakMode = LineBreakMode.TailTruncation
                },
                new Label
                {
                    Text = "SCHINK STORIES",
                    FontSize = 17,
                    TextColor = Color.FromArgb("#A6A6A6"),
                    MaxLines = 1
                }
            }
        };

        grid.Children.Add(text);
        Grid.SetColumn(text, 1);
        return grid;
    }

    private static View BuildCastCurrentDeviceRow()
    {
        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star)
            },
            ColumnSpacing = 22
        };

        grid.Children.Add(new GraphicsView
        {
            Drawable = new CastSheetIconDrawable(CastSheetIconKind.Phone),
            WidthRequest = 44,
            HeightRequest = 52,
            VerticalOptions = LayoutOptions.Center
        });

        var deviceName = string.IsNullOrWhiteSpace(DeviceInfo.Name) ? "this iPhone" : DeviceInfo.Name;
        var text = new VerticalStackLayout
        {
            Spacing = 4,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Label
                {
                    Text = "This device",
                    FontSize = 23,
                    TextColor = Colors.White
                },
                new Label
                {
                    Text = $"Connected to {deviceName}",
                    FontSize = 18,
                    TextColor = Color.FromArgb("#A6A6A6"),
                    MaxLines = 1,
                    LineBreakMode = LineBreakMode.TailTruncation
                }
            }
        };

        grid.Children.Add(text);
        Grid.SetColumn(text, 1);
        return grid;
    }

    private static View BuildCastAvailableControlsHeader() =>
        new Label
        {
            Text = "Available controls",
            FontSize = 23,
            TextColor = Colors.White,
            Margin = new Thickness(0, 0, 0, 22)
        };

    private static View BuildCastPickerDeviceRow(
        CastSheetIconKind iconKind,
        string title,
        Action onTap)
    {
        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star)
            },
            ColumnSpacing = 22,
            Padding = new Thickness(0, 14),
            HeightRequest = 76
        };

        grid.Children.Add(new GraphicsView
        {
            Drawable = new CastSheetIconDrawable(iconKind),
            WidthRequest = 52,
            HeightRequest = 52,
            VerticalOptions = LayoutOptions.Center
        });

        var label = new Label
        {
            Text = title,
            FontSize = 23,
            TextColor = Colors.White,
            VerticalTextAlignment = TextAlignment.Center,
            MaxLines = 1,
            LineBreakMode = LineBreakMode.TailTruncation
        };
        grid.Children.Add(label);
        Grid.SetColumn(label, 1);
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => onTap();
        grid.GestureRecognizers.Add(tap);
        return grid;
    }

    private void DismissCastPicker()
    {
        if (_castScrim is not null)
        {
            _root.Children.Remove(_castScrim);
            _castScrim = null;
        }

        if (_castSheet is not null)
        {
            _root.Children.Remove(_castSheet);
            _castSheet = null;
        }
    }

    private async Task ShowPlayerMenuAsync()
    {
        var detail = _currentDetail;
        if (detail is null)
        {
            return;
        }

        var actions = string.IsNullOrWhiteSpace(detail.ShareUrl)
            ? new[] { "Storie info", "Maak speler toe" }
            : new[] { "Storie info", "Deel storie", "Maak speler toe" };
        var selected = await DisplayActionSheetAsync("Storie opsies", "Kanselleer", null, actions);

        if (string.Equals(selected, "Storie info", StringComparison.Ordinal))
        {
            await DisplayAlertAsync(detail.Story.Title, detail.Story.Description, "Maak toe");
        }
        else if (string.Equals(selected, "Deel storie", StringComparison.Ordinal) &&
                 !string.IsNullOrWhiteSpace(detail.ShareUrl))
        {
            await Share.Default.RequestAsync(new ShareTextRequest
            {
                Title = detail.Story.Title,
                Text = detail.Story.Title,
                Uri = detail.ShareUrl
            });
        }
        else if (string.Equals(selected, "Maak speler toe", StringComparison.Ordinal))
        {
            await CloseAsync(this);
        }
    }

    private async Task CloseAsync(VisualElement closeButton)
    {
        await _navigationGate.RunAsync(async () =>
        {
            if (_isClosing)
            {
                return;
            }

            _isClosing = true;
            closeButton.IsEnabled = false;
            PrepareCloseBackdrop();
            CancelActiveLoad();
            await AnimateCloseAsync();
            await Shell.Current.GoToAsync("..", animate: false);
        });
    }

    private void PrepareCloseBackdrop()
    {
        var backdropSource = _transitionBackdropState.BuildImageSource();
        if (backdropSource is null)
        {
            return;
        }

        _closeBackdrop.Margin = ResolveBackdropMargin();
        _closeBackdrop.Source = backdropSource;
        _closeBackdrop.IsVisible = true;
    }

    private Thickness ResolveBackdropMargin()
    {
#if IOS
        var safeAreaInsets = iOSPage.GetSafeAreaInsets(this);
        return new Thickness(0, -safeAreaInsets.Top, 0, -safeAreaInsets.Bottom);
#elif ANDROID
        var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
        var insets = activity?.Window?.DecorView?.RootWindowInsets;
        if (insets is null)
        {
            return Thickness.Zero;
        }

        var systemBarInsets = insets.GetInsets(AndroidWindowInsets.Type.SystemBars());
        var density = DeviceDisplay.MainDisplayInfo.Density;
        if (density <= 0)
        {
            return Thickness.Zero;
        }

        return new Thickness(
            0,
            -(systemBarInsets.Top / density),
            0,
            -(systemBarInsets.Bottom / density));
#else
        return Thickness.Zero;
#endif
    }

    private async Task AnimateCloseAsync()
    {
        if (_content.TranslationY > 0 || _content.Opacity < 1)
        {
            return;
        }

        var closeDistance = Height > 0
            ? Height + 40
            : 760;

        await _playerSurface.TranslateToAsync(0, closeDistance, CloseAnimationDurationMs, Easing.CubicIn);
    }

    private void CancelActiveLoad()
    {
        if (_loadCts is null)
        {
            return;
        }

        _loadCts.Cancel();
        _loadCts.Dispose();
        _loadCts = null;
    }

    private void TryStopAudioPlayback()
    {
        try
        {
            _audioPlaybackService.Stop();
        }
        catch
        {
            // Teardown must never prevent Shell from returning to Luister.
        }
    }

    private void ClearActivePlaybackUi()
    {
        _activePlayButton = null;
        _activeProgressBar = null;
        _activeCurrentTimeLabel = null;
        _activeDurationLabel = null;
        ResetListenTracking();
    }

    private static Button BuildTopIconButton(string text) =>
        new()
        {
            Text = text,
            FontSize = 34,
            FontAttributes = FontAttributes.Bold,
            TextColor = PlayerTextColor,
            BackgroundColor = Colors.Transparent,
            WidthRequest = 44,
            HeightRequest = 44,
            Padding = 0
        };

    private static Grid BuildDownCaretButton()
    {
        var button = new Grid
        {
            WidthRequest = 44,
            HeightRequest = 44,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };

        button.Children.Add(new GraphicsView
        {
            Drawable = new DownCaretDrawable(),
            WidthRequest = 26,
            HeightRequest = 26,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            InputTransparent = true
        });

        return button;
    }

    private View BuildCoverArt(MobileStoryDetailResponse detail)
    {
        var favoriteButton = BuildFavoriteOverlay(detail);
        var fullscreenButton = BuildFullscreenCoverButton();
        var fullscreenTap = new TapGestureRecognizer();
        fullscreenTap.Tapped += async (_, _) => await ShowFullscreenCoverAsync(detail);
        fullscreenButton.GestureRecognizers.Add(fullscreenTap);

        return new Border
        {
            HeightRequest = CoverArtHeight,
            BackgroundColor = Color.FromArgb("#0C211F"),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 16 },
            Shadow = new Shadow
            {
                Brush = Brush.Black,
                Offset = new Point(0, 18),
                Radius = 32,
                Opacity = 0.22f
            },
            Content = new Grid
            {
                Children =
                {
                    new Image
                    {
                        Source = _apiClient.BuildImageUrl(detail.Story.ImageUrl),
                        Aspect = Aspect.AspectFill
                    },
                    favoriteButton,
                    fullscreenButton
                }
            }
        };
    }

    private View BuildFavoriteOverlay(MobileStoryDetailResponse detail)
    {
        var heart = new Label
        {
            Text = detail.Story.IsFavorite ? "♥" : "♡",
            TextColor = detail.Story.IsFavorite ? Color.FromArgb("#FFE6EF") : Colors.White,
            FontSize = 25,
            FontAttributes = FontAttributes.Bold,
            Shadow = new Shadow
            {
                Brush = Brush.Black,
                Offset = new Point(0, 2),
                Radius = 7,
                Opacity = 0.88f
            },
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center
        };

        var indicator = new ActivityIndicator
        {
            IsRunning = true,
            Color = detail.Story.IsFavorite ? Color.FromArgb("#FFE6EF") : Colors.White,
            WidthRequest = 24,
            HeightRequest = 24,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };

        var target = new Grid
        {
            WidthRequest = 44,
            HeightRequest = 44,
            Margin = new Thickness(0, 6, 6, 0),
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Start,
            Children =
            {
                _isFavoriteRequestInFlight ? indicator : heart
            }
        };

        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) => await ToggleFavoriteAsync(detail);
        target.GestureRecognizers.Add(tap);
        return target;
    }

    private static Grid BuildFullscreenCoverButton() =>
        new()
        {
            WidthRequest = 48,
            HeightRequest = 48,
            Margin = new Thickness(0, 0, 12, 12),
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.End,
            Children =
            {
                new Border
                {
                    BackgroundColor = Color.FromArgb("#B0061816"),
                    Stroke = Color.FromArgb("#44FFFFFF"),
                    StrokeThickness = 1,
                    StrokeShape = new RoundRectangle { CornerRadius = 24 },
                    Content = new GraphicsView
                    {
                        Drawable = new FullscreenIconDrawable(),
                        WidthRequest = 22,
                        HeightRequest = 22,
                        HorizontalOptions = LayoutOptions.Center,
                        VerticalOptions = LayoutOptions.Center,
                        InputTransparent = true
                    }
                }
            }
        };

    private async Task ShowFullscreenCoverAsync(MobileStoryDetailResponse detail)
    {
        var closeButton = BuildFullscreenCloseButton();
        var closeTap = new TapGestureRecognizer();
        closeTap.Tapped += async (_, _) =>
        {
            await Navigation.PopModalAsync(true);
        };
        closeButton.GestureRecognizers.Add(closeTap);

        var fullscreenImage = new Image
        {
            Source = _apiClient.BuildImageUrl(detail.Story.ImageUrl),
            Aspect = Aspect.AspectFit,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill
        };
        if (!detail.RequiresSubscription && !string.IsNullOrWhiteSpace(detail.AudioUrl))
        {
            var fullscreenImageTap = new TapGestureRecognizer();
            fullscreenImageTap.Tapped += (_, _) => _ = ToggleFullscreenPlaybackAsync(detail);
            fullscreenImage.GestureRecognizers.Add(fullscreenImageTap);
        }

        var fullscreenPage = new ContentPage
        {
            BackgroundColor = Colors.Black,
            Content = new Grid
            {
                Padding = new Thickness(8),
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Auto)
                },
                Children =
                {
                    fullscreenImage,
                    closeButton,
                    BuildFullscreenMediaControls(detail)
                }
            }
        };
        Grid.SetColumn(closeButton, 1);
        Shell.SetNavBarIsVisible(fullscreenPage, false);
        fullscreenPage.Disappearing += (_, _) =>
        {
            RestoreFullscreenCoverDeviceState();
            RestoreFullscreenPlaybackUi(detail);
        };

        _wasKeepScreenOnBeforeFullscreen = DeviceDisplay.Current.KeepScreenOn;
        DeviceDisplay.Current.KeepScreenOn = true;
        _orientationService.RequestLandscape();
        _isShowingFullscreenCover = true;
        try
        {
            await Navigation.PushModalAsync(fullscreenPage, true);
        }
        catch
        {
            RestoreFullscreenCoverDeviceState();
            throw;
        }
    }

    private void RestoreFullscreenCoverDeviceState()
    {
        if (!_isShowingFullscreenCover)
        {
            return;
        }

        _isShowingFullscreenCover = false;
        _orientationService.RequestPortrait();
        DeviceDisplay.Current.KeepScreenOn = _wasKeepScreenOnBeforeFullscreen;
    }

    private View BuildFullscreenMediaControls(MobileStoryDetailResponse detail)
    {
        if (detail.RequiresSubscription || string.IsNullOrWhiteSpace(detail.AudioUrl))
        {
            return new Grid { InputTransparent = true };
        }

        var progressBar = new ProgressBar
        {
            Progress = _activeProgressBar?.Progress ?? 0,
            ProgressColor = PlayerAccentColor,
            BackgroundColor = Color.FromArgb("#4D5A57"),
            HeightRequest = 4
        };
        var currentTimeLabel = BuildTimeLabel(
            _activeCurrentTimeLabel?.Text ?? "0:00",
            TextAlignment.Start);
        var durationLabel = BuildTimeLabel(
            _activeDurationLabel?.Text ??
            (_activeCatalogDuration is null ? "--:--" : FormatTime(_activeCatalogDuration.Value)),
            TextAlignment.End);
        var playButton = BuildMainPlaybackButton(_audioPlaybackService.IsPlaying ? "II" : "▶");

        _activePlayButton = playButton;
        _activeProgressBar = progressBar;
        _activeCurrentTimeLabel = currentTimeLabel;
        _activeDurationLabel = durationLabel;

        playButton.Clicked += (_, _) => _ = ToggleFullscreenPlaybackAsync(detail);

        return new Grid
        {
            Padding = new Thickness(20, 0, 20, 12),
            VerticalOptions = LayoutOptions.End,
            Children =
            {
                new Border
                {
                    BackgroundColor = Color.FromArgb("#8A061816"),
                    StrokeThickness = 0,
                    StrokeShape = new RoundRectangle { CornerRadius = 22 },
                    Padding = new Thickness(14, 10, 14, 12),
                    Content = new VerticalStackLayout
                    {
                        Spacing = 8,
                        Children =
                        {
                            progressBar,
                            new Grid
                            {
                                ColumnDefinitions =
                                {
                                    new ColumnDefinition(GridLength.Star),
                                    new ColumnDefinition(GridLength.Star)
                                },
                                Children =
                                {
                                    currentTimeLabel,
                                    durationLabel
                                }
                            },
                            BuildFullscreenTransportControls(detail, playButton)
                        }
                    }
                }
            }
        };
    }

    private async Task ToggleFullscreenPlaybackAsync(MobileStoryDetailResponse detail)
    {
        if (_audioPlaybackService.IsPlaying)
        {
            PausePlayback(_activePlayButton ?? BuildMainPlaybackButton("▶"));
            return;
        }

        if (string.IsNullOrWhiteSpace(detail.AudioUrl))
        {
            return;
        }

        await StartPlaybackAsync(detail, _activePlayButton ?? BuildMainPlaybackButton("▶"));
    }

    private void RestoreFullscreenPlaybackUi(MobileStoryDetailResponse detail)
    {
        if (_inlinePlayButton is not null)
        {
            _activePlayButton = _inlinePlayButton;
        }

        if (_inlineProgressBar is not null)
        {
            _activeProgressBar = _inlineProgressBar;
        }

        if (_inlineCurrentTimeLabel is not null)
        {
            _activeCurrentTimeLabel = _inlineCurrentTimeLabel;
        }

        if (_inlineDurationLabel is not null)
        {
            _activeDurationLabel = _inlineDurationLabel;
        }

        if (_audioPlaybackService.IsPlaying)
        {
            StartProgressTimer();
        }
        else
        {
            UpdateProgressState();
        }
    }

    private static Grid BuildFullscreenCloseButton()
    {
        var button = new Grid
        {
            WidthRequest = 48,
            HeightRequest = 48,
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Start
        };

        button.Children.Add(new Border
        {
            BackgroundColor = Color.FromArgb("#B0061816"),
            Stroke = Color.FromArgb("#44FFFFFF"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 24 },
            Content = new Label
            {
                Text = "×",
                FontSize = 30,
                TextColor = Colors.White,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, -3, 0, 0)
            }
        });

        return button;
    }

    private static double CoverArtHeight
    {
        get
        {
            var height = ScreenHeight;
            if (height <= 0)
            {
                return 310;
            }

            return Math.Clamp(height * 0.36, 260, 330);
        }
    }

    private static double ScreenHeight
    {
        get
        {
            var display = DeviceDisplay.MainDisplayInfo;
            return display.Density <= 0 ? 0 : display.Height / display.Density;
        }
    }

    private static bool IsNativeRoutePickerAvailable =>
        DeviceInfo.Platform == DevicePlatform.iOS ||
        DeviceInfo.Platform == DevicePlatform.MacCatalyst;

    private static View BuildInlineLoadingState() =>
        new HorizontalStackLayout
        {
            Spacing = 10,
            HorizontalOptions = LayoutOptions.Center,
            Children =
            {
                new ActivityIndicator
                {
                    IsRunning = true,
                    Color = PlayerMutedTextColor,
                    WidthRequest = 18,
                    HeightRequest = 18
                },
                new Label
                {
                    Text = "Laai storie...",
                    FontSize = 14,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = PlayerMutedTextColor,
                    VerticalTextAlignment = TextAlignment.Center
                }
            }
        };

    private View BuildStoryHeader(MobileStoryDetailResponse detail)
    {
        var titleRow = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 8
        };
        titleRow.Children.Add(new Label
        {
            Text = detail.Story.Title,
            FontSize = 28,
            FontAttributes = FontAttributes.Bold,
            TextColor = PlayerTextColor,
            LineBreakMode = LineBreakMode.TailTruncation,
            MaxLines = 1
        });
        var titleCaret = new Label
        {
            Text = "›",
            FontSize = 34,
            TextColor = PlayerMutedTextColor,
            VerticalTextAlignment = TextAlignment.Center
        };
        titleRow.Children.Add(titleCaret);
        Grid.SetColumn(titleCaret, 1);
        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) => await DisplayAlertAsync(detail.Story.Title, detail.Story.Description, "Maak toe");
        titleRow.GestureRecognizers.Add(tap);

        return
        new VerticalStackLayout
        {
            Spacing = 2,
            Children =
            {
                titleRow,
                new Label
                {
                    Text = detail.Story.Source.Equals("gratis", StringComparison.OrdinalIgnoreCase)
                        ? "GRATIS STORIE"
                        : "SCHINK STORIES",
                    FontSize = 15,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = PlayerMutedTextColor,
                    CharacterSpacing = 1
                }
            }
        };
    }

    private View BuildActionRail(MobileStoryDetailResponse detail)
    {
        var infoButton = BuildInfoPillButton();
        var infoTap = new TapGestureRecognizer();
        infoTap.Tapped += async (_, _) => await DisplayAlertAsync(detail.Story.Title, detail.Story.Description, "Maak toe");
        infoButton.GestureRecognizers.Add(infoTap);
        var downloadButton = BuildDownloadPillButton(detail);

        return new ScrollView
        {
            Orientation = ScrollOrientation.Horizontal,
            HeightRequest = 42,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Never,
            Content = new HorizontalStackLayout
            {
                Spacing = 10,
                HeightRequest = 42,
                Children =
                {
                    infoButton,
                    downloadButton
                }
            }
        };
    }

    private Border BuildDownloadPillButton(MobileStoryDetailResponse detail)
    {
        var iconView = new GraphicsView
        {
            Drawable = new DownloadIconDrawable(),
            WidthRequest = 17,
            HeightRequest = 17,
            VerticalOptions = LayoutOptions.Center,
            InputTransparent = true
        };
        var textLabel = new Label
        {
            Text = "Laai af",
            AutomationId = "Download for offline listening",
            FontSize = 15,
            FontAttributes = FontAttributes.Bold,
            TextColor = PlayerTextColor,
            VerticalTextAlignment = TextAlignment.Center
        };
        var button = new Border
        {
            BackgroundColor = PlayerPillColor,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 24 },
            HeightRequest = 42,
            Padding = new Thickness(16, 0),
            Content = new HorizontalStackLayout
            {
                Spacing = 8,
                VerticalOptions = LayoutOptions.Center,
                Children =
                {
                    iconView,
                    textLabel
                }
            }
        };

        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) => await HandleDownloadButtonAsync(detail, button, textLabel, iconView);
        button.GestureRecognizers.Add(tap);
        _ = UpdateDownloadButtonAsync(detail, button, textLabel, iconView);
        return button;
    }

    private async Task UpdateDownloadButtonAsync(MobileStoryDetailResponse detail, Border button, Label textLabel, GraphicsView iconView)
    {
        var state = await _offlineDownloadService.GetStateAsync(detail);
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            button.IsEnabled = !detail.RequiresSubscription && state != OfflineDownloadState.Downloading;
            button.Opacity = button.IsEnabled ? 1 : 0.52;
            iconView.Drawable = state == OfflineDownloadState.Downloaded
                ? new DownloadedIconDrawable()
                : new DownloadIconDrawable();
            iconView.Invalidate();
            textLabel.Text = state switch
            {
                OfflineDownloadState.Downloading => "Laai af...",
                OfflineDownloadState.Downloaded => "Afgelaai",
                OfflineDownloadState.ExpiredAccess => "Herbevestig",
                OfflineDownloadState.Failed => "Probeer weer",
                _ => "Laai af"
            };
        });
    }

    private async Task HandleDownloadButtonAsync(MobileStoryDetailResponse detail, Border button, Label textLabel, GraphicsView iconView)
    {
        if (!button.IsEnabled)
        {
            return;
        }

        if (detail.RequiresSubscription || string.IsNullOrWhiteSpace(detail.AudioUrl))
        {
            await DisplayAlertAsync("Nie beskikbaar nie", "Hierdie storie kan nie tans afgelaai word nie.", "Reg so");
            return;
        }

        var state = await _offlineDownloadService.GetStateAsync(detail);
        if (state == OfflineDownloadState.Downloaded)
        {
            var action = await DisplayActionSheetAsync("Afgelaai", "Kanselleer", null, "Verwyder aflaai");
            if (string.Equals(action, "Verwyder aflaai", StringComparison.Ordinal))
            {
                await _offlineDownloadService.RemoveAsync(detail.Story.Slug, detail.Story.Source);
                await UpdateDownloadButtonAsync(detail, button, textLabel, iconView);
            }

            return;
        }

        if (state == OfflineDownloadState.ExpiredAccess)
        {
            await DisplayAlertAsync(
                "Aanlyn bevestiging nodig",
                "Hierdie aflaai moet weer aanlyn bevestig word.",
                "Reg so");
            return;
        }

        if (!await ConfirmCellularDownloadAsync())
        {
            return;
        }

        button.IsEnabled = false;
        button.Opacity = 0.52;
        textLabel.Text = "Laai af...";
        iconView.Drawable = new DownloadIconDrawable();
        iconView.Invalidate();
        try
        {
            await _offlineDownloadService.DownloadAsync(
                detail,
                new Progress<OfflineDownloadProgress>(progress =>
                {
                    if (progress.Percent is { } percent)
                    {
                        MainThread.BeginInvokeOnMainThread(() =>
                            textLabel.Text = $"{Math.Round(percent * 100):0}%");
                    }
                }));
            textLabel.Text = "Afgelaai";
            iconView.Drawable = new DownloadedIconDrawable();
            iconView.Invalidate();
            RenderDetail(detail, trackView: false);
        }
        catch (Exception ex)
        {
            textLabel.Text = "Probeer weer";
            await DisplayAlertAsync("Kon nie aflaai nie", ex.Message, "Reg so");
        }
        finally
        {
            button.IsEnabled = true;
            button.Opacity = 1;
        }
    }

    private async Task<bool> ConfirmCellularDownloadAsync()
    {
        if (!Connectivity.Current.ConnectionProfiles.Contains(ConnectionProfile.Cellular))
        {
            return true;
        }

        return await DisplayAlertAsync(
            "Mobiele data",
            "Hierdie aflaai kan mobiele data gebruik. Wil jy voortgaan?",
            "Laai af",
            "Kanselleer");
    }

    private async Task ToggleFavoriteAsync(MobileStoryDetailResponse detail)
    {
        if (!_sessionState.Current.IsSignedIn)
        {
            await DisplayAlertAsync("Teken in", "Teken eers in om gunstelinge te stoor.", "Reg so");
            return;
        }

        if (_isFavoriteRequestInFlight)
        {
            return;
        }

        _isFavoriteRequestInFlight = true;
        RenderFavoriteState(detail, detail.Story.IsFavorite);
        try
        {
            var isFavorite = await _apiClient.SetFavoriteAsync(detail.Story.Slug, detail.Story.Source, !detail.Story.IsFavorite);
            RenderFavoriteState(detail, isFavorite);
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Kon nie stoor nie", ex.Message, "Reg so");
        }
        finally
        {
            _isFavoriteRequestInFlight = false;
            if (_currentDetail is not null)
            {
                RenderFavoriteState(_currentDetail, _currentDetail.Story.IsFavorite);
            }
        }
    }

    private void RenderFavoriteState(MobileStoryDetailResponse detail, bool isFavorite)
    {
        var updatedStory = detail.Story with { IsFavorite = isFavorite };
        _currentDetail = detail with { Story = updatedStory };
        _previewStory = updatedStory;
        _activeStory = updatedStory;
        RenderDetail(_currentDetail, trackView: false);
    }

    private static Border BuildInfoPillButton() =>
        new()
        {
            BackgroundColor = PlayerPillColor,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 24 },
            HeightRequest = 42,
            Padding = new Thickness(16, 0),
            Content = new HorizontalStackLayout
            {
                Spacing = 8,
                VerticalOptions = LayoutOptions.Center,
                Children =
                {
                    new GraphicsView
                    {
                        Drawable = new InfoIconDrawable(),
                        WidthRequest = 16,
                        HeightRequest = 16,
                        VerticalOptions = LayoutOptions.Center,
                        InputTransparent = true
                    },
                    new Label
                    {
                        Text = "Info",
                        FontSize = 15,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = PlayerTextColor,
                        VerticalTextAlignment = TextAlignment.Center
                    }
                }
            }
        };

    private View BuildLockedPanel(MobileStoryDetailResponse detail)
    {
        var loginButton = BuildPrimaryButton("Teken in");
        loginButton.Clicked += async (_, _) => await Browser.OpenAsync(detail.LoginUrl, BrowserLaunchMode.External);

        var plansButton = BuildSecondaryButton("Sien planne");
        plansButton.Clicked += async (_, _) => await Browser.OpenAsync(detail.PlansUrl, BrowserLaunchMode.External);

        return new Border
        {
            BackgroundColor = PlayerPanelColor,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 28 },
            Padding = 20,
            Content = new VerticalStackLayout
            {
                Spacing = 14,
                Children =
                {
                    new Label
                    {
                        Text = "Hierdie storie is nog gesluit.",
                        FontSize = 20,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = PlayerTextColor
                    },
                    new Label
                    {
                        Text = "Teken in of kies 'n plan om hierdie storie op die app oop te maak.",
                        FontSize = 15,
                        TextColor = PlayerMutedTextColor
                    },
                    new HorizontalStackLayout
                    {
                        Spacing = 10,
                        Children = { loginButton, plansButton }
                    }
                }
            }
        };
    }

    private View BuildAudioPlayer(MobileStoryDetailResponse detail)
    {
        var progressBar = new ProgressBar
        {
            Progress = 0,
            ProgressColor = PlayerAccentColor,
            BackgroundColor = Color.FromArgb("#3D4B48"),
            HeightRequest = 4
        };
        var currentTimeLabel = BuildTimeLabel("0:00", TextAlignment.Start);
        _activeCatalogDuration = ResolveCatalogDuration(detail);
        var durationLabel = BuildTimeLabel(
            _activeCatalogDuration is null ? "--:--" : FormatTime(_activeCatalogDuration.Value),
            TextAlignment.End);
        var playButton = BuildMainPlaybackButton(_audioPlaybackService.IsPlaying ? "II" : "▶");

        _activePlayButton = playButton;
        _activeProgressBar = progressBar;
        _activeCurrentTimeLabel = currentTimeLabel;
        _activeDurationLabel = durationLabel;
        _inlinePlayButton = playButton;
        _inlineProgressBar = progressBar;
        _inlineCurrentTimeLabel = currentTimeLabel;
        _inlineDurationLabel = durationLabel;

        playButton.Clicked += async (_, _) => await TogglePlaybackAsync(detail, playButton);

        return new VerticalStackLayout
        {
            Spacing = 12,
            Children =
            {
                new VerticalStackLayout
                {
                    Spacing = 8,
                    Children =
                    {
                        progressBar,
                        new Grid
                        {
                            ColumnDefinitions =
                            {
                                new ColumnDefinition(GridLength.Star),
                                new ColumnDefinition(GridLength.Star)
                            },
                            Children =
                            {
                                currentTimeLabel,
                                durationLabel
                            }
                        }
                    }
                },
                BuildPlaybackModeRow(detail),
                BuildTransportControls(detail, playButton)
            }
        };
    }

    private async Task PlayPreparedAudioAsync(
        string playbackUrl,
        MobileStoryDetailResponse detail,
        Guid trackingSessionId,
        Button playButton)
    {
        await _audioPlaybackService.PlayAsync(
            playbackUrl,
            new AudioPlaybackMetadata(
                detail.Story.Title,
                "Schink Stories",
                _apiClient.BuildImageUrl(detail.Story.ImageUrl)));
        playButton.Text = "II";
        BeginListenTracking(detail, trackingSessionId);
        StartProgressTimer();
    }

    private async Task TogglePlaybackAsync(MobileStoryDetailResponse detail, Button playButton)
    {
        if (_audioPlaybackService.IsPlaying)
        {
            PausePlayback(playButton);
            return;
        }

        await StartPlaybackAsync(detail, playButton);
    }

    private void PausePlayback(Button playButton)
    {
        FlushPendingListen("pause", force: true);
        _suppressNextPauseTracking = true;
        _audioPlaybackService.Pause();
        playButton.Text = "▶";
        StopProgressTimer();
    }

    private async Task StartPlaybackAsync(MobileStoryDetailResponse detail, Button playButton)
    {
        try
        {
            var offlinePlaybackUrl = await _offlineDownloadService.ResolvePlayableAudioAsync(detail);
            if (!string.IsNullOrWhiteSpace(offlinePlaybackUrl))
            {
                await PlayPreparedAudioAsync(offlinePlaybackUrl, detail, ResolveTrackingSessionId(detail), playButton);
                return;
            }

            if (await _offlineDownloadService.GetStateAsync(detail) == OfflineDownloadState.ExpiredAccess)
            {
                await DisplayAlertAsync(
                    "Aanlyn bevestiging nodig",
                    "Hierdie aflaai moet weer aanlyn bevestig word.",
                    "Reg so");
            }

            var playbackUrl = await _apiClient.PrepareAudioPlaybackSourceAsync(
                detail.AudioUrl,
                detail.Story.Slug,
                detail.Story.Source);
            await PlayPreparedAudioAsync(playbackUrl, detail, ResolveTrackingSessionId(detail), playButton);
        }
        catch (Exception) when (IsR2AudioUrl(detail.AudioUrl))
        {
            try
            {
                var cachedPlaybackUrl = await _apiClient.DownloadAudioForPlaybackAsync(
                    detail.AudioUrl ?? string.Empty,
                    detail.Story.Slug,
                    detail.Story.Source);
                await PlayPreparedAudioAsync(cachedPlaybackUrl, detail, ResolveTrackingSessionId(detail), playButton);
            }
            catch (Exception ex)
            {
                playButton.Text = "▶";
                StopProgressTimer();
                await DisplayAlertAsync("Kon nie audio speel nie", ex.Message, "Maak toe");
            }
        }
        catch (Exception ex)
        {
            playButton.Text = "▶";
            StopProgressTimer();
            await DisplayAlertAsync("Kon nie audio speel nie", ex.Message, "Maak toe");
        }
    }

    private void TryStartPendingAutoplay(MobileStoryDetailResponse detail)
    {
        if (!_pendingAutoplayAfterLoad)
        {
            return;
        }

        _pendingAutoplayAfterLoad = false;
        if (detail.RequiresSubscription || string.IsNullOrWhiteSpace(detail.AudioUrl) || _activePlayButton is null)
        {
            return;
        }

        _ = StartPlaybackAsync(detail, _activePlayButton);
    }

    private View BuildPlaybackModeRow(MobileStoryDetailResponse detail)
    {
        var nextStoryAvailable = ResolveNextStory(detail) is not null;
        var canShuffle = GetOrderedPlaylistStories(detail.Story).Count > 1;
        var speedButton = BuildPlaybackModeButton(
            FormatPlaybackSpeed(_audioPlaybackService.PlaybackSpeed),
            false,
            true,
            () => CyclePlaybackSpeed(detail));
        var autoplayButton = BuildPlaybackModeButton(
            "Auto",
            _playlistPlaybackState.IsAutoplayEnabled,
            nextStoryAvailable,
            () => ToggleAutoplay(detail));
        var autoplayLimitButton = BuildPlaybackModeButton(
            FormatAutoplayLimit(),
            _playlistPlaybackState.AutoplayLimitStories.HasValue,
            _playlistPlaybackState.IsAutoplayEnabled && nextStoryAvailable,
            () => CycleAutoplayLimit(detail));
        var shuffleButton = BuildPlaybackModeButton(
            "Skommel",
            _playlistPlaybackState.IsShuffleEnabled,
            canShuffle,
            () => ToggleShuffle(detail));

        return new ScrollView
        {
            Orientation = ScrollOrientation.Horizontal,
            HorizontalOptions = LayoutOptions.Center,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Never,
            Content = new HorizontalStackLayout
            {
                Spacing = 8,
                HorizontalOptions = LayoutOptions.Center,
                Children =
                {
                    speedButton,
                    autoplayButton,
                    autoplayLimitButton,
                    shuffleButton
                }
            }
        };
    }

    private void CyclePlaybackSpeed(MobileStoryDetailResponse detail)
    {
        var currentSpeed = _audioPlaybackService.PlaybackSpeed;
        var currentIndex = Array.FindIndex(PlaybackSpeedSteps, speed => Math.Abs(speed - currentSpeed) < 0.001);
        var nextIndex = currentIndex < 0 ? 1 : (currentIndex + 1) % PlaybackSpeedSteps.Length;
        _audioPlaybackService.SetPlaybackSpeed(PlaybackSpeedSteps[nextIndex]);
        RenderDetail(detail, trackView: false);
    }

    private void ToggleAutoplay(MobileStoryDetailResponse detail)
    {
        _playlistPlaybackState.SetAutoplay(!_playlistPlaybackState.IsAutoplayEnabled);
        _playlistPlaybackState.TrackManualStorySelection(detail.Story);
        RenderDetail(detail, trackView: false);
    }

    private void CycleAutoplayLimit(MobileStoryDetailResponse detail)
    {
        int? nextLimit = _playlistPlaybackState.AutoplayLimitStories switch
        {
            null => 3,
            3 => 5,
            _ => null
        };
        _playlistPlaybackState.SetAutoplayLimit(nextLimit, detail.Story);
        RenderDetail(detail, trackView: false);
    }

    private void ToggleShuffle(MobileStoryDetailResponse detail)
    {
        _playlistPlaybackState.SetShuffle(!_playlistPlaybackState.IsShuffleEnabled, detail.Story);
        _playlistStories = GetOrderedPlaylistStories(detail.Story);
        RenderDetail(detail, trackView: false);
    }

    private string FormatAutoplayLimit() =>
        _playlistPlaybackState.AutoplayLimitStories is { } limit
            ? $"Stop {limit}"
            : "Geen limiet";

    private static string FormatPlaybackSpeed(double speed) =>
        $"{speed:0.##}x";

    private bool ShouldAutoplaySelection() =>
        _audioPlaybackService.IsPlaying || _playlistPlaybackState.IsAutoplayEnabled;

    private View BuildTransportControls(MobileStoryDetailResponse detail, Button playButton)
    {
        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star)
            },
            HeightRequest = 82
        };

        var previousStory = ResolvePreviousStory(detail);
        var nextStory = ResolveNextStory(detail);
        var previousButton = BuildTransportButton("|‹");
        var nextButton = BuildTransportButton("›|");

        previousButton.IsEnabled = previousStory is not null;
        nextButton.IsEnabled = nextStory is not null;
        previousButton.Clicked += async (_, _) =>
        {
            if (previousStory is not null)
            {
                await OpenPlaylistStoryAsync(previousStory, autoplay: ShouldAutoplaySelection());
            }
        };
        nextButton.Clicked += async (_, _) =>
        {
            if (nextStory is not null)
            {
                await OpenPlaylistStoryAsync(nextStory, autoplay: ShouldAutoplaySelection());
            }
        };

        grid.Children.Add(previousButton);
        grid.Children.Add(playButton);
        grid.Children.Add(nextButton);
        Grid.SetColumn(playButton, 1);
        Grid.SetColumn(nextButton, 2);

        return grid;
    }

    private View BuildFullscreenTransportControls(MobileStoryDetailResponse detail, Button playButton)
    {
        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star)
            },
            HeightRequest = 54
        };

        var previousStory = ResolvePreviousStory(detail);
        var nextStory = ResolveNextStory(detail);
        var previousButton = BuildCompactTransportButton("|‹");
        var nextButton = BuildCompactTransportButton("›|");
        var compactPlayButton = BuildCompactPlaybackButton(playButton.Text);

        _activePlayButton = compactPlayButton;

        previousButton.IsEnabled = previousStory is not null;
        nextButton.IsEnabled = nextStory is not null;

        previousButton.Clicked += async (_, _) =>
        {
            if (previousStory is not null)
            {
                await ReplaceActiveStoryAsync(previousStory, autoplay: ShouldAutoplaySelection());
            }
        };

        compactPlayButton.Clicked += (_, _) => _ = ToggleFullscreenPlaybackAsync(detail);

        nextButton.Clicked += async (_, _) =>
        {
            if (nextStory is not null)
            {
                await ReplaceActiveStoryAsync(nextStory, autoplay: ShouldAutoplaySelection());
            }
        };

        grid.Children.Add(previousButton);
        grid.Children.Add(compactPlayButton);
        grid.Children.Add(nextButton);
        Grid.SetColumn(compactPlayButton, 1);
        Grid.SetColumn(nextButton, 2);

        return grid;
    }

    private MobileStorySummary? ResolvePreviousStory(MobileStoryDetailResponse detail)
    {
        var playlistStories = GetOrderedPlaylistStories(detail.Story);
        var playlistIndex = FindCurrentPlaylistIndex(playlistStories, detail.Story);
        if (playlistIndex > 0)
        {
            return playlistStories[playlistIndex - 1];
        }

        return detail.PreviousStory;
    }

    private MobileStorySummary? ResolveNextStory(MobileStoryDetailResponse detail)
    {
        var playlistStories = GetOrderedPlaylistStories(detail.Story);
        var playlistIndex = FindCurrentPlaylistIndex(playlistStories, detail.Story);
        if (playlistIndex >= 0 && playlistIndex < playlistStories.Count - 1)
        {
            return playlistStories[playlistIndex + 1];
        }

        return detail.NextStory;
    }

    private IReadOnlyList<MobileStorySummary> GetOrderedPlaylistStories(MobileStorySummary? currentStory = null)
    {
        if (!string.IsNullOrWhiteSpace(_playlistSlug) &&
            string.Equals(_playlistPlaybackState.Slug, _playlistSlug, StringComparison.OrdinalIgnoreCase))
        {
            return _playlistPlaybackState.GetPlaybackStories(currentStory);
        }

        return _playlistStories;
    }

    private static int FindCurrentPlaylistIndex(IReadOnlyList<MobileStorySummary> playlistStories, MobileStorySummary story)
    {
        for (var index = 0; index < playlistStories.Count; index++)
        {
            if (string.Equals(playlistStories[index].Slug, story.Slug, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(playlistStories[index].Source, story.Source, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    private View? BuildPlaylistQueue(MobileStoryDetailResponse detail)
    {
        var queuedStories = GetOrderedPlaylistStories(detail.Story)
            .Where(story => !string.Equals(story.Slug, detail.Story.Slug, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (queuedStories.Length == 0)
        {
            return null;
        }

        var list = new VerticalStackLayout
        {
            Spacing = 10
        };
        list.Children.Add(new Label
        {
            Text = string.IsNullOrWhiteSpace(_playlistTitle) ? "Volgende stories" : $"Volgende in {_playlistTitle}",
            FontSize = 18,
            FontAttributes = FontAttributes.Bold,
            TextColor = PlayerTextColor
        });

        foreach (var story in queuedStories)
        {
            list.Children.Add(BuildPlaylistQueueRow(story));
        }

        return new VerticalStackLayout
        {
            Spacing = 12,
            Padding = new Thickness(0, 4, 0, 0),
            Children = { list }
        };
    }

    private View BuildPlaylistQueueRow(MobileStorySummary story)
    {
        var playButton = new Button
        {
            Text = "▶",
            FontSize = 18,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#061816"),
            BackgroundColor = PlayerAccentColor,
            CornerRadius = 24,
            WidthRequest = 48,
            HeightRequest = 48,
            Padding = 0,
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Center
        };
        playButton.Clicked += async (_, _) => await OpenPlaylistStoryAsync(story, autoplay: ShouldAutoplaySelection());

        var row = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 12,
            Padding = new Thickness(10),
            BackgroundColor = PlayerPanelColor
        };

        var imageSource = string.IsNullOrWhiteSpace(story.ThumbnailUrl) ? story.ImageUrl : story.ThumbnailUrl;
        row.Children.Add(new Border
        {
            WidthRequest = 68,
            HeightRequest = 68,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 14 },
            Content = new Image
            {
                Source = _apiClient.BuildImageUrl(imageSource),
                Aspect = Aspect.AspectFill
            }
        });

        var textStack = new VerticalStackLayout
        {
            Spacing = 3,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Label
                {
                    Text = story.Title,
                    FontSize = 15,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = PlayerTextColor,
                    MaxLines = 2,
                    LineBreakMode = LineBreakMode.TailTruncation
                },
                new Label
                {
                    Text = ToTimeSpan(story.DurationSeconds) is { } duration ? FormatTime(duration) : "Storie",
                    FontSize = 13,
                    TextColor = PlayerMutedTextColor
                }
            }
        };
        row.Children.Add(textStack);
        row.Children.Add(playButton);
        Grid.SetColumn(textStack, 1);
        Grid.SetColumn(playButton, 2);

        var frame = new Border
        {
            BackgroundColor = PlayerPanelColor,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 18 },
            Content = row
        };

        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) => await OpenPlaylistStoryAsync(story, autoplay: ShouldAutoplaySelection());
        frame.GestureRecognizers.Add(tap);

        return frame;
    }

    private static Label BuildTimeLabel(string text, TextAlignment alignment)
    {
        var label = new Label
        {
            Text = text,
            FontSize = 14,
            TextColor = PlayerMutedTextColor,
            HorizontalTextAlignment = alignment
        };

        if (alignment == TextAlignment.End)
        {
            Grid.SetColumn(label, 1);
        }

        return label;
    }

    private static Button BuildTransportButton(string text) =>
        new()
        {
            Text = text,
            FontSize = 34,
            FontAttributes = FontAttributes.Bold,
            TextColor = PlayerTextColor,
            BackgroundColor = Colors.Transparent,
            WidthRequest = 56,
            HeightRequest = 68,
            Padding = 0,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };

    private static Button BuildMainPlaybackButton(string text) =>
        new()
        {
            Text = text,
            FontSize = 32,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#061816"),
            BackgroundColor = PlayerAccentColor,
            CornerRadius = 38,
            WidthRequest = 76,
            HeightRequest = 76,
            Padding = 0,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };

    private static Button BuildCompactTransportButton(string text) =>
        new()
        {
            Text = text,
            FontSize = 24,
            FontAttributes = FontAttributes.Bold,
            TextColor = PlayerTextColor,
            BackgroundColor = Colors.Transparent,
            WidthRequest = 44,
            HeightRequest = 44,
            Padding = 0,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.End
        };

    private static Button BuildCompactPlaybackButton(string text) =>
        new()
        {
            Text = text,
            FontSize = 22,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#061816"),
            BackgroundColor = PlayerAccentColor,
            CornerRadius = 26,
            WidthRequest = 52,
            HeightRequest = 52,
            Padding = 0,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.End
        };

    private static Button BuildPrimaryButton(string text) =>
        new()
        {
            Text = text,
            FontSize = 15,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#061816"),
            BackgroundColor = PlayerAccentColor,
            CornerRadius = 20,
            HeightRequest = 42,
            Padding = new Thickness(18, 0)
        };

    private static Button BuildSecondaryButton(string text) =>
        new()
        {
            Text = text,
            FontSize = 15,
            FontAttributes = FontAttributes.Bold,
            TextColor = PlayerTextColor,
            BackgroundColor = PlayerPillColor,
            CornerRadius = 20,
            HeightRequest = 42,
            Padding = new Thickness(18, 0)
        };

    private static Border BuildPlaybackModeButton(string text, bool isSelected, bool isEnabled, Action onTap)
    {
        var backgroundColor = isSelected ? PlayerAccentColor : PlayerPillColor;
        var textColor = isSelected ? Color.FromArgb("#061816") : PlayerTextColor;
        var button = new Border
        {
            BackgroundColor = isEnabled ? backgroundColor : Color.FromArgb("#142B28"),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 20 },
            HeightRequest = 38,
            Padding = new Thickness(16, 0),
            Opacity = isEnabled ? 1 : 0.5,
            Content = new Label
            {
                Text = text,
                FontSize = 13,
                FontAttributes = FontAttributes.Bold,
                TextColor = textColor,
                VerticalTextAlignment = TextAlignment.Center,
                HorizontalTextAlignment = TextAlignment.Center
            }
        };

        if (isEnabled)
        {
            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) => onTap();
            button.GestureRecognizers.Add(tap);
        }

        return button;
    }

    private View BuildStoryInfoCard(MobileStoryDetailResponse detail)
    {
        var cardContent = new VerticalStackLayout
        {
            Spacing = 18
        };

        cardContent.Children.Add(new Label
        {
            Text = detail.Story.Title,
            FontSize = 32,
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.White,
            LineBreakMode = LineBreakMode.WordWrap,
            HorizontalTextAlignment = TextAlignment.Center
        });

        if (ShouldShowStoryLead(detail))
        {
            cardContent.Children.Add(new Label
            {
                Text = detail.Story.Description,
                FontSize = 16,
                TextColor = StorySummaryLeadColor,
                LineBreakMode = LineBreakMode.WordWrap
            });
        }

        var synopsis = GetStorySynopsis(detail);
        if (!string.IsNullOrWhiteSpace(synopsis))
        {
            cardContent.Children.Add(BuildStoryInfoTextBlock("Waaroor gaan die storie?", synopsis));
        }

        if (detail.ValueTags is { Count: > 0 })
        {
            cardContent.Children.Add(BuildStoryInfoTagBlock("Waardes", detail.ValueTags));
        }

        if (detail.Lessons is { Count: > 0 })
        {
            cardContent.Children.Add(BuildStoryInfoListBlock(detail.Lessons.Count == 1 ? "Les" : "Lesse", detail.Lessons));
        }

        if (detail.ConversationQuestions is { Count: > 0 })
        {
            cardContent.Children.Add(BuildStoryInfoListBlock("Gesels 'n bietjie", detail.ConversationQuestions));
        }

        if (detail.Characters is { Count: > 0 } || detail.CharacterTiles is { Count: > 0 })
        {
            cardContent.Children.Add(BuildStoryCharacterBlock(detail));
        }

        if (!string.IsNullOrWhiteSpace(detail.YouTubeUrl))
        {
            cardContent.Children.Add(BuildStoryInfoActionButton("Kyk saam op YouTube", async () =>
            {
                try
                {
                    await Launcher.OpenAsync(detail.YouTubeUrl);
                }
                catch
                {
                    await DisplayAlertAsync("YouTube", "Kon nie die video oopmaak nie.", "Reg so");
                }
            }));
        }

        if (HasStoryTestQuestions(detail))
        {
            cardContent.Children.Add(BuildStoryInfoActionButton(
                "Storie vragies",
                async () => await ShowStoryTestModalAsync(detail),
                isPrimary: true));
        }

        return new Border
        {
            BackgroundColor = StorySummaryCardColor,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 24 },
            Padding = new Thickness(22, 22, 22, 24),
            Content = cardContent
        };
    }

    private static View BuildStoryInfoTextBlock(string title, string body) =>
        new VerticalStackLayout
        {
            Spacing = 6,
            Children =
            {
                BuildStoryInfoHeading(title),
                new Label
                {
                    Text = body,
                    FontSize = 15,
                    TextColor = StorySummaryTextColor,
                    LineBreakMode = LineBreakMode.WordWrap
                }
            }
        };

    private static View BuildStoryInfoListBlock(string title, IReadOnlyList<string> items)
    {
        var list = new VerticalStackLayout
        {
            Spacing = 8
        };
        list.Children.Add(BuildStoryInfoHeading(title));

        foreach (var item in items.Where(item => !string.IsNullOrWhiteSpace(item)))
        {
            var row = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Star)
                },
                ColumnSpacing = 10
            };

            row.Children.Add(new Label
            {
                Text = "•",
                FontSize = 18,
                TextColor = StorySummaryGoldColor,
                VerticalTextAlignment = TextAlignment.Start
            });

            var textLabel = new Label
            {
                Text = item.Trim(),
                FontSize = 15,
                TextColor = StorySummaryTextColor,
                LineBreakMode = LineBreakMode.WordWrap
            };
            row.Children.Add(textLabel);
            Grid.SetColumn(textLabel, 1);
            list.Children.Add(row);
        }

        return list;
    }

    private static View BuildStoryInfoTagBlock(string title, IReadOnlyList<string> tags)
    {
        var block = new VerticalStackLayout
        {
            Spacing = 10
        };
        block.Children.Add(BuildStoryInfoHeading(title));

        var wrap = new FlexLayout
        {
            Direction = Microsoft.Maui.Layouts.FlexDirection.Row,
            Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap,
            AlignItems = Microsoft.Maui.Layouts.FlexAlignItems.Center,
            JustifyContent = Microsoft.Maui.Layouts.FlexJustify.Center
        };

        foreach (var tag in tags.Where(tag => !string.IsNullOrWhiteSpace(tag)))
        {
            wrap.Children.Add(new Border
            {
                BackgroundColor = StorySummaryPillColor,
                StrokeThickness = 1,
                Stroke = StorySummaryGoldColor.WithAlpha(0.58f),
                StrokeShape = new RoundRectangle { CornerRadius = 999 },
                Padding = new Thickness(12, 7),
                Margin = new Thickness(0, 0, 8, 8),
                Content = new Label
                {
                    Text = tag.Trim(),
                    FontSize = 13,
                    TextColor = StorySummaryTextColor
                }
            });
        }

        block.Children.Add(wrap);
        return block;
    }

    private static Label BuildStoryInfoHeading(string text) =>
        new()
        {
            Text = text,
            FontSize = 17,
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.White
        };

    private static View BuildStoryCharacterBlock(MobileStoryDetailResponse detail)
    {
        var block = new VerticalStackLayout
        {
            Spacing = 12
        };
        block.Children.Add(BuildStoryInfoHeading("Karakters in die storie"));

        var tiles = detail.CharacterTiles is { Count: > 0 }
            ? detail.CharacterTiles
            : detail.Characters
                .Where(character => !string.IsNullOrWhiteSpace(character))
                .Select(character => new MobileStoryCharacter(character.Trim(), null, null, true))
                .ToArray();

        var visualTiles = tiles.Where(character => !character.IsTextOnly).ToArray();
        var textOnlyTiles = tiles.Where(character => character.IsTextOnly).ToArray();

        if (visualTiles.Length > 0)
        {
            var visualWrap = new FlexLayout
            {
                Direction = Microsoft.Maui.Layouts.FlexDirection.Row,
                Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap,
                AlignItems = Microsoft.Maui.Layouts.FlexAlignItems.Start,
                JustifyContent = Microsoft.Maui.Layouts.FlexJustify.Center
            };

            foreach (var character in visualTiles)
            {
                visualWrap.Children.Add(BuildStoryCharacterTile(character));
            }

            block.Children.Add(visualWrap);
        }

        if (textOnlyTiles.Length > 0)
        {
            var textWrap = new FlexLayout
            {
                Direction = Microsoft.Maui.Layouts.FlexDirection.Row,
                Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap,
                AlignItems = Microsoft.Maui.Layouts.FlexAlignItems.Center,
                JustifyContent = Microsoft.Maui.Layouts.FlexJustify.Center
            };

            foreach (var character in textOnlyTiles)
            {
                textWrap.Children.Add(new Border
                {
                    BackgroundColor = StorySummaryPillColor,
                    StrokeThickness = 1,
                    Stroke = StorySummaryGoldColor.WithAlpha(0.58f),
                    StrokeShape = new RoundRectangle { CornerRadius = 999 },
                    Padding = new Thickness(14, 8),
                    Margin = new Thickness(0, 0, 8, 8),
                    Content = new Label
                    {
                        Text = character.DisplayName,
                        FontSize = 14,
                        TextColor = StorySummaryTextColor,
                        HorizontalTextAlignment = TextAlignment.Center
                    }
                });
            }

            block.Children.Add(textWrap);
        }

        return block;
    }

    private static View BuildStoryCharacterTile(MobileStoryCharacter character)
    {
        var stack = new VerticalStackLayout
        {
            WidthRequest = 88,
            Spacing = 6,
            Margin = new Thickness(0, 0, 12, 12),
            HorizontalOptions = LayoutOptions.Center
        };

        stack.Children.Add(new Image
        {
            Source = character.ImageUrl,
            WidthRequest = 64,
            HeightRequest = 64,
            Aspect = Aspect.AspectFit,
            HorizontalOptions = LayoutOptions.Center
        });
        stack.Children.Add(new Label
        {
            Text = character.DisplayName,
            FontSize = 12,
            TextColor = StorySummaryTextColor,
            HorizontalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.WordWrap
        });

        return stack;
    }

    private static View BuildStoryInfoActionButton(string text, Func<Task> onTap, bool isPrimary = false)
    {
        var button = new Border
        {
            BackgroundColor = isPrimary ? StorySummaryTestButtonColor : PlayerAccentColor,
            StrokeThickness = isPrimary ? 1 : 0,
            Stroke = isPrimary ? StorySummaryGoldColor.WithAlpha(0.62f) : Colors.Transparent,
            StrokeShape = new RoundRectangle { CornerRadius = isPrimary ? 14 : 22 },
            HeightRequest = isPrimary ? 48 : 44,
            Padding = new Thickness(16, 0),
            Content = new Label
            {
                Text = text,
                FontSize = 15,
                FontAttributes = FontAttributes.Bold,
                TextColor = isPrimary ? StorySummaryCardColor : Color.FromArgb("#061816"),
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center
            }
        };

        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) => await onTap();
        button.GestureRecognizers.Add(tap);
        return button;
    }

    private async Task ShowStoryTestModalAsync(MobileStoryDetailResponse detail)
    {
        _selectedStoryTestOptions.Clear();
        _isStoryTestSubmitted = false;
        _storyTestDetail = detail;
        _storyTestModalContent = new VerticalStackLayout
        {
            Spacing = 16,
            Padding = new Thickness(18, 18, 18, 28)
        };

        _storyTestModalPage = new ContentPage
        {
            BackgroundColor = PlayerBackgroundColor,
            Content = new ScrollView
            {
                BackgroundColor = PlayerBackgroundColor,
                VerticalScrollBarVisibility = ScrollBarVisibility.Never,
                Content = _storyTestModalContent
            }
        };
        Shell.SetNavBarIsVisible(_storyTestModalPage, false);
        RenderStoryTestModalContent();
        await Navigation.PushModalAsync(_storyTestModalPage, true);
    }

    private void RenderStoryTestModalContent()
    {
        if (_storyTestModalContent is null || _storyTestDetail is not { } detail)
        {
            return;
        }

        _storyTestModalContent.Children.Clear();
        _storyTestModalContent.Children.Add(BuildStoryTestHeader(detail));

        if (_isStoryTestSubmitted)
        {
            _storyTestModalContent.Children.Add(new Border
            {
                BackgroundColor = Color.FromArgb("#173B35"),
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = 20 },
                Padding = 16,
                Content = new Label
                {
                    Text = BuildStoryTestScoreText(detail),
                    FontSize = 17,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = PlayerTextColor,
                    HorizontalTextAlignment = TextAlignment.Center
                }
            });
        }

        for (var index = 0; index < detail.TestQuestions.Count; index++)
        {
            _storyTestModalContent.Children.Add(BuildStoryTestQuestionCard(detail.TestQuestions[index], index));
        }

        if (IsStoryTestReadyToCheck(detail))
        {
            if (!_isStoryTestSubmitted)
            {
                _storyTestModalContent.Children.Add(BuildStoryInfoActionButton("Kontroleer antwoorde", () =>
                {
                    _isStoryTestSubmitted = true;
                    RenderStoryTestModalContent();
                    return Task.CompletedTask;
                }));
            }
        }
        else
        {
            _storyTestModalContent.Children.Add(new Label
            {
                Text = "Antwoord al die vragies om jou telling te sien.",
                FontSize = 14,
                TextColor = PlayerMutedTextColor,
                HorizontalTextAlignment = TextAlignment.Center
            });
        }
    }

    private View BuildStoryTestHeader(MobileStoryDetailResponse detail)
    {
        var closeButton = BuildTopIconButton("×");
        closeButton.FontSize = 28;
        closeButton.Clicked += async (_, _) => await CloseStoryTestModalAsync();

        var header = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 12
        };

        header.Children.Add(new VerticalStackLayout
        {
            Spacing = 2,
            Children =
            {
                new Label
                {
                    Text = "Storie toets",
                    FontSize = 13,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = PlayerMutedTextColor,
                    CharacterSpacing = 1
                },
                new Label
                {
                    Text = detail.Story.Title,
                    FontSize = 24,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = PlayerTextColor,
                    LineBreakMode = LineBreakMode.WordWrap
                }
            }
        });
        header.Children.Add(closeButton);
        Grid.SetColumn(closeButton, 1);
        return header;
    }

    private View BuildStoryTestQuestionCard(MobileStoryTestQuestion question, int questionIndex)
    {
        var options = new VerticalStackLayout
        {
            Spacing = 8
        };
        options.Children.Add(BuildStoryTestOption(question, questionIndex, "A", question.OptionA));
        options.Children.Add(BuildStoryTestOption(question, questionIndex, "B", question.OptionB));
        if (!string.IsNullOrWhiteSpace(question.OptionC))
        {
            options.Children.Add(BuildStoryTestOption(question, questionIndex, "C", question.OptionC));
        }

        var card = new VerticalStackLayout
        {
            Spacing = 12,
            Children =
            {
                new Label
                {
                    Text = $"Vraag {questionIndex + 1}",
                    FontSize = 13,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = PlayerMutedTextColor
                },
                new Label
                {
                    Text = question.Question,
                    FontSize = 18,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = PlayerTextColor,
                    LineBreakMode = LineBreakMode.WordWrap
                },
                options
            }
        };

        if (_isStoryTestSubmitted && !string.IsNullOrWhiteSpace(GetSelectedStoryTestOption(questionIndex)))
        {
            card.Children.Add(new Label
            {
                Text = IsStoryTestAnswerCorrect(question, GetSelectedStoryTestOption(questionIndex))
                    ? "Mooi so!"
                    : "Goeie poging! Kyk, die regte antwoord is gemerk.",
                FontSize = 14,
                FontAttributes = FontAttributes.Bold,
                TextColor = IsStoryTestAnswerCorrect(question, GetSelectedStoryTestOption(questionIndex))
                    ? Color.FromArgb("#B9F2D0")
                    : Color.FromArgb("#FFD5A8")
            });
        }

        return new Border
        {
            BackgroundColor = PlayerPanelColor,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 20 },
            Padding = 16,
            Content = card
        };
    }

    private View BuildStoryTestOption(MobileStoryTestQuestion question, int questionIndex, string option, string? optionText)
    {
        var selectedOption = GetSelectedStoryTestOption(questionIndex);
        var isSelected = string.Equals(selectedOption, option, StringComparison.OrdinalIgnoreCase);
        var isCorrect = IsStoryTestAnswerCorrect(question, option);
        var backgroundColor = isSelected ? Color.FromArgb("#254D47") : PlayerPillColor;
        var strokeColor = isSelected ? Color.FromArgb("#D9F2E7") : Color.FromArgb("#2E4844");
        if (_isStoryTestSubmitted && isCorrect)
        {
            backgroundColor = Color.FromArgb("#1F5B3D");
            strokeColor = Color.FromArgb("#B9F2D0");
        }
        else if (_isStoryTestSubmitted && isSelected && !isCorrect)
        {
            backgroundColor = Color.FromArgb("#5B3324");
            strokeColor = Color.FromArgb("#FFD5A8");
        }

        var optionView = new Border
        {
            BackgroundColor = backgroundColor,
            Stroke = strokeColor,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 16 },
            Padding = new Thickness(12),
            Content = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Star)
                },
                ColumnSpacing = 10,
                Children =
                {
                    new Border
                    {
                        WidthRequest = 30,
                        HeightRequest = 30,
                        BackgroundColor = PlayerAccentColor,
                        StrokeThickness = 0,
                        StrokeShape = new RoundRectangle { CornerRadius = 999 },
                        Content = new Label
                        {
                            Text = option,
                            FontSize = 14,
                            FontAttributes = FontAttributes.Bold,
                            TextColor = Color.FromArgb("#061816"),
                            HorizontalTextAlignment = TextAlignment.Center,
                            VerticalTextAlignment = TextAlignment.Center
                        }
                    },
                    new Label
                    {
                        Text = optionText,
                        FontSize = 15,
                        TextColor = PlayerTextColor,
                        LineBreakMode = LineBreakMode.WordWrap,
                        VerticalTextAlignment = TextAlignment.Center
                    }
                }
            }
        };

        if (optionView.Content is Grid grid && grid.Children.Count > 1)
        {
            grid.SetColumn(grid.Children[1], 1);
        }

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) =>
        {
            SelectStoryTestOption(questionIndex, option);
            RenderStoryTestModalContent();
        };
        optionView.GestureRecognizers.Add(tap);
        return optionView;
    }

    private async Task CloseStoryTestModalAsync()
    {
        if (_storyTestModalPage is null)
        {
            return;
        }

        await Navigation.PopModalAsync(true);
        _storyTestModalPage = null;
        _storyTestModalContent = null;
        _storyTestDetail = null;
    }

    private void SelectStoryTestOption(int questionIndex, string option)
    {
        _selectedStoryTestOptions[questionIndex] = option;
        _isStoryTestSubmitted = false;
    }

    private string? GetSelectedStoryTestOption(int questionIndex) =>
        _selectedStoryTestOptions.TryGetValue(questionIndex, out var option) ? option : null;

    private bool IsStoryTestReadyToCheck(MobileStoryDetailResponse detail) =>
        detail.TestQuestions is { Count: > 0 } questions &&
        Enumerable.Range(0, questions.Count).All(index => !string.IsNullOrWhiteSpace(GetSelectedStoryTestOption(index)));

    private string BuildStoryTestScoreText(MobileStoryDetailResponse detail)
    {
        if (detail.TestQuestions is not { Count: > 0 })
        {
            return "Mooi probeer! Jy het 0 uit 0 reg.";
        }

        var correctAnswers = detail.TestQuestions
            .Select((question, index) => IsStoryTestAnswerCorrect(question, GetSelectedStoryTestOption(index)) ? 1 : 0)
            .Sum();

        return correctAnswers == detail.TestQuestions.Count
            ? "Jippie! Jy het alles reg! Fantastiese werk."
            : $"Mooi probeer! Jy het {correctAnswers} uit {detail.TestQuestions.Count} reg.";
    }

    private static bool IsStoryTestAnswerCorrect(MobileStoryTestQuestion question, string? selectedOption) =>
        string.Equals(question.CorrectOption, selectedOption, StringComparison.OrdinalIgnoreCase);

    private static bool HasStoryTestQuestions(MobileStoryDetailResponse detail) =>
        detail.TestQuestions is { Count: > 0 };

    private static string? GetStorySynopsis(MobileStoryDetailResponse detail)
    {
        if (!string.IsNullOrWhiteSpace(detail.Summary))
        {
            return detail.Summary;
        }

        return string.IsNullOrWhiteSpace(detail.Story.Description) ? null : detail.Story.Description;
    }

    private static bool ShouldShowStoryLead(MobileStoryDetailResponse detail)
    {
        if (string.IsNullOrWhiteSpace(detail.Story.Description) ||
            string.IsNullOrWhiteSpace(detail.Summary))
        {
            return false;
        }

        return !string.Equals(
            NormalizeStoryText(detail.Story.Description),
            NormalizeStoryText(detail.Summary),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeStoryText(string value) =>
        string.Join(
            ' ',
            value.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static View BuildMessage(string message) =>
        new Border
        {
            BackgroundColor = PlayerPanelColor,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 24 },
            Padding = 18,
            Content = new Label
            {
                Text = message,
                TextColor = PlayerTextColor,
                FontSize = 16
            }
        };

    private void StartProgressTimer()
    {
        if (_progressTimer is null)
        {
            _progressTimer = Dispatcher.CreateTimer();
            _progressTimer.Interval = TimeSpan.FromMilliseconds(500);
            _progressTimer.Tick += (_, _) =>
            {
                if (_audioPlaybackService.IsPlaying)
                {
                    FlushPendingListen("progress", force: false);
                }

                UpdateProgressState();
            };
        }

        UpdateProgressState();
        _progressTimer.Start();
    }

    private void StopProgressTimer()
    {
        _progressTimer?.Stop();
    }

    private void UpdateProgressState()
    {
        if (!_isPageActive)
        {
            return;
        }

        var position = _audioPlaybackService.CurrentPosition;
        var duration = _audioPlaybackService.Duration ?? _activeCatalogDuration;

        if (_activeCurrentTimeLabel is not null)
        {
            _activeCurrentTimeLabel.Text = FormatTime(position);
        }

        if (_activeDurationLabel is not null)
        {
            _activeDurationLabel.Text = duration is null ? "--:--" : FormatTime(duration.Value);
        }

        if (_activeProgressBar is not null)
        {
            _activeProgressBar.Progress = duration is { TotalSeconds: > 0 }
                ? Math.Clamp(position.TotalSeconds / duration.Value.TotalSeconds, 0, 1)
                : 0;
        }
    }

    private void BeginListenTracking(MobileStoryDetailResponse detail, Guid trackingSessionId)
    {
        _trackingSessionId = trackingSessionId;
        _trackingStorySlug = detail.Story.Slug;
        _trackingSource = detail.Story.Source;
        _pendingListenSeconds = 0;
        _lastTrackedPosition = _audioPlaybackService.CurrentPosition;
    }

    private Guid ResolveTrackingSessionId(MobileStoryDetailResponse detail)
    {
        return _trackingSessionId != Guid.Empty &&
               string.Equals(_trackingStorySlug, detail.Story.Slug, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(_trackingSource, detail.Story.Source, StringComparison.OrdinalIgnoreCase)
            ? _trackingSessionId
            : Guid.NewGuid();
    }

    private void ResetListenTracking()
    {
        _trackingSessionId = Guid.Empty;
        _trackingStorySlug = null;
        _trackingSource = null;
        _pendingListenSeconds = 0;
        _lastTrackedPosition = null;
        _suppressNextPauseTracking = false;
    }

    private void CaptureListenProgressDelta()
    {
        if (_trackingSessionId == Guid.Empty)
        {
            return;
        }

        var currentPosition = _audioPlaybackService.CurrentPosition;
        var previousPosition = _lastTrackedPosition ?? currentPosition;
        _lastTrackedPosition = currentPosition;

        var elapsedSeconds = (currentPosition - previousPosition).TotalSeconds;
        if (!double.IsFinite(elapsedSeconds) || elapsedSeconds <= 0)
        {
            return;
        }

        _pendingListenSeconds += elapsedSeconds;
    }

    private void FlushPendingListen(string eventType, bool force, bool isCompleted = false)
    {
        if (_trackingSessionId == Guid.Empty ||
            string.IsNullOrWhiteSpace(_trackingStorySlug) ||
            string.IsNullOrWhiteSpace(_trackingSource))
        {
            return;
        }

        CaptureListenProgressDelta();
        while (true)
        {
            var pendingSeconds = _pendingListenSeconds;
            if ((!force && pendingSeconds < ListenFlushThresholdSeconds) ||
                pendingSeconds < ListenMinEventSeconds)
            {
                return;
            }

            var listenedSeconds = Math.Min(pendingSeconds, ListenMaxEventSeconds);
            _pendingListenSeconds = Math.Max(0, pendingSeconds - listenedSeconds);

            var currentPosition = NormalizeTrackingSeconds(_audioPlaybackService.CurrentPosition.TotalSeconds);
            var duration = _audioPlaybackService.Duration ?? _activeCatalogDuration;
            var durationSeconds = NormalizeTrackingSeconds(duration?.TotalSeconds);
            var slug = _trackingStorySlug;
            var source = _trackingSource;
            var sessionId = _trackingSessionId;

            if (string.IsNullOrWhiteSpace(slug) || string.IsNullOrWhiteSpace(source))
            {
                return;
            }

            _continueListeningState.UpdateProgress(slug, source, currentPosition, durationSeconds);

            _ = _apiClient.TrackStoryListenAsync(
                slug,
                source,
                sessionId,
                eventType,
                decimal.Round((decimal)listenedSeconds, 3, MidpointRounding.AwayFromZero),
                currentPosition,
                durationSeconds,
                isCompleted);

            if (!force)
            {
                return;
            }
        }
    }

    private static decimal? NormalizeTrackingSeconds(double? seconds)
    {
        if (seconds is not > 0 || !double.IsFinite(seconds.Value))
        {
            return null;
        }

        return decimal.Round((decimal)seconds.Value, 3, MidpointRounding.AwayFromZero);
    }

    private void SaveContinueListening(MobileStoryDetailResponse detail)
    {
        _continueListeningState.Save(
            detail.Story,
            _playlistSlug,
            _playlistTitle,
            NormalizeTrackingSeconds(_audioPlaybackService.CurrentPosition.TotalSeconds),
            ResolveCatalogDurationSeconds(detail));
    }

    private static string FormatTime(TimeSpan value) =>
        value.TotalHours >= 1
            ? $"{(int)value.TotalHours}:{value.Minutes:00}:{value.Seconds:00}"
            : $"{(int)value.TotalMinutes}:{value.Seconds:00}";

    private void EnsureCatalogDurationVisibleAsync(MobileStoryDetailResponse detail)
    {
        if (_activeCatalogDuration is not null || string.IsNullOrWhiteSpace(detail.AudioUrl))
        {
            return;
        }

        var audioUrl = _apiClient.BuildAbsoluteUrl(detail.AudioUrl);
        var slug = detail.Story.Slug;
        var cancellationToken = _loadCts?.Token ?? CancellationToken.None;

        _ = Task.Run(async () =>
        {
            try
            {
                TimeSpan? duration = null;
                var shouldPrepareFirst = DeviceInfo.Current.Platform == DevicePlatform.Android;

                if (shouldPrepareFirst)
                {
                    var preparedAudioUrl = await _apiClient.PrepareAudioPlaybackSourceAsync(
                        detail.AudioUrl,
                        detail.Story.Slug,
                        detail.Story.Source,
                        cancellationToken);
                    duration = await _audioPlaybackService.GetDurationAsync(preparedAudioUrl, cancellationToken);
                }
                else
                {
                    duration = await _audioPlaybackService.GetDurationAsync(audioUrl, cancellationToken);
                    if (duration is null && !cancellationToken.IsCancellationRequested)
                    {
                        var preparedAudioUrl = await _apiClient.PrepareAudioPlaybackSourceAsync(
                            detail.AudioUrl,
                            detail.Story.Slug,
                            detail.Story.Source,
                            cancellationToken);
                        if (!string.Equals(preparedAudioUrl, audioUrl, StringComparison.Ordinal))
                        {
                            duration = await _audioPlaybackService.GetDurationAsync(preparedAudioUrl, cancellationToken);
                        }
                    }
                }

                if (duration is null || cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    if (cancellationToken.IsCancellationRequested ||
                        !_isPageActive ||
                        !string.Equals(_currentDetail?.Story.Slug, slug, StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }

                    _activeCatalogDuration = duration;
                    UpdateProgressState();
                });
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
                // Playback metadata should quietly fall back to the live player duration if probing fails.
            }
        });
    }

    private TimeSpan? ResolveCatalogDuration(MobileStoryDetailResponse detail) =>
        ToTimeSpan(ResolveCatalogDurationSeconds(detail));

    private decimal? ResolveCatalogDurationSeconds(MobileStoryDetailResponse detail)
    {
        if (detail.Story.DurationSeconds is > 0)
        {
            return detail.Story.DurationSeconds;
        }

        if (_previewStory is { DurationSeconds: > 0 } previewStory &&
            string.Equals(_previewStory.Slug, detail.Story.Slug, StringComparison.OrdinalIgnoreCase))
        {
            return previewStory.DurationSeconds;
        }

        var playlistStory = _playlistStories.FirstOrDefault(story =>
            string.Equals(story.Slug, detail.Story.Slug, StringComparison.OrdinalIgnoreCase));

        return playlistStory?.DurationSeconds;
    }

    private static TimeSpan? ToTimeSpan(decimal? seconds)
    {
        if (seconds is not > 0)
        {
            return null;
        }

        return TimeSpan.FromSeconds((double)seconds.Value);
    }

    private static bool IsR2AudioUrl(string? audioUrl) =>
        !string.IsNullOrWhiteSpace(audioUrl) &&
        Uri.TryCreate(audioUrl, UriKind.Absolute, out var uri) &&
        (uri.Host.Contains("r2.cloudflarestorage.com", StringComparison.OrdinalIgnoreCase) ||
         uri.Host.Contains("r2.dev", StringComparison.OrdinalIgnoreCase) ||
         uri.Host.Contains("media.prioritybit.co.za", StringComparison.OrdinalIgnoreCase));

    private void SubscribePlaybackEvents()
    {
        if (_isPlaybackEventSubscribed)
        {
            return;
        }

        _audioPlaybackService.PlaybackEnded += OnPlaybackEnded;
        _audioPlaybackService.PlaybackStateChanged += OnPlaybackStateChanged;
        _isPlaybackEventSubscribed = true;
    }

    private void UnsubscribePlaybackEvents()
    {
        if (!_isPlaybackEventSubscribed)
        {
            return;
        }

        _audioPlaybackService.PlaybackEnded -= OnPlaybackEnded;
        _audioPlaybackService.PlaybackStateChanged -= OnPlaybackStateChanged;
        _isPlaybackEventSubscribed = false;
    }

    private void OnPlaybackStateChanged(object? sender, EventArgs args)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (!_isPageActive)
            {
                return;
            }

            if (_activePlayButton is not null)
            {
                _activePlayButton.Text = _audioPlaybackService.IsPlaying ? "II" : "▶";
            }

            if (_audioPlaybackService.IsPlaying)
            {
                _lastTrackedPosition = _audioPlaybackService.CurrentPosition;
                StartProgressTimer();
            }
            else
            {
                if (_suppressNextPauseTracking)
                {
                    _suppressNextPauseTracking = false;
                }
                else
                {
                    FlushPendingListen("pause", force: true);
                }

                StopProgressTimer();
                UpdateProgressState();
            }
        });
    }

    private void OnPlaybackEnded(object? sender, EventArgs args)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            if (!_isPageActive)
            {
                return;
            }

            FlushPendingListen("ended", force: true, isCompleted: true);
            StopProgressTimer();
            UpdateProgressState();

            if (_activePlayButton is not null)
            {
                _activePlayButton.Text = "▶";
            }

            if (_currentDetail is { } currentDetail &&
                _playlistPlaybackState.CanAutoplayAdvance(currentDetail.Story))
            {
                var nextStory = ResolveNextStory(currentDetail);
                if (nextStory is not null)
                {
                    await ReplaceActiveStoryAsync(nextStory, autoplay: true);
                }
            }
        });
    }

    private async Task OpenStoryAsync(MobileStorySummary story)
    {
        await _navigationGate.RunAsync(async () =>
        {
            _loadedKey = null;
            _previewStory = story;
            await Shell.Current.GoToAsync(
                $"{nameof(StoryDetailPage)}?slug={Uri.EscapeDataString(story.Slug)}&source={Uri.EscapeDataString(story.Source)}",
                animate: false,
                parameters: new Dictionary<string, object>
                {
                    ["preview"] = story
                });
        });
    }

    private async Task OpenPlaylistStoryAsync(MobileStorySummary story, bool autoplay = false)
    {
        await ReplaceActiveStoryAsync(story, autoplay);
    }

    private async Task ReplaceActiveStoryAsync(MobileStorySummary story, bool autoplay = false)
    {
        CancelActiveLoad();
        DismissCastPicker();
        FlushPendingListen("pagehide", force: true);
        StopProgressTimer();
        _suppressNextPauseTracking = true;
        _pendingAutoplayAfterLoad = autoplay;
        TryStopAudioPlayback();
        ClearActivePlaybackUi();
        ResetListenTracking();
        if (autoplay)
        {
            _playlistPlaybackState.TrackAutoplayAdvance(story);
        }
        else
        {
            _playlistPlaybackState.TrackManualStorySelection(story);
        }

        StorySlug = story.Slug;
        Source = story.Source;
        _previewStory = story;
        _loadedKey = $"{StorySlug}:{Source}";

        RenderPreview(story);

        _loadCts = new CancellationTokenSource();
        await LoadAsync(showLoading: false, cancellationToken: _loadCts.Token);
    }

    private sealed class DownCaretDrawable : IDrawable
    {
        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            canvas.StrokeColor = PlayerTextColor;
            canvas.StrokeSize = 3.6f;
            canvas.StrokeLineCap = LineCap.Round;
            canvas.StrokeLineJoin = LineJoin.Round;

            var centerX = dirtyRect.Center.X;
            var centerY = dirtyRect.Center.Y + dirtyRect.Height * 0.04f;
            var halfWidth = dirtyRect.Width * 0.25f;
            var halfHeight = dirtyRect.Height * 0.16f;

            canvas.DrawLine(centerX - halfWidth, centerY - halfHeight, centerX, centerY + halfHeight);
            canvas.DrawLine(centerX, centerY + halfHeight, centerX + halfWidth, centerY - halfHeight);
        }
    }

    private enum CastSheetIconKind
    {
        Phone,
        AirPlay,
        Speaker
    }

    private sealed class CastSheetIconDrawable(CastSheetIconKind kind) : IDrawable
    {
        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            canvas.StrokeColor = Colors.White;
            canvas.FillColor = Colors.White;
            canvas.StrokeSize = 2.2f;
            canvas.StrokeLineCap = LineCap.Round;
            canvas.StrokeLineJoin = LineJoin.Round;

            switch (kind)
            {
                case CastSheetIconKind.Phone:
                    DrawPhone(canvas, dirtyRect);
                    break;
                case CastSheetIconKind.AirPlay:
                    DrawAirPlay(canvas, dirtyRect);
                    break;
                case CastSheetIconKind.Speaker:
                    DrawSpeaker(canvas, dirtyRect);
                    break;
            }
        }

        private static void DrawPhone(ICanvas canvas, RectF rect)
        {
            var phone = new RectF(
                rect.Width * 0.22f,
                rect.Height * 0.08f,
                rect.Width * 0.56f,
                rect.Height * 0.84f);
            canvas.DrawRoundedRectangle(phone, 3);
            canvas.DrawLine(
                phone.Left + phone.Width * 0.28f,
                phone.Top + phone.Height * 0.08f,
                phone.Right - phone.Width * 0.28f,
                phone.Top + phone.Height * 0.08f);
        }

        private static void DrawAirPlay(ICanvas canvas, RectF rect)
        {
            var screen = new RectF(
                rect.Width * 0.08f,
                rect.Height * 0.12f,
                rect.Width * 0.84f,
                rect.Height * 0.54f);
            canvas.DrawRectangle(screen);

            var triangle = new PathF();
            triangle.MoveTo(rect.Width * 0.50f, rect.Height * 0.53f);
            triangle.LineTo(rect.Width * 0.30f, rect.Height * 0.88f);
            triangle.LineTo(rect.Width * 0.70f, rect.Height * 0.88f);
            triangle.Close();
            canvas.FillPath(triangle);
        }

        private static void DrawSpeaker(ICanvas canvas, RectF rect)
        {
            var body = new RectF(
                rect.Width * 0.24f,
                rect.Height * 0.08f,
                rect.Width * 0.52f,
                rect.Height * 0.84f);
            canvas.DrawRectangle(body);
            canvas.DrawCircle(rect.Width * 0.50f, rect.Height * 0.36f, rect.Width * 0.12f);
            canvas.DrawCircle(rect.Width * 0.50f, rect.Height * 0.68f, rect.Width * 0.17f);
        }
    }

    private sealed class CastIconDrawable : IDrawable
    {
        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            canvas.StrokeColor = PlayerTextColor;
            canvas.FillColor = PlayerTextColor;
            canvas.StrokeSize = 2.8f;
            canvas.StrokeLineCap = LineCap.Round;
            canvas.StrokeLineJoin = LineJoin.Round;

            var screenRect = new RectF(
                dirtyRect.Width * 0.12f,
                dirtyRect.Height * 0.18f,
                dirtyRect.Width * 0.76f,
                dirtyRect.Height * 0.48f);
            canvas.DrawRoundedRectangle(screenRect, 2.5f);

            var triangle = new PathF();
            triangle.MoveTo(dirtyRect.Width * 0.50f, dirtyRect.Height * 0.56f);
            triangle.LineTo(dirtyRect.Width * 0.32f, dirtyRect.Height * 0.84f);
            triangle.LineTo(dirtyRect.Width * 0.68f, dirtyRect.Height * 0.84f);
            triangle.Close();
            canvas.FillPath(triangle);
        }
    }

    private sealed class InfoIconDrawable : IDrawable
    {
        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            canvas.StrokeColor = PlayerTextColor;
            canvas.FillColor = PlayerTextColor;
            canvas.StrokeSize = 1.8f;
            canvas.StrokeLineCap = LineCap.Round;
            canvas.StrokeLineJoin = LineJoin.Round;

            var radius = Math.Min(dirtyRect.Width, dirtyRect.Height) * 0.42f;
            var centerX = dirtyRect.Center.X;
            var centerY = dirtyRect.Center.Y;

            canvas.DrawCircle(centerX, centerY, radius);
            canvas.FillCircle(centerX, dirtyRect.Height * 0.30f, 1.1f);
            canvas.DrawLine(centerX, dirtyRect.Height * 0.43f, centerX, dirtyRect.Height * 0.72f);
        }
    }

    private sealed class DownloadIconDrawable : IDrawable
    {
        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            canvas.StrokeColor = PlayerTextColor;
            canvas.StrokeSize = 2.1f;
            canvas.StrokeLineCap = LineCap.Round;
            canvas.StrokeLineJoin = LineJoin.Round;

            var centerX = dirtyRect.Center.X;
            var top = dirtyRect.Height * 0.14f;
            var arrowBottom = dirtyRect.Height * 0.58f;
            var wingY = dirtyRect.Height * 0.43f;
            var wingOffset = dirtyRect.Width * 0.18f;

            canvas.DrawLine(centerX, top, centerX, arrowBottom);
            canvas.DrawLine(centerX - wingOffset, wingY, centerX, arrowBottom);
            canvas.DrawLine(centerX + wingOffset, wingY, centerX, arrowBottom);
            canvas.DrawLine(dirtyRect.Width * 0.22f, dirtyRect.Height * 0.82f, dirtyRect.Width * 0.78f, dirtyRect.Height * 0.82f);
        }
    }

    private sealed class DownloadedIconDrawable : IDrawable
    {
        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            canvas.StrokeColor = PlayerTextColor;
            canvas.StrokeSize = 2.1f;
            canvas.StrokeLineCap = LineCap.Round;
            canvas.StrokeLineJoin = LineJoin.Round;

            var trayY = dirtyRect.Height * 0.82f;
            canvas.DrawLine(dirtyRect.Width * 0.20f, trayY, dirtyRect.Width * 0.80f, trayY);
            canvas.DrawLine(dirtyRect.Width * 0.27f, dirtyRect.Height * 0.50f, dirtyRect.Width * 0.43f, dirtyRect.Height * 0.66f);
            canvas.DrawLine(dirtyRect.Width * 0.43f, dirtyRect.Height * 0.66f, dirtyRect.Width * 0.76f, dirtyRect.Height * 0.30f);
        }
    }

    private sealed class FullscreenIconDrawable : IDrawable
    {
        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            canvas.StrokeColor = Colors.White;
            canvas.StrokeSize = 2.4f;
            canvas.StrokeLineCap = LineCap.Round;
            canvas.StrokeLineJoin = LineJoin.Round;

            var inset = dirtyRect.Width * 0.18f;
            var cornerLength = dirtyRect.Width * 0.22f;
            var left = dirtyRect.Left + inset;
            var right = dirtyRect.Right - inset;
            var top = dirtyRect.Top + inset;
            var bottom = dirtyRect.Bottom - inset;

            canvas.DrawLine(left, top + cornerLength, left, top);
            canvas.DrawLine(left, top, left + cornerLength, top);

            canvas.DrawLine(right - cornerLength, top, right, top);
            canvas.DrawLine(right, top, right, top + cornerLength);

            canvas.DrawLine(left, bottom - cornerLength, left, bottom);
            canvas.DrawLine(left, bottom, left + cornerLength, bottom);

            canvas.DrawLine(right - cornerLength, bottom, right, bottom);
            canvas.DrawLine(right, bottom - cornerLength, right, bottom);
        }
    }
}
