using System.Text.Json;
using Shink.Mobile.Models;
using Shink.Mobile.Services;

namespace Shink.Mobile.Pages;

public sealed class DownloadedPage : ContentPage
{
    private const double FloatingTopBarContentInset = 92;
    private const double BottomBarContentInset = 216;
    private const double BottomBarOverlayHeight = MobileBottomBar.NavigationHeight;
    private const string DownloadOrderPreferenceKey = "schink_downloaded_story_order_v1";
    private const string DraggedDownloadRowProperty = "schink.downloaded-story-row";
    private static readonly Color PageBackgroundColor = Color.FromArgb("#46969E");
    private static readonly Color TextColor = Color.FromArgb("#1B2231");
    private static readonly Color MutedTextColor = Color.FromArgb("#69716D");
    private static readonly Color AccentColor = Color.FromArgb("#123F3F");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly MobileApiClient _apiClient;
    private readonly SessionState _sessionState;
    private readonly IOfflineStoryDownloadService _offlineDownloadService;
    private readonly StoryPlaybackSession _storyPlaybackSession;
    private readonly PlayerTransitionBackdropState _transitionBackdropState;
    private readonly VerticalStackLayout _content;
    private readonly Border _topBarHost;
    private readonly VerticalStackLayout _rows = new() { Spacing = 18 };
    private List<OfflineStoryDownload> _orderedDownloads = [];
    private Border? _draggingRow;
    private int? _pendingDropDestinationIndex;
    private bool _reloadAfterDrag;
    private bool _isStartingPlaylist;

    public DownloadedPage(
        MobileApiClient apiClient,
        SessionState sessionState,
        IOfflineStoryDownloadService offlineDownloadService,
        StoryPlaybackSession storyPlaybackSession,
        PlayerTransitionBackdropState transitionBackdropState)
    {
        _apiClient = apiClient;
        _sessionState = sessionState;
        _offlineDownloadService = offlineDownloadService;
        _storyPlaybackSession = storyPlaybackSession;
        _transitionBackdropState = transitionBackdropState;

        Title = "Afgelaai";
        BackgroundColor = PageBackgroundColor;
        SafeAreaEdges = SafeAreaEdges.None;
        Shell.SetNavBarIsVisible(this, false);
        Shell.SetTabBarIsVisible(this, false);

        _content = new VerticalStackLayout
        {
            Padding = new Thickness(20, FloatingTopBarContentInset, 20, BottomBarContentInset),
            Spacing = 18
        };
        MobileResponsiveLayout.ApplyCenteredContent(_content, Width, 820);
        var drop = new DropGestureRecognizer();
        drop.DragOver += HandleDragOver;
        drop.Drop += HandleRowDrop;
        _rows.GestureRecognizers.Add(drop);

        var scrollView = new ScrollView
        {
            // The floating navbar already occupies the top safe-area band. Keep
            // the scrollable header below that band while the page artwork stays edge-to-edge.
            SafeAreaEdges = new SafeAreaEdges(
                SafeAreaRegions.None,
                SafeAreaRegions.Container,
                SafeAreaRegions.None,
                SafeAreaRegions.None),
            BackgroundColor = Colors.Transparent,
            Content = _content
        };

        _topBarHost = new Border
        {
            BackgroundColor = Colors.Transparent,
            StrokeThickness = 0,
            Padding = 0,
            Margin = Thickness.Zero,
            HeightRequest = 62,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Start,
            ZIndex = 101,
            Content = BuildTopBar()
        };

        var topBarOverlay = new Grid
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
            ZIndex = 100,
            Children = { _topBarHost }
        };
        var topBarBackdropLayer = MobileTopBar.BuildStoriesBackdropLayer(topBarOverlay);

        var bottomBarOverlay = new Grid
        {
            SafeAreaEdges = SafeAreaEdges.None,
            BackgroundColor = Colors.Transparent,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.End,
            HeightRequest = BottomBarOverlayHeight,
            InputTransparent = false,
            ZIndex = 100,
            Children =
            {
                new ContentView
                {
                    Content = MobileBottomBar.Build(this, "downloads"),
                    HorizontalOptions = LayoutOptions.Fill,
                    VerticalOptions = LayoutOptions.End,
                    HeightRequest = BottomBarOverlayHeight
                }
            }
        };

        Content = new Grid
        {
            SafeAreaEdges = SafeAreaEdges.None,
            BackgroundColor = PageBackgroundColor,
            Children =
            {
                new Image
                {
                    Source = "schink_background.jpeg",
                    Aspect = Aspect.AspectFill,
                    Opacity = 0.22,
                    HorizontalOptions = LayoutOptions.Fill,
                    VerticalOptions = LayoutOptions.Fill,
                    InputTransparent = true
                },
                scrollView,
                topBarBackdropLayer,
                topBarOverlay,
                bottomBarOverlay,
                new PersistentNowPlayingBar(_storyPlaybackSession)
                {
                    Margin = new Thickness(10, 0, 10, 124),
                    ZIndex = 120
                }
            }
        };

        SizeChanged += (_, _) => ApplyResponsiveLayout();
        ApplyResponsiveLayout();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _offlineDownloadService.DownloadsChanged -= OnDownloadsChanged;
        _offlineDownloadService.DownloadsChanged += OnDownloadsChanged;
        _topBarHost.Content = BuildTopBar();
        await LoadAsync();
    }

    protected override void OnDisappearing()
    {
        if (_draggingRow is { } row)
        {
            _pendingDropDestinationIndex = null;
            _reloadAfterDrag = false;
            FinishDragging(row);
        }

        _offlineDownloadService.DownloadsChanged -= OnDownloadsChanged;
        base.OnDisappearing();
    }

    private void OnDownloadsChanged(object? sender, EventArgs args) =>
        MainThread.BeginInvokeOnMainThread(async () => await LoadAsync());

    private async Task LoadAsync()
    {
        if (_draggingRow is not null)
        {
            _reloadAfterDrag = true;
            return;
        }

        _content.Children.Clear();
        _content.Children.Add(BuildHeader());
        _content.Children.Add(new ActivityIndicator
        {
            IsRunning = true,
            Color = AccentColor,
            HorizontalOptions = LayoutOptions.Center
        });

        try
        {
            var downloads = await _offlineDownloadService.GetPlayableDownloadsAsync();
            if (_draggingRow is not null)
            {
                _reloadAfterDrag = true;
                return;
            }

            _orderedDownloads = ApplySavedOrder(downloads);
            _content.Children.Clear();
            _rows.Children.Clear();
            _content.Children.Add(BuildHeader(_orderedDownloads));

            if (_orderedDownloads.Count == 0)
            {
                _content.Children.Add(BuildEmptyState());
                return;
            }

            foreach (var download in _orderedDownloads)
            {
                _rows.Children.Add(BuildDownloadRow(download));
            }
            _content.Children.Add(_rows);
        }
        catch (Exception ex)
        {
            _content.Children.Clear();
            _content.Children.Add(BuildHeader());
            _content.Children.Add(new Label
            {
                Text = ex.Message,
                TextColor = Color.FromArgb("#B42318"),
                FontSize = 15
            });
        }
    }

    private View BuildHeader(IReadOnlyList<OfflineStoryDownload>? downloads = null)
    {
        var title = new Label
        {
            Text = "Afgelaai",
            FontSize = 26,
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.White,
            HorizontalTextAlignment = TextAlignment.Start
        };
        var subtitle = new Label
        {
            Text = "Stories gereed vir offline luister.",
            FontSize = 14,
            TextColor = Colors.White,
            HorizontalTextAlignment = TextAlignment.Start
        };
        var heading = new VerticalStackLayout
        {
            Spacing = 4,
            VerticalOptions = LayoutOptions.Center,
            Children = { title, subtitle }
        };

        if (downloads is { Count: > 0 })
        {
            heading.Children.Add(new Label
            {
                Text = "Hou vas en sleep om te rangskik.",
                FontSize = 11,
                TextColor = Colors.White,
                Opacity = 0.82
            });
        }

        var header = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 12,
            Children = { heading }
        };

        if (downloads is not { Count: > 0 } || downloads.All(item => item.StoryType == "video"))
        {
            return header;
        }

        var playAllButton = new Button
        {
            Text = downloads.Any(item => item.StoryType == "video") ? "▶  Speel oudiostories" : "▶  Speel alles",
            FontFamily = "PoppinsSemiBold",
            FontSize = 13,
            TextColor = Colors.White,
            BackgroundColor = AccentColor,
            BorderWidth = 0,
            CornerRadius = 18,
            HeightRequest = 38,
            Padding = new Thickness(14, 0),
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Start,
            AutomationId = "downloads-play-all"
        };
        SemanticProperties.SetDescription(playAllButton, "Speel alle afgelaaide stories");
        playAllButton.Clicked += async (_, _) => await PlayAllDownloadsAsync(downloads, playAllButton);
        header.Children.Add(playAllButton);
        Grid.SetColumn(playAllButton, 1);
        return header;
    }

    private async Task PlayAllDownloadsAsync(
        IReadOnlyList<OfflineStoryDownload> downloads,
        Button playAllButton)
    {
        if (_isStartingPlaylist || downloads.Count == 0)
        {
            return;
        }

        _isStartingPlaylist = true;
        playAllButton.IsEnabled = false;
        var originalText = playAllButton.Text;
        playAllButton.Text = "Begin...";
        try
        {
            var currentDownloads = ApplySavedOrder(await _offlineDownloadService.GetPlayableDownloadsAsync());
            var queue = CreateOfflineQueue(currentDownloads, autoplay: true);
            if (queue.Stories.Count == 0)
            {
                await DisplayAlertAsync(
                    "Geen aflaaie nie",
                    "Daar is nie tans enige stories wat offline gespeel kan word nie.",
                    "Reg so");
                return;
            }

            var firstStory = queue.Stories[0];
            var firstDownload = currentDownloads.First(item => item.Slug == firstStory.Slug && item.Source == firstStory.Source);
            var firstDetail = _offlineDownloadService.CreateOfflineDetail(firstDownload);
            var playbackUrl = await _offlineDownloadService.ResolvePlayableAudioAsync(firstDetail);
            if (string.IsNullOrWhiteSpace(playbackUrl))
            {
                throw new InvalidOperationException("Die eerste afgelaaide storie is nie meer beskikbaar nie.");
            }

            _storyPlaybackSession.Stop();

            await _storyPlaybackSession.PlayAsync(
                playbackUrl,
                firstStory,
                _apiClient.BuildImageUrl(firstStory.ImageUrl),
                queue.Slug,
                queue.Title,
                firstStory.DurationSeconds,
                playbackQueue: queue);
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Kon nie aflaaie speel nie", ex.Message, "Maak toe");
        }
        finally
        {
            playAllButton.Text = originalText;
            playAllButton.IsEnabled = true;
            _isStartingPlaylist = false;
        }
    }

    private View BuildTopBar() =>
        MobileTopBar.BuildStoriesTopBar(
            this,
            _apiClient,
            _sessionState.Current,
            notificationAction: OpenNotificationsAsync);

    private static Task OpenNotificationsAsync() =>
        Shell.Current.GoToAsync(nameof(KennisgewingsPage), animate: false);

    private void ApplyResponsiveLayout()
    {
        var width = MobileResponsiveLayout.ResolveWidth(Width);
        MobileResponsiveLayout.ApplyCenteredContent(_content, width, 820);

        if (DeviceInfo.Current.Platform == DevicePlatform.Android)
        {
            var phoneChromeWidth = Math.Max(280, width - 36);
            _topBarHost.WidthRequest = phoneChromeWidth;
            _topBarHost.MaximumWidthRequest = phoneChromeWidth;
            _topBarHost.HorizontalOptions = LayoutOptions.Center;
            return;
        }

        MobileResponsiveLayout.ApplyStoriesTopBar(_topBarHost, width, 1040);
    }

    private static View BuildEmptyState() =>
        new Border
        {
            BackgroundColor = Colors.White,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 24 },
            Padding = 20,
            Content = new VerticalStackLayout
            {
                Spacing = 8,
                Children =
                {
                    new Label
                    {
                        Text = "Geen afgelaaide stories nie",
                        FontSize = 20,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = TextColor,
                        HorizontalTextAlignment = TextAlignment.Center
                    },
                    new Label
                    {
                        Text = "Laai stories van die speler af om hulle hier te sien.",
                        FontSize = 15,
                        TextColor = MutedTextColor,
                        HorizontalTextAlignment = TextAlignment.Center
                    }
                }
            }
        };

    private View BuildDownloadRow(OfflineStoryDownload download)
    {
        var story = _offlineDownloadService.CreateOfflineStory(download);
        var playButton = new Border
        {
            BackgroundColor = Colors.White,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 24 },
            WidthRequest = 48,
            HeightRequest = 48,
            Content = new Label
            {
                Text = "▶",
                FontSize = 22,
                TextColor = AccentColor,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center,
                Margin = new Thickness(3, 0, 0, 0)
            }
        };

        var detailStack = new VerticalStackLayout
        {
            Spacing = 4,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Label
                {
                    Text = download.Title,
                    FontSize = 17,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = TextColor,
                    MaxLines = 2,
                    LineBreakMode = LineBreakMode.TailTruncation
                },
                new Label
                {
                    Text = BuildMetaText(download),
                    FontSize = 13,
                    TextColor = MutedTextColor,
                    MaxLines = 1,
                    LineBreakMode = LineBreakMode.TailTruncation
                }
            }
        };

        var grid = new Grid
        {
            InputTransparent = true,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 12,
            Children =
            {
                new Border
                {
                    StrokeThickness = 0,
                    StrokeShape = new RoundRectangle { CornerRadius = 12 },
                    WidthRequest = 78,
                    HeightRequest = 78,
                    Content = new ProgressiveCachedImage(
                        _apiClient,
                        PageHelpers.BuildStoryImageRequest(story, _apiClient, "schink_background.jpeg"))
                    {
                        Aspect = Aspect.AspectFill,
                        WidthRequest = 78,
                        HeightRequest = 78
                    }
                },
                detailStack,
                playButton
            }
        };
        grid.SetColumn(detailStack, 1);
        grid.SetColumn(playButton, 2);

        var row = new Border
        {
            BackgroundColor = Colors.White,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 18 },
            Padding = 10,
            Content = grid
        };

        var rowTap = new TapGestureRecognizer();
        rowTap.Tapped += async (_, _) =>
        {
            if (_draggingRow is null)
                await OpenDownloadedStoryAsync(download);
        };
        row.GestureRecognizers.Add(rowTap);

        var drag = new DragGestureRecognizer();
        drag.DragStarting += (_, args) => args.Data.Properties.Add(DraggedDownloadRowProperty, row);
        drag.DropCompleted += (_, _) => FinishDragging(row);
        row.GestureRecognizers.Add(drag);

        SemanticProperties.SetDescription(
            row,
            $"{download.Title}. Tik om te speel. Hou vas en sleep om die volgorde te verander.");

        return row;
    }

    private void HandleDragOver(object? sender, DragEventArgs args)
    {
        if (args.Data?.Properties.TryGetValue(DraggedDownloadRowProperty, out var source) != true ||
            source is not Border row || row.Parent != _rows ||
            (_draggingRow is not null && _draggingRow != row))
        {
            args.AcceptedOperation = DataPackageOperation.None;
            return;
        }

        if (_draggingRow == row)
            return;

        // UIKit can request drag data for a stationary hold without starting a
        // session or sending DropCompleted. Only an actual drag enters this state.
        _draggingRow = row;
        row.Opacity = 0.86;
        row.Scale = 1.02;
        SafeHapticFeedback.TryPerform(HapticFeedbackType.LongPress);
    }

    private void HandleRowDrop(object? sender, DropEventArgs args)
    {
        if (_draggingRow is not { } row ||
            args.Data?.Properties.TryGetValue(DraggedDownloadRowProperty, out var source) != true ||
            !ReferenceEquals(source, row) || args.GetPosition(_rows) is not { } position)
        {
            return;
        }

        args.Handled = true;
        // Each card's midpoint separates "before" from "after", including
        // the space between cards. Exclude the source from the final index.
        _pendingDropDestinationIndex = _rows.Children.Count(child =>
            !ReferenceEquals(child, row) && child.Frame.Center.Y <= position.Y);
    }

    private void MoveRow(Border row, int newIndex)
    {
        var oldIndex = _rows.Children.IndexOf(row);
        if (oldIndex < 0 || oldIndex == newIndex)
        {
            return;
        }

        _rows.Children.RemoveAt(oldIndex);
        _rows.Children.Insert(newIndex, row);
        var download = _orderedDownloads[oldIndex];
        _orderedDownloads.RemoveAt(oldIndex);
        _orderedDownloads.Insert(newIndex, download);
        SaveDownloadOrder();
    }

    private void FinishDragging(Border row)
    {
        if (!ReferenceEquals(_draggingRow, row))
        {
            return;
        }

        // DropCompleted runs inside Android's DragAction.Ended dispatch, which
        // enumerates the native children interested in the drag. Moving a row
        // or changing its ZIndex there modifies that collection and can throw
        // ConcurrentModificationException. Post all tree changes to the next
        // UI turn, after the native dispatch has returned.
        Dispatcher.Dispatch(async () =>
        {
            if (!ReferenceEquals(_draggingRow, row))
            {
                return;
            }

            _draggingRow = null;
            var destinationIndex = _pendingDropDestinationIndex;
            _pendingDropDestinationIndex = null;
            if (destinationIndex is { } index)
            {
                MoveRow(row, index);
            }

            row.Scale = 1;
            row.Opacity = 1;

            if (_reloadAfterDrag)
            {
                _reloadAfterDrag = false;
                await LoadAsync();
            }
        });
    }

    private static List<OfflineStoryDownload> ApplySavedOrder(
        IReadOnlyList<OfflineStoryDownload> downloads)
    {
        var defaultOrder = downloads.ToList();
        var savedJson = Preferences.Default.Get(DownloadOrderPreferenceKey, string.Empty);
        if (string.IsNullOrWhiteSpace(savedJson))
        {
            return defaultOrder;
        }

        try
        {
            var savedKeys = JsonSerializer.Deserialize<string[]>(savedJson, JsonOptions);
            if (savedKeys is not { Length: > 0 })
            {
                return defaultOrder;
            }

            var downloadsByKey = defaultOrder
                .GroupBy(BuildDownloadOrderKey, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            var ordered = new List<OfflineStoryDownload>(defaultOrder.Count);
            var orderedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var key in savedKeys)
            {
                if (key is not null &&
                    downloadsByKey.TryGetValue(key, out var download) &&
                    orderedKeys.Add(key))
                {
                    ordered.Add(download);
                }
            }

            ordered.AddRange(defaultOrder.Where(download =>
                orderedKeys.Add(BuildDownloadOrderKey(download))));
            return ordered;
        }
        catch (JsonException)
        {
            Preferences.Default.Remove(DownloadOrderPreferenceKey);
            return defaultOrder;
        }
    }

    private void SaveDownloadOrder()
    {
        var keys = _orderedDownloads.Select(BuildDownloadOrderKey).ToArray();
        Preferences.Default.Set(DownloadOrderPreferenceKey, JsonSerializer.Serialize(keys, JsonOptions));
    }

    private static string BuildDownloadOrderKey(OfflineStoryDownload download) =>
        $"{download.Source.Trim().ToLowerInvariant()}:{download.Slug.Trim().ToLowerInvariant()}";

    private async Task OpenDownloadedStoryAsync(OfflineStoryDownload download)
    {
        await CapturePlayerTransitionBackdropAsync();
        var story = _offlineDownloadService.CreateOfflineStory(download);
        var parameters = new Dictionary<string, object> { ["preview"] = story };
        if (!string.Equals(story.StoryType, "video", StringComparison.OrdinalIgnoreCase))
        {
            var queue = CreateOfflineQueue(_orderedDownloads, autoplay: false);
            var activeQueue = _storyPlaybackSession.Current?.PlaybackQueue;
            parameters["playbackQueue"] = _storyPlaybackSession.IsCurrentStory(story) &&
                activeQueue is { IsOfflineQueue: true } && activeQueue.Stories.SequenceEqual(queue.Stories)
                ? activeQueue
                : queue;
        }
        await Shell.Current.GoToAsync(
            $"{nameof(StoryDetailPage)}?slug={Uri.EscapeDataString(download.Slug)}&source={Uri.EscapeDataString(download.Source)}",
            animate: false,
            parameters: parameters);
    }

    private PlaylistPlaybackState CreateOfflineQueue(IEnumerable<OfflineStoryDownload> downloads, bool autoplay) =>
        PlaylistPlaybackState.CreateOfflineQueue(downloads.Select(_offlineDownloadService.CreateOfflineStory), autoplay);

    private async Task CapturePlayerTransitionBackdropAsync()
    {
        try
        {
            await _transitionBackdropState.CaptureAsync();
        }
        catch
        {
            // Transition backdrop capture should never block opening the player.
        }
    }

    private static string BuildMetaText(OfflineStoryDownload download)
    {
        var duration = download.DurationSeconds is { } seconds && seconds > 0
            ? FormatDuration(TimeSpan.FromSeconds((double)seconds))
            : null;
        var source = string.Equals(download.Source, "gratis", StringComparison.OrdinalIgnoreCase)
            ? "Gratis"
            : "Schink Stories";

        return string.IsNullOrWhiteSpace(duration)
            ? source
            : $"{source} - {duration}";
    }

    private static string FormatDuration(TimeSpan duration) =>
        duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours}:{duration.Minutes:00}:{duration.Seconds:00}"
            : $"{duration.Minutes}:{duration.Seconds:00}";

}
