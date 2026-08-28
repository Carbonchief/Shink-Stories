using Shink.Mobile.Services;

namespace Shink.Mobile.Pages;

internal sealed class PersistentNowPlayingBar : ContentView
{
    private const string PlayIconGlyph = "\uf04b";
    private const string PauseIconGlyph = "\uf04c";
    private static readonly Color BarBackgroundColor = Color.FromArgb("#F7F2EA");
    private static readonly Color BarTextColor = Color.FromArgb("#1B2231");
    private static readonly Color BarMutedTextColor = Color.FromArgb("#69716D");
    private static readonly Color AccentColor = Color.FromArgb("#123F3F");

    private readonly StoryPlaybackSession _playbackSession;
    private readonly NavigationGate _navigationGate = new();
    private readonly ProgressiveCachedImage _artwork;
    private readonly Label _statusLabel;
    private readonly Label _titleLabel;
    private readonly Button _playPauseButton;
    private bool _isSubscribed;
    private bool _isTogglingPlayback;

    public PersistentNowPlayingBar(StoryPlaybackSession playbackSession)
    {
        _playbackSession = playbackSession;
        IsVisible = false;
        HorizontalOptions = LayoutOptions.Fill;
        VerticalOptions = LayoutOptions.End;
        AutomationId = "persistent-now-playing";

        _artwork = new ProgressiveCachedImage(playbackSession.ImageApiClient)
        {
            Aspect = Aspect.AspectFill,
            WidthRequest = 52,
            HeightRequest = 52,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill
        };

        _statusLabel = new Label
        {
            Text = "Nou speel",
            FontFamily = "PoppinsSemiBold",
            FontSize = 11,
            CharacterSpacing = 0.7,
            TextColor = BarMutedTextColor,
            LineBreakMode = LineBreakMode.NoWrap,
            VerticalOptions = LayoutOptions.Center,
            VerticalTextAlignment = TextAlignment.Center
        };
        _titleLabel = new Label
        {
            FontFamily = "PoppinsSemiBold",
            FontSize = 14,
            TextColor = BarTextColor,
            MaxLines = 1,
            LineBreakMode = LineBreakMode.TailTruncation,
            VerticalOptions = LayoutOptions.Center,
            VerticalTextAlignment = TextAlignment.Center
        };

        _playPauseButton = BuildActionButton(PlayIconGlyph, "Hervat storie", 44);
        _playPauseButton.AutomationId = "persistent-now-playing-toggle";
        _playPauseButton.FontFamily = "FontAwesomeSolid";
        _playPauseButton.FontSize = 16;
        _playPauseButton.Clicked += async (_, _) => await TogglePlaybackAsync();

        var stopButton = BuildActionButton("×", "Stop storie", 38);
        stopButton.AutomationId = "persistent-now-playing-stop";
        stopButton.FontSize = 27;
        stopButton.TextColor = BarMutedTextColor;
        stopButton.BackgroundColor = Colors.Transparent;
        stopButton.Clicked += (_, _) => _playbackSession.Stop();

        var artworkFrame = new Border
        {
            WidthRequest = 52,
            HeightRequest = 52,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 11 },
            Content = _artwork
        };

        var storyInfo = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star)
            },
            RowSpacing = 0,
            HeightRequest = 52,
            MinimumHeightRequest = 52,
            VerticalOptions = LayoutOptions.Center,
            Children = { _statusLabel, _titleLabel }
        };
        Grid.SetRow(_statusLabel, 1);
        Grid.SetRow(_titleLabel, 2);

        var openStorySurface = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star)
            },
            ColumnSpacing = 11,
            MinimumHeightRequest = 52,
            VerticalOptions = LayoutOptions.Center,
            Children = { artworkFrame, storyInfo }
        };
        Grid.SetColumn(storyInfo, 1);
        var openTap = new TapGestureRecognizer();
        openTap.Tapped += async (_, _) => await OpenPlaybackOriginAsync();
        openStorySurface.GestureRecognizers.Add(openTap);
        SemanticProperties.SetDescription(openStorySurface, "Maak die storie wat tans speel oop");

        var row = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 7,
            Children = { openStorySurface, _playPauseButton, stopButton }
        };
        Grid.SetColumn(_playPauseButton, 1);
        Grid.SetColumn(stopButton, 2);

        Content = new Border
        {
            BackgroundColor = BarBackgroundColor,
            Stroke = Color.FromArgb("#22FFFFFF"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 18 },
            Padding = new Thickness(10, 8),
            HeightRequest = 70,
            Content = row
        };

        Loaded += (_, _) => Subscribe();
        Unloaded += (_, _) => Unsubscribe();
        Refresh();
    }

    private static Button BuildActionButton(string text, string description, double size)
    {
        var button = new Button
        {
            Text = text,
            FontFamily = "PoppinsBold",
            FontSize = 18,
            TextColor = Colors.White,
            BackgroundColor = AccentColor,
            BorderWidth = 0,
            CornerRadius = (int)(size / 2),
            WidthRequest = size,
            HeightRequest = size,
            Padding = 0,
            VerticalOptions = LayoutOptions.Center
        };
        SemanticProperties.SetDescription(button, description);
        return button;
    }

    private void Subscribe()
    {
        if (_isSubscribed)
        {
            return;
        }

        _playbackSession.Changed += OnPlaybackChanged;
        _isSubscribed = true;
        Refresh();
    }

    private void Unsubscribe()
    {
        if (!_isSubscribed)
        {
            return;
        }

        _playbackSession.Changed -= OnPlaybackChanged;
        _isSubscribed = false;
    }

    private void OnPlaybackChanged(object? sender, EventArgs args) => Refresh();

    private void Refresh()
    {
        var current = _playbackSession.Current;
        IsVisible = current is not null;
        if (current is null)
        {
            _artwork.Request = null;
            _titleLabel.Text = string.Empty;
            return;
        }

        _artwork.Request = PageHelpers.BuildStoryImageRequest(
            current.Story,
            _playbackSession.ImageApiClient,
            "schink_background.jpeg");
        _titleLabel.Text = current.Story.Title;
        _statusLabel.Text = _playbackSession.IsPlaying ? "Nou speel" : "Gepouseer";
        _playPauseButton.Text = _playbackSession.IsPlaying ? PauseIconGlyph : PlayIconGlyph;
        SemanticProperties.SetDescription(
            _playPauseButton,
            _playbackSession.IsPlaying ? "Pouseer storie" : "Hervat storie");
    }

    private async Task TogglePlaybackAsync()
    {
        if (_isTogglingPlayback)
        {
            return;
        }

        _isTogglingPlayback = true;
        _playPauseButton.IsEnabled = false;
        try
        {
            if (_playbackSession.IsPlaying)
            {
                _playbackSession.Pause();
            }
            else
            {
                await _playbackSession.ResumeAsync();
            }
        }
        catch (Exception ex)
        {
            if (Shell.Current is not null)
            {
                await Shell.Current.DisplayAlertAsync("Kon nie audio speel nie", ex.Message, "Maak toe");
            }
        }
        finally
        {
            _playPauseButton.IsEnabled = true;
            _isTogglingPlayback = false;
            Refresh();
        }
    }

    private async Task OpenPlaybackOriginAsync()
    {
        await _navigationGate.RunAsync(async () =>
        {
            var current = _playbackSession.Current;
            if (current is null)
            {
                return;
            }

            if (current.OriginPlaylist is { } playlist)
            {
                await Shell.Current.GoToAsync(
                    nameof(PlaylistDetailPage),
                    animate: true,
                    parameters: new ShellNavigationQueryParameters
                    {
                        ["playlist"] = playlist
                    });
                return;
            }

            var parameters = new ShellNavigationQueryParameters
            {
                ["preview"] = current.Story
            };
            if (!string.IsNullOrWhiteSpace(current.PlaylistSlug))
            {
                parameters["playlistSlug"] = current.PlaylistSlug;
            }

            if (!string.IsNullOrWhiteSpace(current.PlaylistTitle))
            {
                parameters["playlistTitle"] = current.PlaylistTitle;
            }

            await Shell.Current.GoToAsync(
                $"{nameof(StoryDetailPage)}?slug={Uri.EscapeDataString(current.Story.Slug)}&source={Uri.EscapeDataString(current.Story.Source)}",
                animate: true,
                parameters);
        });
    }
}

internal static class PersistentPlaybackHost
{
    public static View Wrap(
        View content,
        StoryPlaybackSession playbackSession,
        bool edgeToEdge = false)
    {
        var nowPlayingBar = new PersistentNowPlayingBar(playbackSession)
        {
            Margin = new Thickness(10, 6, 10, 10)
        };
        var root = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto)
            },
            Children = { content, nowPlayingBar }
        };
        if (edgeToEdge)
        {
            root.SafeAreaEdges = SafeAreaEdges.None;
        }

        Grid.SetRow(nowPlayingBar, 1);
        return root;
    }
}
