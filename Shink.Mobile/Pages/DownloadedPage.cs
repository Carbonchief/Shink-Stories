using Shink.Mobile.Models;
using Shink.Mobile.Services;

namespace Shink.Mobile.Pages;

public sealed class DownloadedPage : ContentPage
{
    private static readonly Color PageBackgroundColor = Color.FromArgb("#FFF7E8");
    private static readonly Color TextColor = Color.FromArgb("#1B2231");
    private static readonly Color MutedTextColor = Color.FromArgb("#69716D");
    private static readonly Color AccentColor = Color.FromArgb("#123F3F");

    private readonly MobileApiClient _apiClient;
    private readonly IOfflineStoryDownloadService _offlineDownloadService;
    private readonly PlaylistPlaybackState _playlistPlaybackState;
    private readonly PlayerTransitionBackdropState _transitionBackdropState;
    private readonly VerticalStackLayout _content;

    public DownloadedPage(
        MobileApiClient apiClient,
        IOfflineStoryDownloadService offlineDownloadService,
        PlaylistPlaybackState playlistPlaybackState,
        PlayerTransitionBackdropState transitionBackdropState)
    {
        _apiClient = apiClient;
        _offlineDownloadService = offlineDownloadService;
        _playlistPlaybackState = playlistPlaybackState;
        _transitionBackdropState = transitionBackdropState;

        Title = "Downloaded";
        BackgroundColor = PageBackgroundColor;
        SafeAreaEdges = new SafeAreaEdges(SafeAreaRegions.Container);
        Shell.SetNavBarIsVisible(this, false);
        Shell.SetTabBarIsVisible(this, false);

        _content = new VerticalStackLayout
        {
            Padding = new Thickness(20, 18, 20, 28),
            Spacing = 18
        };

        Content = new ScrollView
        {
            BackgroundColor = PageBackgroundColor,
            Content = _content
        };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
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
            _content.Children.Clear();
            _content.Children.Add(BuildHeader());

            if (downloads.Count == 0)
            {
                _content.Children.Add(BuildEmptyState());
                return;
            }

            foreach (var download in downloads)
            {
                _content.Children.Add(BuildDownloadRow(download));
            }
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

    private View BuildHeader()
    {
        var backButton = new Border
        {
            BackgroundColor = Colors.White,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 23 },
            WidthRequest = 46,
            HeightRequest = 46,
            Content = new Label
            {
                Text = "‹",
                FontSize = 34,
                TextColor = AccentColor,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, -4, 0, 0)
            }
        };
        var backTap = new TapGestureRecognizer();
        backTap.Tapped += async (_, _) => await Shell.Current.GoToAsync("..", animate: true);
        backButton.GestureRecognizers.Add(backTap);

        var titleStack = new VerticalStackLayout
        {
            Spacing = 2,
            HorizontalOptions = LayoutOptions.Center,
            Children =
            {
                new Label
                {
                    Text = "Downloaded",
                    FontSize = 26,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = TextColor,
                    HorizontalTextAlignment = TextAlignment.Center
                },
                new Label
                {
                    Text = "Stories gereed vir offline luister.",
                    FontSize = 14,
                    TextColor = MutedTextColor,
                    HorizontalTextAlignment = TextAlignment.Center
                }
            }
        };

        var headerSpacer = new BoxView
        {
            WidthRequest = 46,
            HeightRequest = 46,
            Opacity = 0
        };

        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            Children =
            {
                backButton,
                titleStack,
                headerSpacer
            }
        };
        grid.SetColumn(titleStack, 1);
        grid.SetColumn(headerSpacer, 2);
        return grid;
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
        var story = ToMobileStorySummary(download);
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
                    Content = new Image
                    {
                        Source = _apiClient.BuildCachedImageSource(
                            PageHelpers.ResolveStoryCardImageSource(story, _apiClient)),
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
        rowTap.Tapped += async (_, _) => await OpenDownloadedStoryAsync(download);
        row.GestureRecognizers.Add(rowTap);

        var playTap = new TapGestureRecognizer();
        playTap.Tapped += async (_, _) => await OpenDownloadedStoryAsync(download);
        playButton.GestureRecognizers.Add(playTap);
        return row;
    }

    private async Task OpenDownloadedStoryAsync(OfflineStoryDownload download)
    {
        _playlistPlaybackState.Clear();
        await CapturePlayerTransitionBackdropAsync();
        var story = ToMobileStorySummary(download);
        await Shell.Current.GoToAsync(
            $"{nameof(StoryDetailPage)}?slug={Uri.EscapeDataString(download.Slug)}&source={Uri.EscapeDataString(download.Source)}",
            animate: false,
            parameters: new Dictionary<string, object>
            {
                ["preview"] = story
            });
    }

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

    private static MobileStorySummary ToMobileStorySummary(OfflineStoryDownload download) =>
        new(
            Slug: download.Slug,
            Title: download.Title,
            Description: download.Description,
            ImageUrl: download.ImageUrl,
            ThumbnailUrl: download.ThumbnailUrl,
            Source: download.Source,
            IsLocked: false,
            IsFavorite: false,
            DetailUrl: download.DetailUrl,
            DurationSeconds: download.DurationSeconds);
}
