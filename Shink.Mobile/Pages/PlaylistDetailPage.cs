using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Shink.Mobile.Models;
using Shink.Mobile.Services;
using Microsoft.Maui.Layouts;

namespace Shink.Mobile.Pages;

public sealed class PlaylistDetailPage : ContentPage, IQueryAttributable
{
    private const string PreviousIconGlyph = "\uf048";
    private const string NextIconGlyph = "\uf051";
    private const string PlayIconGlyph = "\uf04b";
    private const string PauseIconGlyph = "\uf04c";
    private const string BackIconGlyph = "\uf060";
    private const string HeartIconGlyph = "\uf004";
    private const string VolumeIconGlyph = "\uf028";
    private const string ShuffleIconGlyph = "\uf074";
    private const string AutoplayIconGlyph = "\uf144";
    private const string InfinityIconGlyph = "\uf534";
    private const string HourglassIconGlyph = "\uf252";
    private const string ShareIconGlyph = "\uf1e0";
    private const string ChevronDownIconGlyph = "\uf078";
    private static readonly double[] PlaybackSpeedSteps = [0.75, 1, 1.25, 1.5];
    private static readonly Color PageColor = Color.FromArgb("#222222");
    private static readonly Color TextColor = Color.FromArgb("#F7F2EA");
    private static readonly Color MutedTextColor = Color.FromArgb("#A9A49E");
    private static readonly Color AquaColor = Color.FromArgb("#8FE5E8");
    private static readonly Color PinkColor = Color.FromArgb("#FF135B");
    private static readonly Color RowColor = Color.FromArgb("#0DFFFFFF");
    private static readonly Color ActiveRowColor = Color.FromArgb("#1FFF135B");
    private static readonly Color RowStrokeColor = Color.FromArgb("#15FFFFFF");
    private static readonly Color ActiveRowStrokeColor = Color.FromArgb("#9EFF135B");

    private readonly MobileApiClient _apiClient;
    private readonly SessionState _sessionState;
    private readonly IAudioPlaybackService _audioPlaybackService;
    private readonly StoryPlaybackSession _storyPlaybackSession;
    private readonly PlaylistPlaybackState _playlistPlaybackState;
    private readonly ObservableCollection<PlaylistTrackItem> _tracks = [];
    private readonly CollectionView _trackList;
    private MobilePlaylist? _playlist;
    private MobileStorySummary? _currentStory;
    private MobileStoryDetailResponse? _currentDetail;
    private CancellationTokenSource? _loadCts;
    private IDispatcherTimer? _progressTimer;
    private Button? _playButton;
    private ActivityIndicator? _playLoadingIndicator;
    private Slider? _progressSlider;
    private Label? _currentTimeLabel;
    private Label? _durationLabel;
    private bool _isPageActive;
    private bool _isPlaybackEventSubscribed;
    private bool _isSummaryExpanded;
    private bool _autoplayAfterLoad;
    private bool _isPlaybackRequestInFlight;
    private bool _isProgressScrubbing;
    private bool _isUpdatingProgressSlider;
    private string? _loadingStoryKey;

    public PlaylistDetailPage(
        MobileApiClient apiClient,
        SessionState sessionState,
        IAudioPlaybackService audioPlaybackService,
        StoryPlaybackSession storyPlaybackSession,
        PlaylistPlaybackState playlistPlaybackState)
    {
        _apiClient = apiClient;
        _sessionState = sessionState;
        _audioPlaybackService = audioPlaybackService;
        _storyPlaybackSession = storyPlaybackSession;
        _playlistPlaybackState = playlistPlaybackState;

        Title = "Speellys";
        SafeAreaEdges = SafeAreaEdges.None;
        BackgroundColor = PageColor;
        Shell.SetNavBarIsVisible(this, false);
        Shell.SetTabBarIsVisible(this, false);

        _trackList = new CollectionView
        {
            BackgroundColor = Colors.Transparent,
            ItemsSource = _tracks,
            SelectionMode = SelectionMode.None,
            ItemSizingStrategy = ItemSizingStrategy.MeasureFirstItem,
            ItemsLayout = new LinearItemsLayout(ItemsLayoutOrientation.Vertical)
            {
                ItemSpacing = 7
            },
            ItemTemplate = new DataTemplate(BuildTrackRow),
            Footer = new BoxView { HeightRequest = 22, BackgroundColor = Colors.Transparent }
        };

        Content = new Grid
        {
            SafeAreaEdges = SafeAreaEdges.None,
            Children =
            {
                _trackList,
                BuildFixedBackButtonOverlay()
            }
        };
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("playlist", out var value) && value is MobilePlaylist playlist)
        {
            SetPlaylist(playlist);
        }
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _isPageActive = true;
        SubscribePlaybackEvents();
        if (_currentStory is { IsLocked: false })
        {
            _ = LoadCurrentStoryAsync();
        }
    }

    protected override void OnDisappearing()
    {
        _isPageActive = false;
        _loadCts?.Cancel();
        StopProgressTimer();
        UnsubscribePlaybackEvents();
        _storyPlaybackSession.NotifyPageHidden();

        base.OnDisappearing();
    }

    private void SetPlaylist(MobilePlaylist playlist)
    {
        var requestedStory = playlist.Stories.FirstOrDefault(_storyPlaybackSession.IsCurrentStory)
            ?? playlist.Stories.FirstOrDefault(story => !story.IsLocked)
            ?? playlist.Stories.FirstOrDefault();
        _playlistPlaybackState.Set(playlist, requestedStory);
        _playlist = _playlistPlaybackState.CurrentPlaylist ?? playlist;
        _currentStory = _playlist.Stories.FirstOrDefault(_storyPlaybackSession.IsCurrentStory)
            ?? _playlist.Stories.FirstOrDefault(story => !story.IsLocked)
            ?? _playlist.Stories.FirstOrDefault();
        _currentDetail = null;
        _loadingStoryKey = null;

        _tracks.Clear();
        for (var index = 0; index < _playlist.Stories.Count; index++)
        {
            var story = _playlist.Stories[index];
            _tracks.Add(new PlaylistTrackItem(
                index + 1,
                story,
                IsCurrentStory(story),
                PageHelpers.BuildStoryImageRequest(story, _apiClient, "schink_background.jpeg")));
        }

        RebuildHeader();
    }

    private void RebuildHeader()
    {
        _playButton = null;
        _playLoadingIndicator = null;
        _progressSlider = null;
        _isProgressScrubbing = false;
        _isUpdatingProgressSlider = false;
        _currentTimeLabel = null;
        _durationLabel = null;
        _trackList.Header = _playlist is null || _currentStory is null
            ? BuildEmptyState()
            : BuildPlaylistHeader(_playlist, _currentStory);
        UpdateProgressState();
    }

    private View BuildEmptyState() =>
        new VerticalStackLayout
        {
            Padding = 24,
            Children =
            {
                new Label
                {
                    Text = "Hierdie speellys is nie beskikbaar nie.",
                    TextColor = TextColor,
                    FontSize = 17
                }
            }
        };

    private View BuildPlaylistHeader(MobilePlaylist playlist, MobileStorySummary story)
    {
        var stack = new VerticalStackLayout
        {
            Padding = new Thickness(14, 76, 14, 12),
            Spacing = 14
        };

        stack.Children.Add(BuildTopRow(playlist));
        stack.Children.Add(BuildCover(story));
        stack.Children.Add(BuildCurrentStoryLabel(playlist, story));
        stack.Children.Add(BuildProgress());
        stack.Children.Add(BuildTransportControls());
        stack.Children.Add(BuildSecondaryControls());
        stack.Children.Add(BuildStorySummary(story));
        stack.Children.Add(new BoxView
        {
            HeightRequest = 1,
            Margin = new Thickness(-14, 3, -14, 2),
            Color = RowStrokeColor
        });
        stack.Children.Add(new Label
        {
            Text = "Volledige speellys",
            TextColor = Colors.White,
            FontFamily = "PoppinsSemiBold",
            FontSize = 18,
            Margin = new Thickness(2, 3, 0, 0)
        });

        return stack;
    }

    private View BuildTopRow(MobilePlaylist playlist)
    {
        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            }
        };

        grid.Add(new BoxView
        {
            WidthRequest = 42,
            HeightRequest = 42,
            Color = Colors.Transparent
        }, 0);

        var title = new Label
        {
            Text = playlist.Title,
            TextColor = Colors.White,
            FontFamily = "PoppinsSemiBold",
            FontSize = 17,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.TailTruncation,
            Margin = new Thickness(8, 0)
        };
        grid.Add(title, 1);

        var favoriteButton = BuildRoundIconButton(HeartIconGlyph, 42, BuildFavoriteDescription(_currentStory));
        favoriteButton.TextColor = _currentStory?.IsFavorite == true ? PinkColor : Colors.White;
        favoriteButton.Clicked += async (_, _) => await ToggleFavoriteAsync(_currentStory);
        grid.Add(favoriteButton, 2);

        return grid;
    }

    private static Grid BuildFixedBackButtonOverlay()
    {
        var backButton = new Button
        {
            Text = BackIconGlyph,
            FontFamily = "FontAwesomeSolid",
            FontSize = 22,
            TextColor = Colors.White,
            BackgroundColor = Colors.Transparent,
            BorderWidth = 0,
            WidthRequest = 52,
            HeightRequest = 52,
            Padding = 0,
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Start
        };
        SemanticProperties.SetDescription(backButton, "Gaan terug na speellys stories");
        backButton.Clicked += async (_, _) => await Shell.Current.GoToAsync("..", animate: false);

        return new Grid
        {
            SafeAreaEdges = new SafeAreaEdges(
                SafeAreaRegions.None,
                SafeAreaRegions.Container,
                SafeAreaRegions.None,
                SafeAreaRegions.None),
            HeightRequest = 96,
            Padding = new Thickness(16, 2, 0, 0),
            BackgroundColor = Colors.Transparent,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Start,
            ZIndex = 100,
            Children = { backButton }
        };
    }

    private View BuildCover(MobileStorySummary story)
    {
        var image = new ProgressiveCachedImage(
            _apiClient,
            PageHelpers.BuildStoryImageRequest(story, _apiClient, "schink_background.jpeg"))
        {
            Aspect = Aspect.AspectFill
        };

        var cover = new Border
        {
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 24 },
            HeightRequest = 340,
            BackgroundColor = Color.FromArgb("#303030"),
            Content = image
        };
        cover.SizeChanged += (_, _) => cover.HeightRequest = Math.Min(Math.Max(cover.Width, 260), 540);
        var coverTap = new TapGestureRecognizer();
        coverTap.Tapped += async (_, _) => await TogglePlaybackAsync();
        cover.GestureRecognizers.Add(coverTap);
        SemanticProperties.SetDescription(cover, $"Speel of pouse {story.Title}");
        return cover;
    }

    private View BuildCurrentStoryLabel(MobilePlaylist playlist, MobileStorySummary story)
    {
        var index = Math.Max(0, playlist.Stories.ToList().FindIndex(candidate => SameStory(candidate, story))) + 1;
        return new Label
        {
            Text = $"{story.Title}  •  Storie {index} van {playlist.Stories.Count}",
            TextColor = AquaColor,
            FontFamily = "PoppinsSemiBold",
            FontSize = 13,
            CharacterSpacing = 0.35,
            HorizontalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.WordWrap
        };
    }

    private View BuildProgress()
    {
        _progressSlider = new Slider
        {
            Minimum = 0,
            Maximum = 1,
            MinimumTrackColor = PinkColor,
            MaximumTrackColor = Color.FromArgb("#32FFFFFF"),
            ThumbColor = PinkColor,
            HeightRequest = 28,
            IsEnabled = false
        };
        SemanticProperties.SetDescription(_progressSlider, "Spring na 'n ander plek in die storie");
        _progressSlider.DragStarted += (_, _) => _isProgressScrubbing = true;
        _progressSlider.ValueChanged += (_, args) =>
        {
            if (_isUpdatingProgressSlider)
            {
                return;
            }

            if (!_isProgressScrubbing)
            {
                _ = SeekToProgressValueAsync(args.NewValue);
                return;
            }

            if (_currentTimeLabel is null)
            {
                return;
            }

            var duration = ResolvePlaybackDuration();
            if (duration is { TotalSeconds: > 0 })
            {
                _currentTimeLabel.Text = FormatTime(
                    TimeSpan.FromSeconds(args.NewValue * duration.Value.TotalSeconds));
            }
        };
        _progressSlider.DragCompleted += async (_, _) => await CompleteProgressSeekAsync();
        _currentTimeLabel = BuildTimeLabel("0:00", TextAlignment.Start);
        _durationLabel = BuildTimeLabel("--:--", TextAlignment.End);

        var timeRow = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star)
            },
            Children = { _currentTimeLabel, _durationLabel }
        };
        Grid.SetColumn(_durationLabel, 1);

        return new VerticalStackLayout
        {
            Spacing = 6,
            Children = { _progressSlider, timeRow }
        };
    }

    private View BuildTransportControls()
    {
        var previous = BuildTransportButton(PreviousIconGlyph, "Vorige storie", HasPreviousStory());
        previous.Clicked += async (_, _) => await SelectRelativeStoryAsync(-1, autoplay: ShouldAutoplaySelection());

        _playButton = new Button
        {
            Text = IsCurrentStoryPlaying() ? PauseIconGlyph : PlayIconGlyph,
            FontFamily = "FontAwesomeSolid",
            FontSize = 19,
            TextColor = Colors.White,
            Background = new LinearGradientBrush(
                [new GradientStop(Color.FromArgb("#FF3B82"), 0), new GradientStop(PinkColor, 1)],
                new Point(0, 0),
                new Point(1, 1)),
            WidthRequest = 50,
            HeightRequest = 50,
            CornerRadius = 25,
            Padding = 0
        };
        SemanticProperties.SetDescription(_playButton, "Speel of pouse storie");
        _playButton.Clicked += async (_, _) => await TogglePlaybackAsync();

        _playLoadingIndicator = new ActivityIndicator
        {
            IsVisible = _isPlaybackRequestInFlight,
            IsRunning = _isPlaybackRequestInFlight,
            Color = Colors.White,
            WidthRequest = 23,
            HeightRequest = 23,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            InputTransparent = true
        };
        SemanticProperties.SetDescription(_playLoadingIndicator, "Storie laai");
        var playControl = new Grid
        {
            WidthRequest = 50,
            HeightRequest = 50,
            Children = { _playButton, _playLoadingIndicator }
        };
        SetPlaybackLoading(_isPlaybackRequestInFlight);

        var next = BuildTransportButton(NextIconGlyph, "Volgende storie", HasNextStory());
        next.Clicked += async (_, _) => await SelectRelativeStoryAsync(1, autoplay: ShouldAutoplaySelection());

        return new HorizontalStackLayout
        {
            Spacing = 13,
            HorizontalOptions = LayoutOptions.Center,
            Children = { previous, playControl, next }
        };
    }

    private View BuildSecondaryControls()
    {
        var speed = BuildSmallControlButton(FormatPlaybackSpeed(), $"Speelspoed {FormatPlaybackSpeed()}");
        speed.FontFamily = "PoppinsSemiBold";
        speed.FontSize = 12;
        speed.Clicked += (_, _) =>
        {
            CyclePlaybackSpeed();
            RebuildHeader();
        };

        var shuffle = BuildSmallControlButton(ShuffleIconGlyph, "Skakel skommel");
        shuffle.TextColor = _playlistPlaybackState.IsShuffleEnabled ? AquaColor : Colors.White;
        shuffle.Clicked += (_, _) =>
        {
            _playlistPlaybackState.SetShuffle(!_playlistPlaybackState.IsShuffleEnabled, _currentStory);
            _storyPlaybackSession.RefreshAutoplayPreparation();
            RebuildHeader();
        };

        var autoplay = BuildSmallControlButton(AutoplayIconGlyph, "Skakel outospeel");
        autoplay.TextColor = _playlistPlaybackState.IsAutoplayEnabled ? AquaColor : Colors.White;
        autoplay.Clicked += (_, _) =>
        {
            _playlistPlaybackState.SetAutoplay(!_playlistPlaybackState.IsAutoplayEnabled);
            _playlistPlaybackState.TrackManualStorySelection(_currentStory);
            _storyPlaybackSession.RefreshAutoplayPreparation();
            RebuildHeader();
        };

        var limitGlyph = _playlistPlaybackState.AutoplayLimitStories.HasValue ? HourglassIconGlyph : InfinityIconGlyph;
        var limit = BuildSmallControlButton(limitGlyph, FormatAutoplayLimitDescription());
        limit.TextColor = _playlistPlaybackState.AutoplayLimitStories.HasValue ? AquaColor : Colors.White;
        limit.Clicked += (_, _) =>
        {
            CycleAutoplayLimit();
            RebuildHeader();
        };

        var share = BuildSmallControlButton(ShareIconGlyph, "Deel speellys");
        share.Clicked += async (_, _) => await SharePlaylistAsync();

        return new HorizontalStackLayout
        {
            Spacing = 8,
            HorizontalOptions = LayoutOptions.Center,
            Children = { speed, shuffle, autoplay, limit, share }
        };
    }

    private View BuildStorySummary(MobileStorySummary story)
    {
        var chevron = new Label
        {
            Text = ChevronDownIconGlyph,
            FontFamily = "FontAwesomeSolid",
            FontSize = 13,
            TextColor = TextColor,
            VerticalTextAlignment = TextAlignment.Center,
            Rotation = _isSummaryExpanded ? 180 : 0
        };
        var header = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            Padding = new Thickness(15, 13),
            Children =
            {
                new Label
                {
                    Text = "Waaroor gaan hierdie storie?",
                    TextColor = TextColor,
                    FontFamily = "PoppinsSemiBold",
                    FontSize = 14
                },
                chevron
            }
        };
        Grid.SetColumn(chevron, 1);

        var content = new VerticalStackLayout
        {
            Spacing = 0,
            Children = { header }
        };
        if (_isSummaryExpanded)
        {
            content.Children.Add(new Label
            {
                Text = string.IsNullOrWhiteSpace(_currentDetail?.Summary)
                    ? story.Description
                    : _currentDetail.Summary,
                TextColor = Color.FromArgb("#E6F7F2EA"),
                FontSize = 14,
                LineHeight = 1.45,
                Padding = new Thickness(15, 0, 15, 14)
            });
        }

        var panel = new Border
        {
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 20 },
            BackgroundColor = RowColor,
            Content = content
        };
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) =>
        {
            _isSummaryExpanded = !_isSummaryExpanded;
            RebuildHeader();
        };
        panel.GestureRecognizers.Add(tap);
        return panel;
    }

    private View BuildTrackRow()
    {
        var index = new Label
        {
            TextColor = Color.FromArgb("#B8F7F2EA"),
            FontFamily = "PoppinsSemiBold",
            FontSize = 14,
            WidthRequest = 24,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center
        };
        index.SetBinding(Label.TextProperty, nameof(PlaylistTrackItem.Number));

        var image = new ProgressiveCachedImage(_apiClient)
        {
            Aspect = Aspect.AspectFill,
            HeightRequest = 56,
            WidthRequest = 56
        };
        image.SetBinding(ProgressiveCachedImage.RequestProperty, nameof(PlaylistTrackItem.ImageRequest));
        var artwork = new Border
        {
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 12 },
            HeightRequest = 56,
            WidthRequest = 56,
            Content = image
        };

        var title = new Label
        {
            TextColor = Colors.White,
            FontFamily = "PoppinsSemiBold",
            FontSize = 15,
            LineBreakMode = LineBreakMode.TailTruncation
        };
        title.SetBinding(Label.TextProperty, nameof(PlaylistTrackItem.Title));
        var status = new Label
        {
            TextColor = MutedTextColor,
            FontSize = 12.5,
            LineBreakMode = LineBreakMode.TailTruncation
        };
        status.SetBinding(Label.TextProperty, nameof(PlaylistTrackItem.Status));
        var copy = new VerticalStackLayout { Spacing = 2, Children = { title, status } };

        var favorite = BuildBareIconButton(HeartIconGlyph, "Gunsteling");
        favorite.SetBinding(Button.TextColorProperty, nameof(PlaylistTrackItem.FavoriteColor));
        favorite.Clicked += async (sender, _) =>
        {
            if ((sender as BindableObject)?.BindingContext is PlaylistTrackItem item)
            {
                await ToggleFavoriteAsync(item.Story);
            }
        };

        var action = new Label
        {
            FontFamily = "FontAwesomeSolid",
            FontSize = 14,
            TextColor = Colors.White,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center
        };
        action.SetBinding(Label.TextProperty, nameof(PlaylistTrackItem.ActionGlyph));
        action.SetBinding(OpacityProperty, nameof(PlaylistTrackItem.ActionOpacity));
        var actionLoadingIndicator = new ActivityIndicator
        {
            Color = Colors.White,
            WidthRequest = 18,
            HeightRequest = 18,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            InputTransparent = true
        };
        actionLoadingIndicator.SetBinding(ActivityIndicator.IsVisibleProperty, nameof(PlaylistTrackItem.IsLoading));
        actionLoadingIndicator.SetBinding(ActivityIndicator.IsRunningProperty, nameof(PlaylistTrackItem.IsLoading));
        SemanticProperties.SetDescription(actionLoadingIndicator, "Storie laai");
        var actionCircle = new Border
        {
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 18 },
            BackgroundColor = Color.FromArgb("#15FFFFFF"),
            HeightRequest = 36,
            WidthRequest = 36,
            Content = new Grid { Children = { action, actionLoadingIndicator } }
        };

        var grid = new Grid
        {
            Padding = new Thickness(10, 7),
            ColumnSpacing = 9,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto)
            }
        };
        grid.Add(index, 0);
        grid.Add(artwork, 1);
        grid.Add(copy, 2);
        grid.Add(favorite, 3);
        grid.Add(actionCircle, 4);

        var row = new Border
        {
            Margin = new Thickness(14, 0),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 18 },
            Content = grid
        };
        row.SetBinding(Border.BackgroundColorProperty, nameof(PlaylistTrackItem.BackgroundColor));
        row.SetBinding(Border.StrokeProperty, nameof(PlaylistTrackItem.StrokeColor));
        var tap = new TapGestureRecognizer();
        tap.Tapped += async (sender, _) =>
        {
            if ((sender as BindableObject)?.BindingContext is PlaylistTrackItem item)
            {
                await SelectStoryAsync(item.Story, autoplay: true);
            }
        };
        row.GestureRecognizers.Add(tap);
        return row;
    }

    private async Task SelectRelativeStoryAsync(int offset, bool autoplay)
    {
        if (_playlist is null || _currentStory is null)
        {
            return;
        }

        var orderedStories = _playlistPlaybackState.GetPlaybackStories(_currentStory);
        var currentIndex = orderedStories.ToList().FindIndex(story => SameStory(story, _currentStory));
        var targetIndex = currentIndex + offset;
        if (currentIndex < 0 || targetIndex < 0 || targetIndex >= orderedStories.Count)
        {
            return;
        }

        await SelectStoryAsync(orderedStories[targetIndex], autoplay);
    }

    private async Task SelectStoryAsync(MobileStorySummary story, bool autoplay)
    {
        if (story.IsLocked)
        {
            await Shell.Current.GoToAsync(nameof(PlansPage), animate: true);
            return;
        }

        var loadingTrack = autoplay
            ? _tracks.FirstOrDefault(track => SameStory(track.Story, story))
            : null;
        loadingTrack?.SetLoading(true);
        try
        {
            if (SameStory(story, _currentStory))
            {
                if (autoplay)
                {
                    await TogglePlaybackAsync();
                }
                return;
            }

            _storyPlaybackSession.Stop();

            _currentStory = story;
            _currentDetail = null;
            _loadingStoryKey = null;
            _autoplayAfterLoad = false;
            _isSummaryExpanded = false;
            _playlistPlaybackState.TrackManualStorySelection(story);
            foreach (var track in _tracks)
            {
                track.SetActive(SameStory(track.Story, story));
            }

            RebuildHeader();
            await LoadCurrentStoryAsync(autoplay);
        }
        finally
        {
            loadingTrack?.SetLoading(false);
        }
    }

    private async Task LoadCurrentStoryAsync(bool autoplay = false)
    {
        if (_currentStory is null || _currentStory.IsLocked)
        {
            return;
        }

        _autoplayAfterLoad |= autoplay;

        var key = GetStoryKey(_currentStory);
        if (_currentDetail is not null && string.Equals(GetStoryKey(_currentDetail.Story), key, StringComparison.OrdinalIgnoreCase))
        {
            if (_autoplayAfterLoad)
            {
                _autoplayAfterLoad = false;
                await StartPlaybackAsync(_currentDetail);
            }
            return;
        }

        if (string.Equals(_loadingStoryKey, key, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        _loadingStoryKey = key;
        try
        {
            var detail = await _apiClient.GetStoryAsync(_currentStory.Slug, _currentStory.Source, _loadCts.Token);
            if (!_isPageActive || detail is null || !string.Equals(GetStoryKey(detail.Story), key, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _currentDetail = detail;
            _currentStory = detail.Story;
            for (var index = 0; index < _tracks.Count; index++)
            {
                if (SameStory(_tracks[index].Story, detail.Story))
                {
                    _tracks[index].UpdateStory(
                        detail.Story,
                        PageHelpers.BuildStoryImageRequest(detail.Story, _apiClient, "schink_background.jpeg"));
                }
            }
            RebuildHeader();
            if (IsCurrentStoryPlaying())
            {
                StartProgressTimer();
            }
            if (_autoplayAfterLoad)
            {
                _autoplayAfterLoad = false;
                await StartPlaybackAsync(detail);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (_isPageActive)
            {
                await DisplayAlertAsync("Kon nie storie laai nie", ex.Message, "Maak toe");
            }
        }
        finally
        {
            if (string.Equals(_loadingStoryKey, key, StringComparison.OrdinalIgnoreCase))
            {
                _loadingStoryKey = null;
            }
        }
    }

    private async Task TogglePlaybackAsync()
    {
        if (_isPlaybackRequestInFlight)
        {
            return;
        }

        if (_storyPlaybackSession.IsCurrentStory(_currentStory) && _storyPlaybackSession.IsPlaying)
        {
            _storyPlaybackSession.Pause();
            StopProgressTimer();
            UpdatePlaybackButton();
            return;
        }

        if (_currentStory?.IsLocked == true)
        {
            await Shell.Current.GoToAsync(nameof(PlansPage), animate: true);
            return;
        }

        _isPlaybackRequestInFlight = true;
        SetPlaybackLoading(isLoading: true);
        try
        {
            if (_currentDetail is null)
            {
                await LoadCurrentStoryAsync(autoplay: true);
                return;
            }

            await StartPlaybackAsync(_currentDetail);
        }
        finally
        {
            _isPlaybackRequestInFlight = false;
            SetPlaybackLoading(isLoading: false);
        }
    }

    private void SetPlaybackLoading(bool isLoading)
    {
        if (_playButton is not null)
        {
            _playButton.IsEnabled = !isLoading;
            _playButton.TextColor = isLoading ? Colors.Transparent : Colors.White;
        }

        if (_playLoadingIndicator is not null)
        {
            _playLoadingIndicator.IsVisible = isLoading;
            _playLoadingIndicator.IsRunning = isLoading;
        }
    }

    private async Task StartPlaybackAsync(MobileStoryDetailResponse detail)
    {
        try
        {
            if (_storyPlaybackSession.IsCurrentStory(detail.Story))
            {
                await _storyPlaybackSession.ResumeAsync();
                StartProgressTimer();
                UpdatePlaybackButton();
                return;
            }

            var playbackUrl = await _apiClient.PrepareAudioPlaybackSourceAsync(
                detail.AudioUrl,
                detail.Story.Slug,
                detail.Story.Source);
            await _storyPlaybackSession.PlayAsync(
                playbackUrl,
                detail.Story,
                _apiClient.BuildImageUrl(detail.Story.ImageUrl),
                _playlist?.Slug,
                _playlist?.Title,
                detail.Story.DurationSeconds,
                originPlaylist: _playlist);
            StartProgressTimer();
            UpdatePlaybackButton();
        }
        catch (Exception ex)
        {
            UpdatePlaybackButton();
            await DisplayAlertAsync("Kon nie audio speel nie", ex.Message, "Maak toe");
        }
    }

    private async Task ToggleFavoriteAsync(MobileStorySummary? story)
    {
        if (story is null)
        {
            return;
        }

        if (!_sessionState.Current.IsSignedIn)
        {
            await DisplayAlertAsync("Teken in", "Teken eers in om gunstelinge te stoor.", "Reg so");
            return;
        }

        try
        {
            var isFavorite = await _apiClient.SetFavoriteAsync(story.Slug, story.Source, !story.IsFavorite);
            foreach (var track in _tracks.Where(track => SameStory(track.Story, story)))
            {
                track.SetFavorite(isFavorite);
            }

            if (_currentStory is not null && SameStory(_currentStory, story))
            {
                _currentStory = _currentStory with { IsFavorite = isFavorite };
                if (_currentDetail is not null)
                {
                    _currentDetail = _currentDetail with { Story = _currentDetail.Story with { IsFavorite = isFavorite } };
                }
                RebuildHeader();
            }
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Kon nie stoor nie", ex.Message, "Reg so");
        }
    }

    private async Task SharePlaylistAsync()
    {
        if (_playlist is null)
        {
            return;
        }

        var shareUrl = _currentDetail?.ShareUrl;
        if (string.IsNullOrWhiteSpace(shareUrl))
        {
            shareUrl = $"{_apiClient.BaseUrl.TrimEnd('/')}/luister/speellys/{Uri.EscapeDataString(_playlist.Slug)}";
        }

        await Share.Default.RequestAsync(new ShareTextRequest
        {
            Title = _playlist.Title,
            Text = $"Luister na {_playlist.Title} op Schink Stories",
            Uri = shareUrl
        });
    }

    private void SubscribePlaybackEvents()
    {
        if (_isPlaybackEventSubscribed)
        {
            return;
        }

        _audioPlaybackService.PlaybackEnded += OnPlaybackEnded;
        _audioPlaybackService.PlaybackStateChanged += OnPlaybackStateChanged;
        _storyPlaybackSession.AutoplayAdvanced += OnAutoplayAdvanced;
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
        _storyPlaybackSession.AutoplayAdvanced -= OnAutoplayAdvanced;
        _isPlaybackEventSubscribed = false;
    }

    private void OnPlaybackStateChanged(object? sender, EventArgs args) =>
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (!_isPageActive)
            {
                return;
            }

            UpdatePlaybackButton();
            if (IsCurrentStoryPlaying())
            {
                StartProgressTimer();
            }
            else
            {
                StopProgressTimer();
                UpdateProgressState();
            }
        });

    private void OnPlaybackEnded(object? sender, EventArgs args) =>
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (!_storyPlaybackSession.IsCurrentStory(_currentStory))
            {
                return;
            }

            UpdatePlaybackButton();
            StopProgressTimer();
            UpdateProgressState();
        });

    private void OnAutoplayAdvanced(object? sender, StoryAutoplayAdvancedEventArgs args) =>
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (!_isPageActive ||
                _playlist is null ||
                !string.Equals(_playlist.Slug, args.Playlist.Slug, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _playlist = args.Playlist;
            _currentStory = args.Detail.Story;
            _currentDetail = args.Detail;
            _loadingStoryKey = null;
            _autoplayAfterLoad = false;
            for (var index = 0; index < _tracks.Count; index++)
            {
                var track = _tracks[index];
                track.SetActive(SameStory(track.Story, _currentStory));
                if (SameStory(track.Story, _currentStory))
                {
                    track.UpdateStory(
                        _currentStory,
                        PageHelpers.BuildStoryImageRequest(_currentStory, _apiClient, "schink_background.jpeg"));
                }
            }

            RebuildHeader();
            StartProgressTimer();
        });

    private void StartProgressTimer()
    {
        _progressTimer ??= Dispatcher.CreateTimer();
        _progressTimer.Interval = TimeSpan.FromMilliseconds(500);
        _progressTimer.Tick -= ProgressTimerTick;
        _progressTimer.Tick += ProgressTimerTick;
        UpdateProgressState();
        _progressTimer.Start();
    }

    private void ProgressTimerTick(object? sender, EventArgs args) => UpdateProgressState();

    private void StopProgressTimer() => _progressTimer?.Stop();

    private void UpdateProgressState()
    {
        var isCurrentStory = _storyPlaybackSession.IsCurrentStory(_currentStory);
        var position = isCurrentStory ? _storyPlaybackSession.CurrentPosition : TimeSpan.Zero;
        var duration = (isCurrentStory ? _storyPlaybackSession.Duration : null) ??
            (_currentStory?.DurationSeconds is > 0 ? TimeSpan.FromSeconds((double)_currentStory.DurationSeconds.Value) : null);

        if (_currentTimeLabel is not null && !_isProgressScrubbing)
        {
            _currentTimeLabel.Text = FormatTime(position);
        }
        if (_durationLabel is not null)
        {
            _durationLabel.Text = duration.HasValue ? FormatTime(duration.Value) : "--:--";
        }
        if (_progressSlider is not null)
        {
            _progressSlider.IsEnabled = isCurrentStory && duration is { TotalSeconds: > 0 };
            if (!_isProgressScrubbing)
            {
                _isUpdatingProgressSlider = true;
                try
                {
                    _progressSlider.Value = duration is { TotalSeconds: > 0 }
                        ? Math.Clamp(position.TotalSeconds / duration.Value.TotalSeconds, 0, 1)
                        : 0;
                }
                finally
                {
                    _isUpdatingProgressSlider = false;
                }
            }
        }
    }

    private async Task CompleteProgressSeekAsync()
    {
        try
        {
            var duration = ResolvePlaybackDuration();
            if (_progressSlider is not null && duration is { TotalSeconds: > 0 })
            {
                await SeekToProgressValueAsync(_progressSlider.Value);
            }
        }
        finally
        {
            _isProgressScrubbing = false;
            UpdateProgressState();
        }
    }

    private TimeSpan? ResolvePlaybackDuration() =>
        _storyPlaybackSession.Duration ??
        (_currentStory?.DurationSeconds is > 0
            ? TimeSpan.FromSeconds((double)_currentStory.DurationSeconds.Value)
            : null);

    private async Task SeekToProgressValueAsync(double value)
    {
        var duration = ResolvePlaybackDuration();
        if (!_storyPlaybackSession.IsCurrentStory(_currentStory) ||
            duration is not { TotalSeconds: > 0 })
        {
            return;
        }

        await _storyPlaybackSession.SeekAsync(
            TimeSpan.FromSeconds(Math.Clamp(value, 0, 1) * duration.Value.TotalSeconds));
    }

    private void UpdatePlaybackButton()
    {
        if (_playButton is not null)
        {
            _playButton.Text = IsCurrentStoryPlaying() ? PauseIconGlyph : PlayIconGlyph;
        }
    }

    private bool IsCurrentStoryPlaying() =>
        _storyPlaybackSession.IsCurrentStory(_currentStory) && _storyPlaybackSession.IsPlaying;

    private bool HasPreviousStory() => ResolveRelativeStory(-1) is not null;

    private bool HasNextStory() => ResolveRelativeStory(1) is not null;

    private MobileStorySummary? ResolveRelativeStory(int offset)
    {
        if (_currentStory is null)
        {
            return null;
        }

        var stories = _playlistPlaybackState.GetPlaybackStories(_currentStory);
        var index = stories.ToList().FindIndex(story => SameStory(story, _currentStory));
        var target = index + offset;
        return index >= 0 && target >= 0 && target < stories.Count ? stories[target] : null;
    }

    private bool ShouldAutoplaySelection() =>
        _storyPlaybackSession.IsPlaying || _playlistPlaybackState.IsAutoplayEnabled;

    private void CyclePlaybackSpeed()
    {
        var current = _audioPlaybackService.PlaybackSpeed;
        var index = Array.FindIndex(PlaybackSpeedSteps, speed => Math.Abs(speed - current) < 0.001);
        _audioPlaybackService.SetPlaybackSpeed(PlaybackSpeedSteps[index < 0 ? 1 : (index + 1) % PlaybackSpeedSteps.Length]);
    }

    private void CycleAutoplayLimit()
    {
        int? next = _playlistPlaybackState.AutoplayLimitStories switch
        {
            null => 3,
            3 => 5,
            _ => null
        };
        _playlistPlaybackState.SetAutoplayLimit(next, _currentStory);
        _storyPlaybackSession.RefreshAutoplayPreparation();
    }

    private string FormatAutoplayLimitDescription() =>
        _playlistPlaybackState.AutoplayLimitStories is { } limit
            ? $"Stop na {limit} stories"
            : "Geen outospeellimiet";

    private string FormatPlaybackSpeed() => $"{_audioPlaybackService.PlaybackSpeed:0.##}x";

    private static Label BuildTimeLabel(string text, TextAlignment alignment) =>
        new()
        {
            Text = text,
            TextColor = MutedTextColor,
            FontSize = 12,
            HorizontalTextAlignment = alignment
        };

    private static Button BuildTransportButton(string glyph, string description, bool isEnabled)
    {
        var button = new Button
        {
            Text = glyph,
            FontFamily = "FontAwesomeSolid",
            FontSize = 22,
            TextColor = Colors.White,
            BackgroundColor = Color.FromArgb("#15FFFFFF"),
            WidthRequest = 50,
            HeightRequest = 50,
            CornerRadius = 25,
            Padding = 0,
            IsEnabled = isEnabled,
            Opacity = isEnabled ? 1 : 0.35
        };
        SemanticProperties.SetDescription(button, description);
        return button;
    }

    private static Button BuildRoundIconButton(string glyph, double size, string description)
    {
        var button = new Button
        {
            Text = glyph,
            FontFamily = "FontAwesomeSolid",
            FontSize = 17,
            TextColor = Colors.White,
            BackgroundColor = Color.FromArgb("#15FFFFFF"),
            HeightRequest = size,
            WidthRequest = size,
            CornerRadius = (int)(size / 2),
            Padding = 0
        };
        SemanticProperties.SetDescription(button, description);
        return button;
    }

    private static Button BuildSmallControlButton(string text, string description)
    {
        var button = new Button
        {
            Text = text,
            FontFamily = "FontAwesomeSolid",
            FontSize = 15,
            TextColor = Colors.White,
            BackgroundColor = Color.FromArgb("#15FFFFFF"),
            WidthRequest = 43,
            HeightRequest = 39,
            CornerRadius = 19,
            Padding = 0
        };
        SemanticProperties.SetDescription(button, description);
        return button;
    }

    private static Button BuildBareIconButton(string glyph, string description)
    {
        var button = new Button
        {
            Text = glyph,
            FontFamily = "FontAwesomeSolid",
            FontSize = 16,
            TextColor = Colors.White,
            BackgroundColor = Colors.Transparent,
            WidthRequest = 36,
            HeightRequest = 36,
            Padding = 0
        };
        SemanticProperties.SetDescription(button, description);
        return button;
    }

    private static string BuildFavoriteDescription(MobileStorySummary? story) =>
        story?.IsFavorite == true ? "Verwyder uit gunstelinge" : "Voeg by gunstelinge";

    private bool IsCurrentStory(MobileStorySummary story) => SameStory(story, _currentStory);

    private static bool SameStory(MobileStorySummary? left, MobileStorySummary? right) =>
        left is not null && right is not null &&
        string.Equals(left.Slug, right.Slug, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(left.Source, right.Source, StringComparison.OrdinalIgnoreCase);

    private static string GetStoryKey(MobileStorySummary story) => $"{story.Source}|{story.Slug}";

    private static string FormatTime(TimeSpan value) =>
        value.TotalHours >= 1
            ? $"{(int)value.TotalHours}:{value.Minutes:00}:{value.Seconds:00}"
            : $"{(int)value.TotalMinutes}:{value.Seconds:00}";

    private sealed class PlaylistTrackItem : INotifyPropertyChanged
    {
        private bool _isActive;
        private bool _isLoading;
        private MobileStorySummary _story;

        public PlaylistTrackItem(
            int number,
            MobileStorySummary story,
            bool isActive,
            ProgressiveImageRequest imageRequest)
        {
            Number = number;
            _story = story;
            _isActive = isActive;
            ImageRequest = imageRequest;
        }

        public int Number { get; }
        public MobileStorySummary Story => _story;
        public string Title => _story.Title;
        public ProgressiveImageRequest ImageRequest { get; private set; }
        public string Status => _story.IsLocked ? "Intekening nodig" : _isActive ? "Speel nou" : "Speel in speellys";
        public string ActionGlyph => _story.IsLocked ? "\uf023" : _isActive ? VolumeIconGlyph : PlayIconGlyph;
        public double ActionOpacity => _isLoading ? 0 : 1;
        public bool IsLoading => _isLoading;
        public Color BackgroundColor => _isActive ? ActiveRowColor : RowColor;
        public Color StrokeColor => _isActive ? ActiveRowStrokeColor : RowStrokeColor;
        public Color FavoriteColor => _story.IsFavorite ? PinkColor : Colors.White;

        public event PropertyChangedEventHandler? PropertyChanged;

        public void SetActive(bool isActive)
        {
            if (_isActive == isActive)
            {
                return;
            }

            _isActive = isActive;
            Notify(nameof(Status));
            Notify(nameof(ActionGlyph));
            Notify(nameof(BackgroundColor));
            Notify(nameof(StrokeColor));
        }

        public void SetFavorite(bool isFavorite)
        {
            _story = _story with { IsFavorite = isFavorite };
            Notify(nameof(FavoriteColor));
        }

        public void SetLoading(bool isLoading)
        {
            if (_isLoading == isLoading)
            {
                return;
            }

            _isLoading = isLoading;
            Notify(nameof(IsLoading));
            Notify(nameof(ActionOpacity));
        }

        public void UpdateStory(MobileStorySummary story, ProgressiveImageRequest imageRequest)
        {
            _story = story;
            ImageRequest = imageRequest;
            Notify(nameof(Title));
            Notify(nameof(ImageRequest));
            Notify(nameof(Status));
            Notify(nameof(ActionGlyph));
            Notify(nameof(FavoriteColor));
        }

        private void Notify([CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
