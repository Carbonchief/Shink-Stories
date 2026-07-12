using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Shink.Mobile.Models;
using IoPath = System.IO.Path;

namespace Shink.Mobile.Services;

public enum OfflineDownloadState
{
    NotDownloaded,
    Downloading,
    Downloaded,
    ExpiredAccess,
    Failed
}

public sealed record OfflineDownloadProgress(
    string Slug,
    string Source,
    long BytesReceived,
    long? TotalBytes,
    double? Percent);

public sealed record OfflineStoryDownload(
    string Slug,
    string Source,
    string Title,
    string Description,
    string ImageUrl,
    string ThumbnailUrl,
    string DetailUrl,
    decimal? DurationSeconds,
    bool RequiresSubscription,
    DateTimeOffset DownloadedAt,
    DateTimeOffset LastAccessVerifiedAt,
    long FileSizeBytes,
    string AudioFileName);

public interface IOfflineStoryDownloadService
{
    event EventHandler? DownloadsChanged;

    Task<IReadOnlyList<OfflineStoryDownload>> GetDownloadsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OfflineStoryDownload>> GetPlayableDownloadsAsync(CancellationToken cancellationToken = default);

    Task<OfflineStoryDownload?> GetDownloadAsync(string slug, string source, CancellationToken cancellationToken = default);

    Task<OfflineDownloadState> GetStateAsync(MobileStoryDetailResponse detail, CancellationToken cancellationToken = default);

    Task<OfflineStoryDownload> DownloadAsync(
        MobileStoryDetailResponse detail,
        IProgress<OfflineDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task RefreshAccessAsync(MobileStoryDetailResponse detail, CancellationToken cancellationToken = default);

    Task RemoveAsync(string slug, string source, CancellationToken cancellationToken = default);

    Task DeletePaidDownloadsAsync(CancellationToken cancellationToken = default);

    Task<string?> ResolvePlayableAudioAsync(MobileStoryDetailResponse detail, CancellationToken cancellationToken = default);

    MobileStoryDetailResponse CreateOfflineDetail(OfflineStoryDownload download);
}

public sealed class OfflineStoryDownloadService : IOfflineStoryDownloadService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly TimeSpan AccessRefreshWindow = TimeSpan.FromDays(30);
    private readonly MobileApiClient _apiClient;
    private readonly SessionState _sessionState;
    private readonly MobileAnalyticsService _analytics;
    private readonly SemaphoreSlim _metadataLock = new(1, 1);
    private readonly HashSet<string> _activeDownloads = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<OfflineStoryDownload>? _cachedDownloads;

    public OfflineStoryDownloadService(
        MobileApiClient apiClient,
        SessionState sessionState,
        MobileAnalyticsService analytics)
    {
        _apiClient = apiClient;
        _sessionState = sessionState;
        _analytics = analytics;
        _sessionState.Changed += session =>
        {
            if (!session.IsSignedIn)
            {
                _ = DeletePaidDownloadsAsync();
            }
        };
    }

    public event EventHandler? DownloadsChanged;

    public async Task<IReadOnlyList<OfflineStoryDownload>> GetDownloadsAsync(CancellationToken cancellationToken = default)
    {
        await _metadataLock.WaitAsync(cancellationToken);
        try
        {
            return await LoadDownloadsUnsafeAsync(cancellationToken);
        }
        finally
        {
            _metadataLock.Release();
        }
    }

    public async Task<IReadOnlyList<OfflineStoryDownload>> GetPlayableDownloadsAsync(CancellationToken cancellationToken = default)
    {
        var downloads = await GetDownloadsAsync(cancellationToken);
        return downloads
            .Where(download => IsPlayable(download) && File.Exists(BuildAudioPath(download.AudioFileName)))
            .OrderByDescending(download => download.DownloadedAt)
            .ToArray();
    }

    public async Task<OfflineStoryDownload?> GetDownloadAsync(
        string slug,
        string source,
        CancellationToken cancellationToken = default)
    {
        var downloads = await GetDownloadsAsync(cancellationToken);
        return downloads.FirstOrDefault(download => IsSameStory(download, slug, source));
    }

    public async Task<OfflineDownloadState> GetStateAsync(
        MobileStoryDetailResponse detail,
        CancellationToken cancellationToken = default)
    {
        var key = BuildStoryKey(detail.Story.Slug, detail.Story.Source);
        if (_activeDownloads.Contains(key))
        {
            return OfflineDownloadState.Downloading;
        }

        var download = await GetDownloadAsync(detail.Story.Slug, detail.Story.Source, cancellationToken);
        if (download is null)
        {
            return OfflineDownloadState.NotDownloaded;
        }

        if (!File.Exists(BuildAudioPath(download.AudioFileName)))
        {
            return OfflineDownloadState.Failed;
        }

        return IsPlayable(download)
            ? OfflineDownloadState.Downloaded
            : OfflineDownloadState.ExpiredAccess;
    }

    public async Task<OfflineStoryDownload> DownloadAsync(
        MobileStoryDetailResponse detail,
        IProgress<OfflineDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (detail.RequiresSubscription || string.IsNullOrWhiteSpace(detail.AudioUrl))
        {
            throw new InvalidOperationException("Hierdie storie kan nie tans afgelaai word nie.");
        }

        var key = BuildStoryKey(detail.Story.Slug, detail.Story.Source);
        if (!_activeDownloads.Add(key))
        {
            throw new InvalidOperationException("Hierdie storie is reeds besig om af te laai.");
        }

        try
        {
            _analytics.TrackEvent("mobile_story_download_started", new Dictionary<string, object>
            {
                ["story_slug"] = detail.Story.Slug,
                ["story_source"] = detail.Story.Source,
                ["duration_seconds"] = detail.Story.DurationSeconds ?? 0
            });
            Directory.CreateDirectory(AudioDirectory);
            var audioUrl = _apiClient.BuildAbsoluteUrl(detail.AudioUrl);
            var audioFileName = $"{BuildStableKey(key)}{ResolveAudioExtensionFromUrl(audioUrl)}";
            var audioPath = BuildAudioPath(audioFileName);
            var temporaryPath = $"{audioPath}.tmp";
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }

            await _apiClient.DownloadAudioToFileAsync(
                audioUrl,
                temporaryPath,
                new Progress<MobileAudioDownloadProgress>(downloadProgress =>
                {
                    progress?.Report(new OfflineDownloadProgress(
                        detail.Story.Slug,
                        detail.Story.Source,
                        downloadProgress.BytesReceived,
                        downloadProgress.TotalBytes,
                        downloadProgress.Percent));
                }),
                cancellationToken);

            if (File.Exists(audioPath))
            {
                File.Delete(audioPath);
            }

            File.Move(temporaryPath, audioPath);
            var fileInfo = new FileInfo(audioPath);
            var now = DateTimeOffset.UtcNow;
            var download = new OfflineStoryDownload(
                Slug: detail.Story.Slug,
                Source: detail.Story.Source,
                Title: detail.Story.Title,
                Description: detail.Story.Description,
                ImageUrl: detail.Story.ImageUrl,
                ThumbnailUrl: detail.Story.ThumbnailUrl,
                DetailUrl: detail.Story.DetailUrl,
                DurationSeconds: detail.Story.DurationSeconds,
                RequiresSubscription: !string.Equals(detail.Story.Source, "gratis", StringComparison.OrdinalIgnoreCase),
                DownloadedAt: now,
                LastAccessVerifiedAt: now,
                FileSizeBytes: fileInfo.Length,
                AudioFileName: audioFileName);

            await SaveDownloadAsync(download, cancellationToken);
            DownloadsChanged?.Invoke(this, EventArgs.Empty);
            _analytics.TrackEvent("mobile_story_download_completed", new Dictionary<string, object>
            {
                ["story_slug"] = download.Slug,
                ["story_source"] = download.Source,
                ["file_size_bytes"] = download.FileSizeBytes,
                ["requires_subscription"] = download.RequiresSubscription
            });
            return download;
        }
        catch (Exception ex)
        {
            CleanupTempFiles(key);
            _analytics.TrackException(ex, "mobile_story_download_failed", new Dictionary<string, object>
            {
                ["story_slug"] = detail.Story.Slug,
                ["story_source"] = detail.Story.Source
            });
            throw;
        }
        finally
        {
            _activeDownloads.Remove(key);
        }
    }

    public async Task RefreshAccessAsync(MobileStoryDetailResponse detail, CancellationToken cancellationToken = default)
    {
        if (detail.RequiresSubscription)
        {
            return;
        }

        await _metadataLock.WaitAsync(cancellationToken);
        try
        {
            var downloads = (await LoadDownloadsUnsafeAsync(cancellationToken)).ToList();
            var index = downloads.FindIndex(download => IsSameStory(download, detail.Story.Slug, detail.Story.Source));
            if (index < 0)
            {
                return;
            }

            downloads[index] = downloads[index] with { LastAccessVerifiedAt = DateTimeOffset.UtcNow };
            await SaveDownloadsUnsafeAsync(downloads, cancellationToken);
            _analytics.TrackEvent("mobile_story_download_access_refreshed", new Dictionary<string, object>
            {
                ["story_slug"] = detail.Story.Slug,
                ["story_source"] = detail.Story.Source
            });
        }
        finally
        {
            _metadataLock.Release();
        }
    }

    public async Task RemoveAsync(string slug, string source, CancellationToken cancellationToken = default)
    {
        await _metadataLock.WaitAsync(cancellationToken);
        try
        {
            var downloads = (await LoadDownloadsUnsafeAsync(cancellationToken)).ToList();
            var download = downloads.FirstOrDefault(item => IsSameStory(item, slug, source));
            if (download is null)
            {
                return;
            }

            DeleteAudioFile(download);
            downloads.Remove(download);
            await SaveDownloadsUnsafeAsync(downloads, cancellationToken);
            _analytics.TrackEvent("mobile_story_download_removed", new Dictionary<string, object>
            {
                ["story_slug"] = download.Slug,
                ["story_source"] = download.Source,
                ["file_size_bytes"] = download.FileSizeBytes
            });
        }
        finally
        {
            _metadataLock.Release();
        }

        DownloadsChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task DeletePaidDownloadsAsync(CancellationToken cancellationToken = default)
    {
        await _metadataLock.WaitAsync(cancellationToken);
        try
        {
            var downloads = (await LoadDownloadsUnsafeAsync(cancellationToken)).ToList();
            var paidDownloads = downloads.Where(download => download.RequiresSubscription).ToArray();
            foreach (var download in paidDownloads)
            {
                DeleteAudioFile(download);
                downloads.Remove(download);
            }

            await SaveDownloadsUnsafeAsync(downloads, cancellationToken);
            if (paidDownloads.Length > 0)
            {
                _analytics.TrackEvent("mobile_paid_downloads_deleted", new Dictionary<string, object>
                {
                    ["deleted_count"] = paidDownloads.Length
                });
            }
        }
        finally
        {
            _metadataLock.Release();
        }

        DownloadsChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task<string?> ResolvePlayableAudioAsync(
        MobileStoryDetailResponse detail,
        CancellationToken cancellationToken = default)
    {
        var download = await GetDownloadAsync(detail.Story.Slug, detail.Story.Source, cancellationToken);
        if (download is null || !IsPlayable(download))
        {
            return null;
        }

        var audioPath = BuildAudioPath(download.AudioFileName);
        return File.Exists(audioPath)
            ? new Uri(audioPath).AbsoluteUri
            : null;
    }

    public MobileStoryDetailResponse CreateOfflineDetail(OfflineStoryDownload download)
    {
        var story = new MobileStorySummary(
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

        return new MobileStoryDetailResponse(
            Story: story,
            AudioUrl: new Uri(BuildAudioPath(download.AudioFileName)).AbsoluteUri,
            ShareUrl: download.DetailUrl,
            RequiresSubscription: !IsPlayable(download),
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
    }

    private static string AudioDirectory =>
        IoPath.Combine(FileSystem.AppDataDirectory, "offline-story-audio");

    private static string MetadataPath =>
        IoPath.Combine(FileSystem.AppDataDirectory, "offline-story-downloads.json");

    private static string BuildAudioPath(string audioFileName) =>
        IoPath.Combine(AudioDirectory, audioFileName);

    private async Task SaveDownloadAsync(OfflineStoryDownload download, CancellationToken cancellationToken)
    {
        await _metadataLock.WaitAsync(cancellationToken);
        try
        {
            var downloads = (await LoadDownloadsUnsafeAsync(cancellationToken))
                .Where(item => !IsSameStory(item, download.Slug, download.Source))
                .Append(download)
                .ToArray();
            await SaveDownloadsUnsafeAsync(downloads, cancellationToken);
        }
        finally
        {
            _metadataLock.Release();
        }
    }

    private async Task<IReadOnlyList<OfflineStoryDownload>> LoadDownloadsUnsafeAsync(CancellationToken cancellationToken)
    {
        if (_cachedDownloads is not null)
        {
            return _cachedDownloads;
        }

        if (!File.Exists(MetadataPath))
        {
            return _cachedDownloads = Array.Empty<OfflineStoryDownload>();
        }

        try
        {
            await using var stream = File.OpenRead(MetadataPath);
            _cachedDownloads = await JsonSerializer.DeserializeAsync<OfflineStoryDownload[]>(
                    stream,
                    JsonOptions,
                    cancellationToken)
                ?? Array.Empty<OfflineStoryDownload>();
            return _cachedDownloads;
        }
        catch
        {
            return _cachedDownloads = Array.Empty<OfflineStoryDownload>();
        }
    }

    private async Task SaveDownloadsUnsafeAsync(
        IReadOnlyList<OfflineStoryDownload> downloads,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(IoPath.GetDirectoryName(MetadataPath)!);
        var temporaryPath = $"{MetadataPath}.tmp";
        await using (var stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(stream, downloads, JsonOptions, cancellationToken);
        }

        if (File.Exists(MetadataPath))
        {
            File.Delete(MetadataPath);
        }

        File.Move(temporaryPath, MetadataPath);
        _cachedDownloads = downloads.ToArray();
    }

    private static bool IsPlayable(OfflineStoryDownload download) =>
        !download.RequiresSubscription ||
        DateTimeOffset.UtcNow - download.LastAccessVerifiedAt <= AccessRefreshWindow;

    private static bool IsSameStory(OfflineStoryDownload download, string slug, string source) =>
        string.Equals(download.Slug, slug, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(download.Source, source, StringComparison.OrdinalIgnoreCase);

    private static string BuildStoryKey(string slug, string source) =>
        $"{source.Trim().ToLowerInvariant()}:{slug.Trim().ToLowerInvariant()}";

    private static string BuildStableKey(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant()[..24];

    private static string ResolveAudioExtensionFromUrl(string audioUrl)
    {
        if (Uri.TryCreate(audioUrl, UriKind.Absolute, out var uri))
        {
            var extension = IoPath.GetExtension(uri.AbsolutePath).ToLowerInvariant();
            if (extension is ".mp3" or ".mpeg" or ".m4a" or ".wav" or ".ogg")
            {
                return extension == ".mpeg" ? ".mp3" : extension;
            }
        }

        return ".mp3";
    }

    private static void DeleteAudioFile(OfflineStoryDownload download)
    {
        var audioPath = BuildAudioPath(download.AudioFileName);
        if (File.Exists(audioPath))
        {
            File.Delete(audioPath);
        }
    }

    private static void CleanupTempFiles(string key)
    {
        if (!Directory.Exists(AudioDirectory))
        {
            return;
        }

        var prefix = BuildStableKey(key);
        foreach (var temporaryPath in Directory.EnumerateFiles(AudioDirectory, $"{prefix}*.tmp"))
        {
            File.Delete(temporaryPath);
        }
    }
}
