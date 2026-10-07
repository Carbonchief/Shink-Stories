using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Options;

namespace Shink.Services;

public sealed class SupabaseSubscriberPlaylistService(
    HttpClient httpClient,
    IOptions<SupabaseOptions> options,
    AuthenticationStateProvider authenticationStateProvider,
    IStoryCatalogService storyCatalogService,
    ILogger<SupabaseSubscriberPlaylistService> logger) : ISubscriberPlaylistService
{
    public const int MaximumTitleLength = 80;
    public const int MaximumStories = 100;
    public const int MaximumPlaylists = 50;
    private const string UnavailableMessage = "Ons kon nie jou speellyste laai nie. Probeer asseblief weer.";

    public async Task<SubscriberPlaylistListResult> GetPlaylistsAsync(Guid? playlistId = null, CancellationToken cancellationToken = default)
    {
        var email = await GetSignedInEmailAsync();
        if (email is null)
        {
            return new(false, [], "Teken in om jou speellyste te sien.");
        }

        var json = await CallRpcAsync("get_subscriber_playlists", new { p_email = email, p_playlist_id = playlistId }, cancellationToken);
        var playlists = ParsePlaylists(json);
        return playlists is null ? new(false, [], UnavailableMessage) : new(true, playlists);
    }

    public async Task<SubscriberPlaylistSaveResult> SavePlaylistAsync(Guid? playlistId, string title, IReadOnlyList<string> storySlugs, CancellationToken cancellationToken = default)
    {
        var email = await GetSignedInEmailAsync();
        if (email is null)
        {
            return new(false, ErrorMessage: "Teken in om jou speellys te stoor.");
        }

        var normalizedTitle = title.Trim();
        if (normalizedTitle.Length is 0 or > MaximumTitleLength)
        {
            return new(false, ErrorMessage: "Gee jou speellys 'n naam van 1 tot 80 karakters.");
        }

        var slugs = storySlugs.Select(slug => slug.Trim().ToLowerInvariant()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (slugs.Length > MaximumStories || slugs.Any(slug => slug.Length is 0 or > 160))
        {
            return new(false, ErrorMessage: "Kies hoogstens 100 stories vir jou speellys.");
        }

        var catalog = await storyCatalogService.GetLuisterStoriesAsync(cancellationToken);
        var availableSlugs = catalog.Where(IsAudioStory).Select(story => story.Slug).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (slugs.Any(slug => !availableSlugs.Contains(slug)))
        {
            return new(false, ErrorMessage: "Een van die stories is nie meer beskikbaar nie. Kies asseblief weer.");
        }

        var json = await CallRpcAsync("save_subscriber_playlist", new
        {
            p_email = email,
            p_playlist_id = playlistId,
            p_title = normalizedTitle,
            p_story_slugs = slugs
        }, cancellationToken);
        var saved = ParsePlaylists(json)?.SingleOrDefault();
        return saved is null
            ? new(false, ErrorMessage: "Ons kon nie jou speellys stoor nie. Jy kan tot 50 speellyste hê. Probeer asseblief weer.")
            : new(true, saved);
    }

    public async Task<bool> DeletePlaylistAsync(Guid playlistId, CancellationToken cancellationToken = default)
    {
        var email = await GetSignedInEmailAsync();
        if (email is null || playlistId == Guid.Empty)
        {
            return false;
        }

        var json = await CallRpcAsync("delete_subscriber_playlist", new { p_email = email, p_playlist_id = playlistId }, cancellationToken);
        return string.Equals(json?.Trim(), "true", StringComparison.Ordinal);
    }

    public static bool IsAudioStory(Shink.Components.Content.StoryItem story) =>
        !string.Equals(story.StoryType, "video", StringComparison.OrdinalIgnoreCase);

    private async Task<string?> GetSignedInEmailAsync()
    {
        var user = (await authenticationStateProvider.GetAuthenticationStateAsync()).User;
        if (user.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        var email = user.FindFirst(ClaimTypes.Email)?.Value ?? user.Identity.Name;
        return string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();
    }

    private async Task<string?> CallRpcAsync(string functionName, object payload, CancellationToken cancellationToken)
    {
        var configuration = options.Value;
        if (string.IsNullOrWhiteSpace(configuration.SecretKey) || string.IsNullOrWhiteSpace(configuration.Url) ||
            !Uri.TryCreate(configuration.Url.TrimEnd('/') + "/", UriKind.Absolute, out var baseUri))
        {
            return null;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUri, $"rest/v1/rpc/{functionName}"))
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };
            request.Headers.TryAddWithoutValidation("apikey", configuration.SecretKey);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", configuration.SecretKey);
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                // Provider bodies may contain private playlist names; keep them out of logs.
                logger.LogWarning("Personal playlist operation {Function} failed. Status={Status}", functionName, (int)response.StatusCode);
                return null;
            }

            return await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            logger.LogWarning("Personal playlist operation {Function} could not reach Supabase.", functionName);
            return null;
        }
    }

    private static IReadOnlyList<SubscriberPlaylist>? ParsePlaylists(string? json)
    {
        if (json is null)
        {
            return null;
        }
        try
        {
            var rows = JsonSerializer.Deserialize<List<PlaylistRow>>(json);
            return rows?.Select(row => new SubscriberPlaylist(row.PlaylistId, row.Title, row.StorySlugs)).ToArray();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed class PlaylistRow
    {
        [JsonPropertyName("playlist_id")] public Guid PlaylistId { get; set; }
        [JsonPropertyName("title")] public string Title { get; set; } = string.Empty;
        [JsonPropertyName("story_slugs")] public string[] StorySlugs { get; set; } = [];
    }
}
