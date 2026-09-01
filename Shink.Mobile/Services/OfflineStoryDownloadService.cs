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
    string AudioFileName,
    string? OwnerKey = null,
    string? ArtworkFileName = null);

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

    Task<string?> ResolvePlayableAudioAsync(MobileStoryDetailResponse detail, CancellationToken cancellationToken = default);

    MobileStorySummary CreateOfflineStory(OfflineStoryDownload download);

    MobileStoryDetailResponse CreateOfflineDetail(OfflineStoryDownload download);
}

public sealed class OfflineStoryDownloadService : IOfflineStoryDownloadService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly TimeSpan AccessRefreshWindow = TimeSpan.FromDays(30);
    private const string LastSignedInOwnerKeyPreferenceKey = "offline_download_last_signed_in_owner_v1";
    private readonly MobileApiClient _apiClient;
    private readonly SessionState _sessionState;
    private readonly MobileAnalyticsService _analytics;
    private readonly SemaphoreSlim _metadataLock = new(1, 1);
    private readonly HashSet<string> _activeDownloads = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _activeArtworkRepairs = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<OfflineStoryDownload>? _cachedDownloads;

    public OfflineStoryDownloadService(
        MobileApiClient apiClient,
        SessionState sessionState,
        MobileAnalyticsService analytics)
    {
        _apiClient = apiClient;
        _sessionState = sessionState;
        _analytics = analytics;
        RememberCurrentOwnerKey(_sessionState.Current);
        _sessionState.Changed += session =>
        {
            RememberCurrentOwnerKey(session);
            DownloadsChanged?.Invoke(this, EventArgs.Empty);
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
        var currentOwnerKey = ResolveCurrentOwnerKey();
        var session = _sessionState.Current;
        var now = DateTimeOffset.UtcNow;
        var playableDownloads = downloads
            .Where(download =>
                IsPlayable(download, session, currentOwnerKey, now) &&
                File.Exists(BuildAudioPath(download.AudioFileName)))
            .OrderByDescending(download => download.DownloadedAt)
            .ToArray();

        foreach (var download in playableDownloads.Where(download => !HasOfflineArtwork(download)))
        {
            QueueArtworkRepair(download);
        }

        return playableDownloads;
    }

    public async Task<OfflineStoryDownload?> GetDownloadAsync(
        string slug,
        string source,
        CancellationToken cancellationToken = default)
    {
        var downloads = await GetDownloadsAsync(cancellationToken);
        var currentOwnerKey = ResolveCurrentOwnerKey();
        return downloads.FirstOrDefault(download =>
            IsSameStory(download, slug, source) &&
            IsOwnedByCurrentAccount(download, currentOwnerKey));
    }

    public async Task<OfflineDownloadState> GetStateAsync(
        MobileStoryDetailResponse detail,
        CancellationToken cancellationToken = default)
    {
        var ownerKey = RequiresSubscription(detail.Story.Source)
            ? ResolveCurrentOwnerKey()
            : null;
        var key = BuildDownloadKey(detail.Story.Slug, detail.Story.Source, ownerKey);
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

        var requiresSubscription = RequiresSubscription(detail.Story.Source);
        var ownerKey = requiresSubscription
            ? ResolveCurrentOwnerKey()
            : null;
        if (requiresSubscription &&
            (!_sessionState.Current.IsSignedIn ||
             !_sessionState.Current.HasFullStoryAccess ||
             string.IsNullOrWhiteSpace(ownerKey)))
        {
            throw new InvalidOperationException("Teken asseblief in met jou aktiewe rekening om hierdie storie af te laai.");
        }

        var key = BuildDownloadKey(detail.Story.Slug, detail.Story.Source, ownerKey);
        if (!_activeDownloads.Add(key))
        {
            throw new InvalidOperationException("Hierdie storie is reeds besig om af te laai.");
        }

        string? completedAudioPath = null;
        string? completedArtworkFileName = null;
        var metadataSaved = false;
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
            completedAudioPath = audioPath;
            var artworkFileName = await SaveArtworkFileAsync(
                detail.Story.ImageUrl,
                detail.Story.ThumbnailUrl,
                key,
                cancellationToken);
            completedArtworkFileName = artworkFileName;
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
                AudioFileName: audioFileName,
                OwnerKey: ownerKey,
                ArtworkFileName: artworkFileName);

            await SaveDownloadAsync(download, cancellationToken);
            metadataSaved = true;
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
            if (!metadataSaved)
            {
                if (!string.IsNullOrWhiteSpace(completedAudioPath) && File.Exists(completedAudioPath))
                {
                    File.Delete(completedAudioPath);
                }

                if (!string.IsNullOrWhiteSpace(completedArtworkFileName))
                {
                    DeleteArtworkFileByName(completedArtworkFileName);
                }
            }

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
            var currentOwnerKey = ResolveCurrentOwnerKey();
            var index = downloads.FindIndex(download =>
                IsSameStory(download, detail.Story.Slug, detail.Story.Source) &&
                IsOwnedByCurrentAccount(download, currentOwnerKey));
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
            var currentOwnerKey = ResolveCurrentOwnerKey();
            var download = downloads.FirstOrDefault(item =>
                IsSameStory(item, slug, source) &&
                IsOwnedByCurrentAccount(item, currentOwnerKey));
            if (download is null)
            {
                return;
            }

            DeleteAudioFile(download);
            DeleteArtworkFile(download);
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

    public MobileStorySummary CreateOfflineStory(OfflineStoryDownload download)
    {
        var artworkUrl = ResolveOfflineArtworkUrl(download);
        return new MobileStorySummary(
            Slug: download.Slug,
            Title: download.Title,
            Description: download.Description,
            ImageUrl: artworkUrl,
            ThumbnailUrl: artworkUrl,
            Source: download.Source,
            IsLocked: false,
            IsFavorite: false,
            DetailUrl: download.DetailUrl,
            DurationSeconds: download.DurationSeconds);
    }

    public MobileStoryDetailResponse CreateOfflineDetail(OfflineStoryDownload download)
    {
        var story = CreateOfflineStory(download);

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

    private static string ArtworkDirectory =>
        IoPath.Combine(FileSystem.AppDataDirectory, "offline-story-artwork");

    private static string MetadataPath =>
        IoPath.Combine(FileSystem.AppDataDirectory, "offline-story-downloads.json");

    private static string BuildAudioPath(string audioFileName) =>
        IoPath.Combine(AudioDirectory, audioFileName);

    private static string BuildArtworkPath(string artworkFileName) =>
        IoPath.Combine(ArtworkDirectory, artworkFileName);

    private async Task SaveDownloadAsync(OfflineStoryDownload download, CancellationToken cancellationToken)
    {
        await _metadataLock.WaitAsync(cancellationToken);
        try
        {
            var existingDownloads = await LoadDownloadsUnsafeAsync(cancellationToken);
            var replacedDownloads = existingDownloads
                .Where(item => IsSameOwnedStory(item, download))
                .ToArray();
            foreach (var replacedDownload in replacedDownloads)
            {
                if (!string.Equals(replacedDownload.AudioFileName, download.AudioFileName, StringComparison.Ordinal))
                {
                    DeleteAudioFile(replacedDownload);
                }

                if (!string.Equals(replacedDownload.ArtworkFileName, download.ArtworkFileName, StringComparison.Ordinal))
                {
                    DeleteArtworkFile(replacedDownload);
                }
            }

            var downloads = existingDownloads
                .Where(item => !IsSameOwnedStory(item, download))
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
            return await ClaimLegacyPaidDownloadsUnsafeAsync(_cachedDownloads, cancellationToken);
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
        }
        catch
        {
            return _cachedDownloads = Array.Empty<OfflineStoryDownload>();
        }

        return await ClaimLegacyPaidDownloadsUnsafeAsync(_cachedDownloads, cancellationToken);
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

        File.Move(temporaryPath, MetadataPath, overwrite: true);
        _cachedDownloads = downloads.ToArray();
    }

    private async Task<IReadOnlyList<OfflineStoryDownload>> ClaimLegacyPaidDownloadsUnsafeAsync(
        IReadOnlyList<OfflineStoryDownload> downloads,
        CancellationToken cancellationToken)
    {
        var currentOwnerKey = ResolveCurrentOwnerKey();
        var legacyDownloadCount = downloads.Count(download =>
            download.RequiresSubscription &&
            string.IsNullOrWhiteSpace(download.OwnerKey));
        if (string.IsNullOrWhiteSpace(currentOwnerKey) || legacyDownloadCount == 0)
        {
            return downloads;
        }

        var claimedDownloads = downloads
            .Select(download =>
                download.RequiresSubscription && string.IsNullOrWhiteSpace(download.OwnerKey)
                    ? download with { OwnerKey = currentOwnerKey }
                    : download)
            .ToArray();
        await SaveDownloadsUnsafeAsync(claimedDownloads, cancellationToken);
        _analytics.TrackEvent("mobile_legacy_downloads_claimed", new Dictionary<string, object>
        {
            ["claimed_count"] = legacyDownloadCount
        });
        return claimedDownloads;
    }

    private bool IsPlayable(OfflineStoryDownload download)
    {
        var session = _sessionState.Current;
        return IsPlayable(download, session, ResolveCurrentOwnerKey(), DateTimeOffset.UtcNow);
    }

    private static bool IsPlayable(
        OfflineStoryDownload download,
        MobileSession session,
        string? currentOwnerKey,
        DateTimeOffset now) =>
        OfflineDownloadAccessPolicy.IsPlayable(
            download.RequiresSubscription,
            download.OwnerKey,
            download.LastAccessVerifiedAt,
            session.IsSignedIn,
            session.HasFullStoryAccess,
            currentOwnerKey,
            now,
            AccessRefreshWindow);

    private static bool IsOwnedByCurrentAccount(OfflineStoryDownload download, string? currentOwnerKey) =>
        OfflineDownloadAccessPolicy.IsOwnedByCurrentAccount(
            download.RequiresSubscription,
            download.OwnerKey,
            currentOwnerKey);

    private string? ResolveCurrentOwnerKey()
    {
        var session = _sessionState.Current;
        if (!session.IsSignedIn)
        {
            return null;
        }

        var ownerKey = OfflineDownloadAccessPolicy.BuildOwnerKey(session.Email);
        if (!string.IsNullOrWhiteSpace(ownerKey))
        {
            Preferences.Default.Set(LastSignedInOwnerKeyPreferenceKey, ownerKey);
            return ownerKey;
        }

        var rememberedOwnerKey = Preferences.Default.Get(LastSignedInOwnerKeyPreferenceKey, string.Empty);
        return string.IsNullOrWhiteSpace(rememberedOwnerKey) ? null : rememberedOwnerKey;
    }

    private static void RememberCurrentOwnerKey(MobileSession session)
    {
        if (!session.IsSignedIn)
        {
            return;
        }

        var ownerKey = OfflineDownloadAccessPolicy.BuildOwnerKey(session.Email);
        if (!string.IsNullOrWhiteSpace(ownerKey))
        {
            Preferences.Default.Set(LastSignedInOwnerKeyPreferenceKey, ownerKey);
        }
    }

    private static bool IsSameStory(OfflineStoryDownload download, string slug, string source) =>
        string.Equals(download.Slug, slug, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(download.Source, source, StringComparison.OrdinalIgnoreCase);

    private static bool IsSameOwnedStory(OfflineStoryDownload left, OfflineStoryDownload right) =>
        IsSameStory(left, right.Slug, right.Source) &&
        string.Equals(left.OwnerKey, right.OwnerKey, StringComparison.Ordinal);

    private static bool RequiresSubscription(string source) =>
        !string.Equals(source, "gratis", StringComparison.OrdinalIgnoreCase);

    private static string BuildStoryKey(string slug, string source) =>
        $"{source.Trim().ToLowerInvariant()}:{slug.Trim().ToLowerInvariant()}";

    private static string BuildDownloadKey(string slug, string source, string? ownerKey)
    {
        var storyKey = BuildStoryKey(slug, source);
        return string.IsNullOrWhiteSpace(ownerKey)
            ? storyKey
            : $"{storyKey}:{ownerKey}";
    }

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

    private static void DeleteArtworkFile(OfflineStoryDownload download)
    {
        if (string.IsNullOrWhiteSpace(download.ArtworkFileName))
        {
            return;
        }

        DeleteArtworkFileByName(download.ArtworkFileName);
    }

    private static void DeleteArtworkFileByName(string artworkFileName)
    {
        var artworkPath = BuildArtworkPath(artworkFileName);
        if (File.Exists(artworkPath))
        {
            File.Delete(artworkPath);
        }
    }

    private static bool HasOfflineArtwork(OfflineStoryDownload download) =>
        !string.IsNullOrWhiteSpace(download.ArtworkFileName) &&
        File.Exists(BuildArtworkPath(download.ArtworkFileName));

    private static string ResolveOfflineArtworkUrl(OfflineStoryDownload download)
    {
        if (HasOfflineArtwork(download))
        {
            return new Uri(BuildArtworkPath(download.ArtworkFileName!)).AbsoluteUri;
        }

        return string.IsNullOrWhiteSpace(download.ImageUrl)
            ? download.ThumbnailUrl
            : download.ImageUrl;
    }

    private async Task<string> SaveArtworkFileAsync(
        string imageUrl,
        string thumbnailUrl,
        string downloadKey,
        CancellationToken cancellationToken)
    {
        Exception? lastError = null;
        var candidates = new[] { imageUrl, thumbnailUrl }
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in candidates)
        {
            try
            {
                var source = await _apiClient.CacheImageSourceAsync(candidate, cancellationToken);
                if (source is not FileImageSource fileSource ||
                    string.IsNullOrWhiteSpace(fileSource.File) ||
                    !File.Exists(fileSource.File))
                {
                    continue;
                }

                Directory.CreateDirectory(ArtworkDirectory);
                var extension = ResolveArtworkExtension(fileSource.File);
                var artworkFileName = $"{BuildStableKey(downloadKey)}{extension}";
                var artworkPath = BuildArtworkPath(artworkFileName);
                var temporaryPath = $"{artworkPath}.tmp";
                try
                {
                    File.Copy(fileSource.File, temporaryPath, overwrite: true);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!File.Exists(temporaryPath) || new FileInfo(temporaryPath).Length == 0)
                    {
                        throw new InvalidOperationException("Die storie se kunswerk is leeg.");
                    }

                    File.Move(temporaryPath, artworkPath, overwrite: true);
                    return artworkFileName;
                }
                finally
                {
                    if (File.Exists(temporaryPath))
                    {
                        File.Delete(temporaryPath);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastError = ex;
            }
        }

        throw new InvalidOperationException(
            "Die storie se kunswerk kon nie vir offline gebruik gestoor word nie.",
            lastError);
    }

    private static string ResolveArtworkExtension(string sourcePath)
    {
        var extension = IoPath.GetExtension(sourcePath).ToLowerInvariant();
        return extension is ".jpg" or ".jpeg" or ".png" or ".webp" or ".gif"
            ? extension
            : ".jpg";
    }

    private void QueueArtworkRepair(OfflineStoryDownload download)
    {
        var key = BuildDownloadKey(download.Slug, download.Source, download.OwnerKey);
        lock (_activeArtworkRepairs)
        {
            if (!_activeArtworkRepairs.Add(key))
            {
                return;
            }
        }

        _ = RepairArtworkAsync(download, key);
    }

    private async Task RepairArtworkAsync(OfflineStoryDownload download, string key)
    {
        try
        {
            var artworkFileName = await SaveArtworkFileAsync(
                download.ImageUrl,
                download.ThumbnailUrl,
                key,
                CancellationToken.None);

            await _metadataLock.WaitAsync();
            try
            {
                var downloads = (await LoadDownloadsUnsafeAsync(CancellationToken.None)).ToList();
                var index = downloads.FindIndex(item => IsSameOwnedStory(item, download));
                if (index < 0)
                {
                    DeleteArtworkFile(download with { ArtworkFileName = artworkFileName });
                    return;
                }

                downloads[index] = downloads[index] with { ArtworkFileName = artworkFileName };
                await SaveDownloadsUnsafeAsync(downloads, CancellationToken.None);
            }
            finally
            {
                _metadataLock.Release();
            }

            DownloadsChanged?.Invoke(this, EventArgs.Empty);
        }
        catch
        {
            // Legacy artwork repair is best-effort and retries next time downloads are opened online.
        }
        finally
        {
            lock (_activeArtworkRepairs)
            {
                _activeArtworkRepairs.Remove(key);
            }
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
