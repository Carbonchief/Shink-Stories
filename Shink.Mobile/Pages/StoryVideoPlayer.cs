using CommunityToolkit.Maui.Core;
using CommunityToolkit.Maui.Views;
using Shink.Mobile.Models;
using Shink.Mobile.Services;

namespace Shink.Mobile.Pages;

internal sealed class StoryVideoPlayer : ContentView, IDisposable
{
    private readonly MobileApiClient _api;
    private readonly IOfflineStoryDownloadService _downloads;
    private readonly StoryPlaybackSession _audio;
    private readonly MobileStoryDetailResponse _detail;
    private readonly MediaElement _media;
    private readonly Button _play;
    private readonly Button _fullscreen;
    private readonly Label _error;
    private readonly Image _poster;
    private CancellationTokenSource? _request;
    private Window? _window;
    private bool _disposed;
    private bool _failed;
    private bool _loading;

    public event EventHandler? FullscreenRequested;
    public bool HasMedia => _media.Source is not null;

    public StoryVideoPlayer(MobileStoryDetailResponse detail, MobileApiClient api,
        IOfflineStoryDownloadService downloads, StoryPlaybackSession audio)
    {
        _detail = detail;
        _api = api;
        _downloads = downloads;
        _audio = audio;
        BackgroundColor = Colors.Black;
        HeightRequest = 300;
        AutomationId = "story-video-player";
        _media = new MediaElement
        {
            ShouldAutoPlay = false,
            ShouldShowPlaybackControls = true,
            ShouldKeepScreenOn = true,
            Aspect = Aspect.AspectFit,
            BackgroundColor = Colors.Black,
            AutomationId = "story-video-surface"
        };
        _poster = new Image { Source = api.BuildImageUrl(detail.Story.ImageUrl), Aspect = Aspect.AspectFit, InputTransparent = true };
        _play = new Button { Text = "Speel video", AutomationId = "story-video-play", BackgroundColor = Colors.Transparent, TextColor = Colors.White };
        _fullscreen = new Button { Text = "Volskerm", AutomationId = "story-video-fullscreen", BackgroundColor = Colors.Transparent, TextColor = Colors.White };
        _error = new Label { IsVisible = false, TextColor = Colors.White, FontSize = 14, Margin = 12, HorizontalTextAlignment = TextAlignment.Center };
        _play.Clicked += async (_, _) =>
        {
            if (_media.CurrentState == MediaElementState.Playing) _media.Pause();
            else if (_media.Source is not null && !_failed) { _audio.Pause(); _media.Play(); }
            else await PrepareAndPlayAsync();
        };
        _fullscreen.Clicked += (_, _) => FullscreenRequested?.Invoke(this, EventArgs.Empty);
        _media.StateChanged += (_, args) =>
        {
            if (args.NewState == MediaElementState.Playing) _audio.Pause();
            _play.Text = args.NewState == MediaElementState.Playing ? "Pouse" : "Speel video";
        };
        _media.MediaOpened += (_, _) => { _poster.IsVisible = false; _loading = false; _play.IsEnabled = true; };
        _media.MediaFailed += (_, _) => ShowError();
        var actions = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) }, Children = { _play, _fullscreen } };
        Grid.SetColumn(_fullscreen, 1);
        var stage = new Grid { Children = { _media, _poster } };
        var layout = new Grid { RowDefinitions = { new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto) }, Children = { stage, _error, actions } };
        Grid.SetRow(_error, 1);
        Grid.SetRow(actions, 2);
        Content = layout;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, EventArgs args)
    {
        if (_disposed || _window is not null) return;
        _window = Window;
        if (_window is not null) _window.Stopped += OnStopped;
        Connectivity.Current.ConnectivityChanged += OnConnectivityChanged;
    }

    private void OnStopped(object? sender, EventArgs args) => _media.Pause();

    private void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs args)
    {
        if (args.NetworkAccess == NetworkAccess.Internet)
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                if (!_disposed && _failed && IsLoaded) await PrepareAndPlayAsync();
            });
    }

    private async Task PrepareAndPlayAsync()
    {
        if (_disposed || _loading) return;
        _loading = true;
        _failed = false;
        _play.IsEnabled = false;
        _play.Text = "Laai video...";
        _error.IsVisible = false;
        _request?.Cancel();
        _request?.Dispose();
        _request = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = _request.Token;
        try
        {
            var local = await _downloads.ResolvePlayableVideoAsync(_detail, token);
            string source;
            if (local is not null) source = local;
            else
            {
                if (await _downloads.GetStateAsync(_detail, token) == OfflineDownloadState.ExpiredAccess)
                    throw new InvalidOperationException("Hierdie aflaai moet weer aanlyn bevestig word.");
                // Refresh the short-lived authorization link, including after a retry.
                var fresh = await _api.GetStoryAsync(_detail.Story.Slug, _detail.Story.Source, token);
                if (fresh is null || fresh.RequiresSubscription || string.IsNullOrWhiteSpace(fresh.VideoUrl))
                    throw new InvalidOperationException("Die video is nie tans beskikbaar nie.");
                source = await _api.ResolveVideoPlaybackSourceAsync(fresh.VideoUrl, token);
            }
            if (_disposed || token.IsCancellationRequested) return;
            _audio.Pause();
            _media.ShouldAutoPlay = true;
            _media.Source = source.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
                ? MediaSource.FromFile(new Uri(source).LocalPath)
                : MediaSource.FromUri(new Uri(source));
        }
        catch (Exception)
        {
            if (!_disposed) ShowError();
        }
        finally
        {
            _loading = false;
            if (!_disposed) _play.IsEnabled = true;
        }
    }

    private void ShowError()
    {
        if (_disposed) return;
        _failed = true;
        _loading = false;
        _error.Text = "Kon nie die video speel nie. Gaan jou verbinding na en probeer weer.";
        _error.IsVisible = true;
        _play.Text = "Probeer weer";
        _play.IsEnabled = true;
    }

    public void SetFullscreen(bool fullscreen)
    {
        HeightRequest = fullscreen ? -1 : 300;
        VerticalOptions = fullscreen ? LayoutOptions.Fill : LayoutOptions.Start;
        _fullscreen.Text = fullscreen ? "Verlaat volskerm" : "Volskerm";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _request?.Cancel();
        _request?.Dispose();
        Connectivity.Current.ConnectivityChanged -= OnConnectivityChanged;
        if (_window is not null) _window.Stopped -= OnStopped;
        _media.Stop();
        _media.Source = null;
        _media.Handler?.DisconnectHandler();
        _media.Dispose();
    }
}
