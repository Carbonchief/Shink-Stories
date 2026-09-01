using Shink.Mobile.Services;

namespace Shink.Mobile.Pages;

internal sealed record ProgressiveImageRequest(
    string? FullImageUrl,
    string? PreviewImageUrl = null,
    string? FallbackFile = null);

/// <summary>
/// Displays the smallest available cached image first, then replaces only this
/// image with the device-resolution cache entry when the full artwork is ready.
/// Page layouts and CollectionView item sources are never rebuilt for upgrades.
/// </summary>
internal sealed class ProgressiveCachedImage : Image
{
    private const uint FadeInDurationMilliseconds = 120;
    private static readonly Color PlaceholderBackgroundColor = Color.FromArgb("#146D69");

    public static readonly BindableProperty RequestProperty = BindableProperty.Create(
        nameof(Request),
        typeof(ProgressiveImageRequest),
        typeof(ProgressiveCachedImage),
        default(ProgressiveImageRequest),
        propertyChanged: static (bindable, _, _) =>
            ((ProgressiveCachedImage)bindable).ApplyRequest());

    private readonly MobileApiClient _apiClient;
    private CancellationTokenSource? _loadCancellation;
    private long _requestVersion;
    private bool _hasDisplayedArtwork;
    private bool _isLoaded;

    public ProgressiveCachedImage(MobileApiClient apiClient)
    {
        _apiClient = apiClient;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public ProgressiveCachedImage(MobileApiClient apiClient, ProgressiveImageRequest request)
        : this(apiClient)
    {
        Request = request;
    }

    public ProgressiveImageRequest? Request
    {
        get => (ProgressiveImageRequest?)GetValue(RequestProperty);
        set => SetValue(RequestProperty, value);
    }

    public void SetImage(string? fullImageUrl, string? previewImageUrl = null, string? fallbackFile = null) =>
        Request = new ProgressiveImageRequest(fullImageUrl, previewImageUrl, fallbackFile);

    private void OnLoaded(object? sender, EventArgs args)
    {
        _isLoaded = true;
        ShowCurrentRequest();
    }

    private void OnUnloaded(object? sender, EventArgs args)
    {
        _isLoaded = false;
        Interlocked.Increment(ref _requestVersion);
        CancelCurrentListener();

        // A CollectionView can temporarily detach a recycled carousel cell and
        // bring the same native view back without rebinding its request. Keep the
        // already-decoded bitmap visible until ApplyRequest replaces the item.
        this.CancelAnimations();
        Opacity = Source is null ? 0 : 1;
    }

    private void ApplyRequest()
    {
        Interlocked.Increment(ref _requestVersion);
        CancelCurrentListener();
        ResetVisual();

        if (_isLoaded)
        {
            ShowCurrentRequest();
        }
    }

    private void ShowCurrentRequest()
    {
        var request = Request;
        if (request is null || !_isLoaded)
        {
            return;
        }

        var version = Volatile.Read(ref _requestVersion);
        if (_apiClient.TryBuildCachedImageSource(request.FullImageUrl, out var fullSource) &&
            fullSource is not null)
        {
            ShowArtworkSource(fullSource, version);
            return;
        }

        var previewDisplayed = false;
        if (_apiClient.TryBuildCachedImageSource(request.PreviewImageUrl, out var previewSource))
        {
            if (previewSource is not null)
            {
                ShowArtworkSource(previewSource, version);
                previewDisplayed = true;
            }
        }
        else
        {
            ShowFallback(request.FallbackFile);
        }

        StartLoad(request, version, previewDisplayed);
    }

    private void StartLoad(
        ProgressiveImageRequest request,
        long version,
        bool previewDisplayed)
    {
        if (!_isLoaded)
        {
            return;
        }

        CancelCurrentListener();
        _loadCancellation = new CancellationTokenSource();
        _ = LoadProgressivelyAsync(
            request,
            version,
            previewDisplayed,
            _loadCancellation.Token);
    }

    private async Task LoadProgressivelyAsync(
        ProgressiveImageRequest request,
        long version,
        bool previewDisplayed,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!previewDisplayed &&
                !string.IsNullOrWhiteSpace(request.PreviewImageUrl) &&
                !string.Equals(
                    request.PreviewImageUrl.Trim(),
                    request.FullImageUrl?.Trim(),
                    StringComparison.OrdinalIgnoreCase))
            {
                var previewSource = await _apiClient.CacheImageSourceAsync(
                    request.PreviewImageUrl,
                    cancellationToken);
                await ApplySourceIfCurrentAsync(previewSource, version, cancellationToken);
            }

            var fullSource = await _apiClient.CacheImageSourceAsync(
                request.FullImageUrl,
                cancellationToken);
            await ApplySourceIfCurrentAsync(fullSource, version, cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            // The fallback or preview remains visible. A later binding/load retries.
        }
    }

    private Task ApplySourceIfCurrentAsync(
        ImageSource? source,
        long version,
        CancellationToken cancellationToken)
    {
        if (source is null || cancellationToken.IsCancellationRequested)
        {
            return Task.CompletedTask;
        }

        return MainThread.InvokeOnMainThreadAsync(() =>
        {
            if (_isLoaded &&
                !cancellationToken.IsCancellationRequested &&
                version == Volatile.Read(ref _requestVersion))
            {
                ShowArtworkSource(source, version);
            }
        });
    }

    private void ShowFallback(string? fallbackFile)
    {
        if (string.IsNullOrWhiteSpace(fallbackFile))
        {
            return;
        }

        this.CancelAnimations();
        BackgroundColor = string.Equals(
            fallbackFile.Trim(),
            PageHelpers.StoryPlaceholderFile,
            StringComparison.OrdinalIgnoreCase)
                ? PlaceholderBackgroundColor
                : Colors.Transparent;
        Source = ImageSource.FromFile(fallbackFile);
        Opacity = 1;
        _hasDisplayedArtwork = false;
    }

    private void ShowArtworkSource(ImageSource source, long version)
    {
        if (!_isLoaded || version != Volatile.Read(ref _requestVersion))
        {
            return;
        }

        // Several recycled carousel cells can receive a new source during one
        // Android fling. Avoid starting competing opacity animations on those
        // frames; iOS retains the established progressive transition.
        var shouldFade = !_hasDisplayedArtwork &&
            DeviceInfo.Current.Platform != DevicePlatform.Android;
        _hasDisplayedArtwork = true;
        this.CancelAnimations();
        BackgroundColor = IsPlaceholderRequest(Request)
            ? PlaceholderBackgroundColor
            : Colors.Transparent;
        Source = source;
        if (!shouldFade)
        {
            Opacity = 1;
            return;
        }

        Opacity = 0;
        _ = FadeInAsync(version);
    }

    private async Task FadeInAsync(long version)
    {
        try
        {
            await this.FadeToAsync(1, FadeInDurationMilliseconds, Easing.CubicOut);
        }
        catch
        {
            // A recycled cell cancels its in-flight opacity animation.
        }

        if (_isLoaded && version == Volatile.Read(ref _requestVersion))
        {
            Opacity = 1;
        }
    }

    private void ResetVisual()
    {
        this.CancelAnimations();
        BackgroundColor = Colors.Transparent;
        Source = null;
        Opacity = 0;
        _hasDisplayedArtwork = false;
    }

    private static bool IsPlaceholderRequest(ProgressiveImageRequest? request) =>
        IsPlaceholderUrl(request?.FullImageUrl) || IsPlaceholderUrl(request?.PreviewImageUrl);

    private static bool IsPlaceholderUrl(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Contains("/branding/schink-placeholder.png", StringComparison.OrdinalIgnoreCase);

    private void CancelCurrentListener()
    {
        var cancellation = Interlocked.Exchange(ref _loadCancellation, null);
        if (cancellation is null)
        {
            return;
        }

        cancellation.Cancel();
        cancellation.Dispose();
    }
}
