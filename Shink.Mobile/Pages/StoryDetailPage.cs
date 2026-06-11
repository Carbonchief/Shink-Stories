using Shink.Mobile.Models;
using Shink.Mobile.Services;
using Shink.Mobile.Views;

namespace Shink.Mobile.Pages;

[QueryProperty(nameof(StorySlug), "slug")]
[QueryProperty(nameof(Source), "source")]
public sealed class StoryDetailPage : ContentPage, IQueryAttributable
{
    private const double TallScreenThreshold = 820;
    private static readonly Color PlayerBackgroundColor = Color.FromArgb("#061816");
    private static readonly Color PlayerPanelColor = Color.FromArgb("#102724");
    private static readonly Color PlayerPillColor = Color.FromArgb("#1B302D");
    private static readonly Color PlayerTextColor = Color.FromArgb("#F7FBF7");
    private static readonly Color PlayerMutedTextColor = Color.FromArgb("#AAB7B2");
    private static readonly Color PlayerAccentColor = Color.FromArgb("#FFFFFF");

    private readonly MobileApiClient _apiClient;
    private readonly SessionState _sessionState;
    private readonly IAudioPlaybackService _audioPlaybackService;
    private readonly PlaylistPlaybackState _playlistPlaybackState;
    private readonly Grid _root;
    private readonly VerticalStackLayout _content;
    private Border? _castSheet;
    private BoxView? _castScrim;
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
    private bool _isPlaybackEventSubscribed;
    private bool _isClosing;

    public StoryDetailPage(
        MobileApiClient apiClient,
        SessionState sessionState,
        IAudioPlaybackService audioPlaybackService,
        PlaylistPlaybackState playlistPlaybackState)
    {
        _apiClient = apiClient;
        _sessionState = sessionState;
        _audioPlaybackService = audioPlaybackService;
        _playlistPlaybackState = playlistPlaybackState;
        BackgroundColor = PlayerBackgroundColor;
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

        _root = new Grid
        {
            BackgroundColor = PlayerBackgroundColor,
            Children = { scrollView }
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
        _isPageActive = false;
        CancelActiveLoad();
        DismissCastPicker();
        StopProgressTimer();
        UnsubscribePlaybackEvents();
        TryStopAudioPlayback();
        ClearActivePlaybackUi();
    }

    private async Task LoadAsync(bool showLoading = true, CancellationToken cancellationToken = default)
    {
        if (showLoading)
        {
            _content.Children.Clear();
            _content.Children.Add(BuildLoadingState());
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

            RenderDetail(detail);
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

            _content.Children.Clear();
            _content.Children.Add(BuildMessage(ex.Message));
        }
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
            LoginUrl: string.Empty,
            PlansUrl: string.Empty);

        _content.Children.Clear();
        Title = story.Title;
        _activeStory = story;
        _content.Children.Add(BuildTopBar());
        _content.Children.Add(BuildCoverArt(previewDetail));
        _content.Children.Add(BuildStoryHeader(previewDetail));
        _content.Children.Add(BuildActionRail(previewDetail));
        _content.Children.Add(BuildInlineLoadingState());
    }

    private void RenderDetail(MobileStoryDetailResponse detail)
    {
        _content.Children.Clear();
        Title = detail.Story.Title;
        _activeStory = detail.Story;
        _content.Children.Add(BuildTopBar());
        _content.Children.Add(BuildCoverArt(detail));
        _content.Children.Add(BuildStoryHeader(detail));
        _content.Children.Add(BuildActionRail(detail));

        if (detail.RequiresSubscription)
        {
            _content.Children.Add(BuildLockedPanel(detail));
        }
        else if (!string.IsNullOrWhiteSpace(detail.AudioUrl))
        {
            _ = _apiClient.TrackStoryViewAsync(detail.Story.Slug, detail.Story.Source);
            _content.Children.Add(BuildAudioPlayer(detail));
        }
        else
        {
            _content.Children.Add(BuildMessage("Geen audio is tans beskikbaar vir hierdie storie nie."));
        }

        var playlistQueue = BuildPlaylistQueue(detail);
        if (playlistQueue is not null)
        {
            _content.Children.Add(playlistQueue);
        }

        if (ScreenHeight >= TallScreenThreshold)
        {
            _content.Children.Add(BuildQueueHint());
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

        var closeButton = BuildTopIconButton("⌄");
        closeButton.Clicked += async (_, _) => await CloseAsync(closeButton);

        var castButton = BuildCastButton();
        var menuButton = BuildTopIconButton("⋮");

        grid.Children.Add(closeButton);
        grid.Children.Add(castButton);
        grid.Children.Add(menuButton);
        Grid.SetColumn(castButton, 2);
        Grid.SetColumn(menuButton, 3);

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
                    BuildCastAllDevicesHeader(),
                    BuildCastPickerDeviceRow(
                        CastSheetIconKind.AirPlay,
                        "AirPlay and Bluetooth devices",
                        () => routePicker.OpenPicker()),
                    BuildCastPickerDeviceRow(
                        CastSheetIconKind.Speaker,
                        "Living Room Speaker",
                        () => routePicker.OpenPicker()),
                    routePicker
                }
            }
        };

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

    private static View BuildCastAllDevicesHeader() =>
        new HorizontalStackLayout
        {
            Spacing = 18,
            Margin = new Thickness(0, 0, 0, 22),
            Children =
            {
                new Label
                {
                    Text = "All devices",
                    FontSize = 23,
                    TextColor = Colors.White,
                    VerticalTextAlignment = TextAlignment.Center
                },
                new ActivityIndicator
                {
                    IsRunning = true,
                    Color = Color.FromArgb("#9C9C9C"),
                    WidthRequest = 28,
                    HeightRequest = 28,
                    VerticalOptions = LayoutOptions.Center
                }
            }
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

    private async Task CloseAsync(Button closeButton)
    {
        if (_isClosing)
        {
            return;
        }

        _isClosing = true;
        closeButton.IsEnabled = false;
        CancelActiveLoad();
        await Shell.Current.GoToAsync("..", animate: false);
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

    private View BuildCoverArt(MobileStoryDetailResponse detail) =>
        new Border
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
            Content = new Image
            {
                Source = _apiClient.BuildImageUrl(detail.Story.ImageUrl),
                Aspect = Aspect.AspectFill
            }
        };

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

    private static View BuildStoryHeader(MobileStoryDetailResponse detail)
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
        var favoriteButton = BuildPillButton(detail.Story.IsFavorite ? "♥  Gunsteling" : "♡  Gunsteling");
        favoriteButton.TextColor = detail.Story.IsFavorite ? Color.FromArgb("#FF8A9A") : PlayerTextColor;
        favoriteButton.IsEnabled = _sessionState.Current.IsSignedIn;
        favoriteButton.Clicked += async (_, _) =>
        {
            if (!_sessionState.Current.IsSignedIn)
            {
                return;
            }

            await _apiClient.SetFavoriteAsync(detail.Story.Slug, detail.Story.Source, !detail.Story.IsFavorite);
            _loadedKey = null;
            await LoadAsync();
        };

        var saveButton = BuildPillButton("☰+  Stoor");
        saveButton.IsEnabled = _sessionState.Current.IsSignedIn;
        saveButton.Clicked += async (_, _) =>
        {
            if (!_sessionState.Current.IsSignedIn)
            {
                return;
            }

            await _apiClient.SetFavoriteAsync(detail.Story.Slug, detail.Story.Source, true);
            _loadedKey = null;
            await LoadAsync();
        };

        var shareButton = BuildPillButton("↗");
        shareButton.WidthRequest = 64;
        shareButton.Clicked += async (_, _) => await Share.Default.RequestAsync(new ShareTextRequest
        {
            Uri = detail.ShareUrl,
            Title = detail.Story.Title
        });

        var infoButton = BuildPillButton("▱  Info");
        infoButton.Clicked += async (_, _) => await DisplayAlertAsync(detail.Story.Title, detail.Story.Description, "Maak toe");

        return new ScrollView
        {
            Orientation = ScrollOrientation.Horizontal,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Never,
            Content = new HorizontalStackLayout
            {
                Spacing = 10,
                Children =
                {
                    favoriteButton,
                    infoButton,
                    saveButton,
                    shareButton
                }
            }
        };
    }

    private static Button BuildPillButton(string text) =>
        new()
        {
            Text = text,
            FontSize = 15,
            FontAttributes = FontAttributes.Bold,
            TextColor = PlayerTextColor,
            BackgroundColor = PlayerPillColor,
            CornerRadius = 24,
            HeightRequest = 42,
            Padding = new Thickness(16, 0)
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
        var trackingSessionId = Guid.NewGuid();
        var progressBar = new ProgressBar
        {
            Progress = 0,
            ProgressColor = PlayerAccentColor,
            BackgroundColor = Color.FromArgb("#3D4B48"),
            HeightRequest = 4
        };
        var currentTimeLabel = BuildTimeLabel("0:00", TextAlignment.Start);
        _activeCatalogDuration = ToTimeSpan(detail.Story.DurationSeconds);
        var durationLabel = BuildTimeLabel(
            _activeCatalogDuration is null ? "--:--" : FormatTime(_activeCatalogDuration.Value),
            TextAlignment.End);
        var playButton = BuildMainPlaybackButton("▶");

        _activePlayButton = playButton;
        _activeProgressBar = progressBar;
        _activeCurrentTimeLabel = currentTimeLabel;
        _activeDurationLabel = durationLabel;

        playButton.Clicked += async (_, _) =>
        {
            if (_audioPlaybackService.IsPlaying)
            {
                _audioPlaybackService.Pause();
                playButton.Text = "▶";
                StopProgressTimer();
                return;
            }

            try
            {
                var playbackUrl = await _apiClient.PrepareAudioPlaybackSourceAsync(
                    detail.AudioUrl,
                    detail.Story.Slug,
                    detail.Story.Source);
                await PlayPreparedAudioAsync(playbackUrl, detail, trackingSessionId, playButton);
            }
            catch (Exception) when (IsR2AudioUrl(detail.AudioUrl))
            {
                try
                {
                    var cachedPlaybackUrl = await _apiClient.DownloadAudioForPlaybackAsync(
                        detail.AudioUrl ?? string.Empty,
                        detail.Story.Slug,
                        detail.Story.Source);
                    await PlayPreparedAudioAsync(cachedPlaybackUrl, detail, trackingSessionId, playButton);
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
        };

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
        StartProgressTimer();
        _ = _apiClient.TrackStoryListenAsync(
            detail.Story.Slug,
            detail.Story.Source,
            trackingSessionId,
            "play",
            listenedSeconds: 1,
            positionSeconds: null,
            durationSeconds: detail.Story.DurationSeconds,
            isCompleted: false);
    }

    private View BuildTransportControls(MobileStoryDetailResponse detail, Button playButton)
    {
        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star)
            },
            HeightRequest = 82
        };

        var shuffleButton = BuildTransportButton("⇄");
        var previousButton = BuildTransportButton("|‹");
        var nextButton = BuildTransportButton("›|");
        var repeatButton = BuildTransportButton("↻");

        previousButton.IsEnabled = detail.PreviousStory is not null;
        nextButton.IsEnabled = detail.NextStory is not null;
        previousButton.Clicked += async (_, _) =>
        {
            if (detail.PreviousStory is not null)
            {
                await OpenStoryAsync(detail.PreviousStory);
            }
        };
        nextButton.Clicked += async (_, _) =>
        {
            if (detail.NextStory is not null)
            {
                await OpenStoryAsync(detail.NextStory);
            }
        };

        grid.Children.Add(shuffleButton);
        grid.Children.Add(previousButton);
        grid.Children.Add(playButton);
        grid.Children.Add(nextButton);
        grid.Children.Add(repeatButton);
        Grid.SetColumn(previousButton, 1);
        Grid.SetColumn(playButton, 2);
        Grid.SetColumn(nextButton, 3);
        Grid.SetColumn(repeatButton, 4);

        return grid;
    }

    private View? BuildPlaylistQueue(MobileStoryDetailResponse detail)
    {
        var queuedStories = _playlistStories
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
        playButton.Clicked += async (_, _) => await OpenPlaylistStoryAsync(story);

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
        tap.Tapped += async (_, _) => await OpenPlaylistStoryAsync(story);
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

    private static View BuildQueueHint() =>
        new VerticalStackLayout
        {
            Spacing = 10,
            Padding = new Thickness(0, 8, 0, 0),
            Children =
            {
                new BoxView
                {
                    WidthRequest = 48,
                    HeightRequest = 6,
                    CornerRadius = 3,
                    Color = Color.FromArgb("#2B3B38"),
                    HorizontalOptions = LayoutOptions.Center
                },
                new Label
                {
                    Text = "Jou ry",
                    FontSize = 16,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = PlayerMutedTextColor,
                    HorizontalTextAlignment = TextAlignment.Center
                }
            }
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
            _progressTimer.Tick += (_, _) => UpdateProgressState();
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

    private static string FormatTime(TimeSpan value) =>
        value.TotalHours >= 1
            ? $"{(int)value.TotalHours}:{value.Minutes:00}:{value.Seconds:00}"
            : $"{(int)value.TotalMinutes}:{value.Seconds:00}";

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
                StartProgressTimer();
            }
            else
            {
                StopProgressTimer();
                UpdateProgressState();
            }
        });
    }

    private void OnPlaybackEnded(object? sender, EventArgs args)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (!_isPageActive)
            {
                return;
            }

            StopProgressTimer();
            UpdateProgressState();

            if (_activePlayButton is not null)
            {
                _activePlayButton.Text = "▶";
            }

        });
    }

    private async Task OpenStoryAsync(MobileStorySummary story)
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
    }

    private async Task OpenPlaylistStoryAsync(MobileStorySummary story)
    {
        _loadedKey = null;
        _previewStory = story;
        await Shell.Current.GoToAsync(
            $"{nameof(StoryDetailPage)}?slug={Uri.EscapeDataString(story.Slug)}&source={Uri.EscapeDataString(story.Source)}",
            animate: false,
            parameters: new Dictionary<string, object>
            {
                ["preview"] = story,
                ["playlistTitle"] = _playlistTitle ?? string.Empty,
                ["playlistSlug"] = _playlistSlug ?? string.Empty
            });
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
            canvas.StrokeSize = 2.6f;
            canvas.StrokeLineCap = LineCap.Round;
            canvas.StrokeLineJoin = LineJoin.Round;

            var screenRect = new RectF(
                dirtyRect.Width * 0.25f,
                dirtyRect.Height * 0.18f,
                dirtyRect.Width * 0.56f,
                dirtyRect.Height * 0.42f);
            canvas.DrawRoundedRectangle(screenRect, 1.5f);

            canvas.DrawArc(
                dirtyRect.Width * 0.05f,
                dirtyRect.Height * 0.49f,
                dirtyRect.Width * 0.42f,
                dirtyRect.Height * 0.42f,
                0,
                90,
                false,
                false);
            canvas.DrawArc(
                dirtyRect.Width * 0.05f,
                dirtyRect.Height * 0.66f,
                dirtyRect.Width * 0.22f,
                dirtyRect.Height * 0.22f,
                0,
                90,
                false,
                false);
            canvas.FillColor = PlayerTextColor;
            canvas.FillCircle(dirtyRect.Width * 0.12f, dirtyRect.Height * 0.82f, 2.3f);
        }
    }
}
