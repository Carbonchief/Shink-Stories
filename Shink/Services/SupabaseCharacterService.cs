using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Shink.Services;

public sealed partial class SupabaseCharacterService(
    HttpClient httpClient,
    IOptions<SupabaseOptions> supabaseOptions,
    IMemoryCache memoryCache,
    ILogger<SupabaseCharacterService> logger) : ICharacterCatalogService, ICharacterAdminService, ICharacterTrackingService
{
    private const string PublishedCharactersCacheKey = "story-characters:published:v1";
    private const string SubscriberCachePrefix = "character-tracking:subscriber:";
    private static readonly TimeSpan PublishedCharactersCacheDuration = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan SubscriberCacheDuration = TimeSpan.FromMinutes(10);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient = httpClient;
    private readonly SupabaseOptions _options = supabaseOptions.Value;
    private readonly IMemoryCache _memoryCache = memoryCache;
    private readonly ILogger<SupabaseCharacterService> _logger = logger;

    public async Task<IReadOnlyList<StoryCharacterItem>> GetPublishedCharactersAsync(CancellationToken cancellationToken = default)
    {
        if (_memoryCache.TryGetValue(PublishedCharactersCacheKey, out IReadOnlyList<StoryCharacterItem>? cachedCharacters) &&
            cachedCharacters is not null)
        {
            return cachedCharacters;
        }

        if (!TryBuildSupabaseBaseUri(out var baseUri))
        {
            _logger.LogWarning("Supabase characters lookup skipped: URL is not configured.");
            return Array.Empty<StoryCharacterItem>();
        }

        var apiKey = ResolveReadApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("Supabase characters lookup skipped: PublishableKey is not configured.");
            return Array.Empty<StoryCharacterItem>();
        }

        try
        {
            var requestUri = new Uri(
                baseUri,
                "rest/v1/story_characters" +
                "?select=character_id,slug,display_name,character_category,unlock_rules,tagline,species,habitat,catchphrase,favorite_thing,character_trait,golden_lesson,core_value,first_appearance,friends,reflection_question,challenge_text,image_path,mystery_image_path,unlock_story_slug,related_story_slugs,audio_clips,unlock_threshold_seconds,sort_order,status" +
                "&status=eq.published" +
                "&order=sort_order.asc" +
                "&order=display_name.asc");

            using var request = CreateRequest(HttpMethod.Get, requestUri, apiKey);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    _logger.LogInformation("Supabase characters lookup skipped: table story_characters is not available yet.");
                }
                else
                {
                    var body = await response.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogWarning(
                        "Supabase characters lookup failed. Status={StatusCode} Body={Body}",
                        (int)response.StatusCode,
                        body);
                }

                return Array.Empty<StoryCharacterItem>();
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var rows = await JsonSerializer.DeserializeAsync<List<StoryCharacterRow>>(stream, JsonOptions, cancellationToken)
                ?? [];

            var characters = rows
                .Where(row => row.CharacterId != Guid.Empty)
                .Where(row => !string.IsNullOrWhiteSpace(row.Slug))
                .Where(row => !string.IsNullOrWhiteSpace(row.DisplayName))
                .Where(row => string.Equals(row.Status, "published", StringComparison.OrdinalIgnoreCase))
                .Select(MapToPublicCharacter)
                .OrderBy(character => character.SortOrder)
                .ThenBy(character => character.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            _memoryCache.Set(PublishedCharactersCacheKey, characters, PublishedCharactersCacheDuration);
            return characters;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(exception, "Supabase characters lookup failed unexpectedly.");
            return Array.Empty<StoryCharacterItem>();
        }
    }

    public async Task<CharacterAudioClipItem?> FindPublishedAudioClipByStreamSlugAsync(
        string? streamSlug,
        CancellationToken cancellationToken = default)
    {
        var normalizedStreamSlug = NormalizeOptionalSlug(streamSlug);
        if (string.IsNullOrWhiteSpace(normalizedStreamSlug))
        {
            return null;
        }

        var publishedCharacters = await GetPublishedCharactersAsync(cancellationToken);
        foreach (var character in publishedCharacters)
        {
            var clip = character.AudioClips.FirstOrDefault(candidate =>
                string.Equals(candidate.StreamSlug, normalizedStreamSlug, StringComparison.OrdinalIgnoreCase));
            if (clip is not null)
            {
                return clip;
            }
        }

        return null;
    }

    public async Task<IReadOnlyList<AdminCharacterRecord>> GetCharactersAsync(
        string? adminEmail,
        CancellationToken cancellationToken = default)
    {
        if (!await TryResolveAdminContextAsync(adminEmail, cancellationToken))
        {
            return Array.Empty<AdminCharacterRecord>();
        }

        if (!TryBuildSupabaseBaseUri(out var baseUri))
        {
            return Array.Empty<AdminCharacterRecord>();
        }

        var apiKey = ResolveSecretKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return Array.Empty<AdminCharacterRecord>();
        }

        try
        {
            var requestUri = new Uri(
                baseUri,
                "rest/v1/story_characters" +
                "?select=character_id,slug,display_name,character_category,unlock_rules,tagline,species,habitat,catchphrase,favorite_thing,character_trait,golden_lesson,core_value,first_appearance,friends,reflection_question,challenge_text,image_path,mystery_image_path,image_drive_file_id,mystery_image_drive_file_id,unlock_story_slug,related_story_slugs,audio_clips,unlock_threshold_seconds,sort_order,status,updated_at" +
                "&order=sort_order.asc" +
                "&order=display_name.asc");

            using var request = CreateRequest(HttpMethod.Get, requestUri, apiKey);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    return Array.Empty<AdminCharacterRecord>();
                }

                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "Admin characters lookup failed. Status={StatusCode} Body={Body}",
                    (int)response.StatusCode,
                    body);
                return Array.Empty<AdminCharacterRecord>();
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var rows = await JsonSerializer.DeserializeAsync<List<StoryCharacterRow>>(stream, JsonOptions, cancellationToken)
                ?? [];

            return rows
                .Where(row => row.CharacterId != Guid.Empty)
                .Where(row => !string.IsNullOrWhiteSpace(row.Slug))
                .Where(row => !string.IsNullOrWhiteSpace(row.DisplayName))
                .Select(MapToAdminCharacter)
                .OrderBy(character => character.SortOrder)
                .ThenBy(character => character.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(exception, "Admin characters lookup failed unexpectedly.");
            return Array.Empty<AdminCharacterRecord>();
        }
    }

    public async Task<bool> RecordProfileListenAsync(
        string? email,
        CharacterProfileListenTrackingRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email) ||
            request.CharacterId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.CharacterSlug) ||
            string.IsNullOrWhiteSpace(request.StreamSlug))
        {
            return false;
        }

        if (!TryBuildSupabaseBaseUri(out var baseUri))
        {
            _logger.LogWarning("Character profile listen tracking skipped: URL is not configured.");
            return false;
        }

        var apiKey = ResolveSecretKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("Character profile listen tracking skipped: SecretKey is not configured.");
            return false;
        }

        try
        {
            var subscriberId = await ResolveOrCreateSubscriberIdAsync(baseUri, apiKey, email, cancellationToken);
            if (string.IsNullOrWhiteSpace(subscriberId))
            {
                return false;
            }

            var payload = new
            {
                subscriber_id = subscriberId,
                character_id = request.CharacterId,
                character_slug = NormalizeOptionalSlug(request.CharacterSlug),
                stream_slug = NormalizeOptionalSlug(request.StreamSlug),
                metadata = new
                {
                    source = NormalizeOptionalText(request.Source, 40)
                }
            };

            using var insertRequest = CreateJsonRequest(
                HttpMethod.Post,
                new Uri(baseUri, "rest/v1/character_audio_plays"),
                apiKey,
                payload,
                "return=minimal");
            using var insertResponse = await _httpClient.SendAsync(insertRequest, cancellationToken);
            if (!insertResponse.IsSuccessStatusCode)
            {
                var body = await insertResponse.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "Character profile listen tracking failed. character_id={CharacterId} Status={StatusCode} Body={Body}",
                    request.CharacterId,
                    (int)insertResponse.StatusCode,
                    body);
                return false;
            }

            return true;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(exception, "Character profile listen tracking failed unexpectedly.");
            return false;
        }
    }

    public async Task<IReadOnlyList<UserCharacterProfileListenItem>> GetUserProfileListenStatsAsync(
        string? email,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return Array.Empty<UserCharacterProfileListenItem>();
        }

        if (!TryBuildSupabaseBaseUri(out var baseUri))
        {
            _logger.LogWarning("Character profile listen lookup skipped: URL is not configured.");
            return Array.Empty<UserCharacterProfileListenItem>();
        }

        var apiKey = ResolveSecretKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("Character profile listen lookup skipped: SecretKey is not configured.");
            return Array.Empty<UserCharacterProfileListenItem>();
        }

        try
        {
            var subscriberId = await ResolveOrCreateSubscriberIdAsync(baseUri, apiKey, email, cancellationToken);
            if (string.IsNullOrWhiteSpace(subscriberId))
            {
                return Array.Empty<UserCharacterProfileListenItem>();
            }

            var escapedSubscriberId = Uri.EscapeDataString(subscriberId);
            var requestUri = new Uri(
                baseUri,
                "rest/v1/character_audio_plays" +
                "?select=character_id,character_slug,occurred_at" +
                $"&subscriber_id=eq.{escapedSubscriberId}" +
                "&order=occurred_at.desc" +
                "&limit=2500");

            using var request = CreateRequest(HttpMethod.Get, requestUri, apiKey);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "Character profile listen lookup failed. Status={StatusCode} Body={Body}",
                    (int)response.StatusCode,
                    body);
                return Array.Empty<UserCharacterProfileListenItem>();
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var rows = await JsonSerializer.DeserializeAsync<List<CharacterAudioPlayRow>>(stream, JsonOptions, cancellationToken)
                ?? [];

            return rows
                .Where(row => row.CharacterId != Guid.Empty)
                .Where(row => !string.IsNullOrWhiteSpace(row.CharacterSlug))
                .GroupBy(row => new
                {
                    row.CharacterId,
                    CharacterSlug = row.CharacterSlug.Trim().ToLowerInvariant()
                })
                .Select(group => new UserCharacterProfileListenItem(
                    CharacterId: group.Key.CharacterId,
                    CharacterSlug: group.Key.CharacterSlug,
                    ListenCount: group.Count(),
                    LastListenedAtUtc: group.Max(item => item.OccurredAt)))
                .OrderByDescending(item => item.LastListenedAtUtc)
                .ToArray();
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(exception, "Character profile listen lookup failed unexpectedly.");
            return Array.Empty<UserCharacterProfileListenItem>();
        }
    }

    public async Task<AdminOperationResult> SaveCharacterAsync(
        string? adminEmail,
        AdminCharacterSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!await TryResolveAdminContextAsync(adminEmail, cancellationToken))
        {
            return new AdminOperationResult(false, "Jy het nie admin toegang nie.");
        }

        if (!TryBuildSupabaseBaseUri(out var baseUri))
        {
            return new AdminOperationResult(false, "Supabase URL is nog nie opgestel nie.");
        }

        var apiKey = ResolveSecretKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new AdminOperationResult(false, "Supabase SecretKey is nog nie opgestel nie.");
        }

        var normalizedDisplayName = NormalizeOptionalText(request.DisplayName, 140);
        if (string.IsNullOrWhiteSpace(normalizedDisplayName))
        {
            return new AdminOperationResult(false, "Karakternaam is verpligtend.");
        }

        var normalizedSlug = NormalizeSlugCandidate(request.Slug, normalizedDisplayName);
        if (!CharacterSlugRegex().IsMatch(normalizedSlug))
        {
            return new AdminOperationResult(false, "Karakter slug is ongeldig.");
        }

        var normalizedStatus = request.Status?.Trim().ToLowerInvariant() switch
        {
            "draft" => "draft",
            "published" => "published",
            "archived" => "archived",
            _ => string.Empty
        };
        if (string.IsNullOrWhiteSpace(normalizedStatus))
        {
            return new AdminOperationResult(false, "Status moet 'draft', 'published' of 'archived' wees.");
        }

        var normalizedCharacterCategory = NormalizeCharacterCategory(request.CharacterCategory);
        if (string.IsNullOrWhiteSpace(normalizedCharacterCategory))
        {
            return new AdminOperationResult(false, "Karakter kategorie moet 'hoofkarakter', 'ander', 'bonus' of 'bonus-2' wees.");
        }

        var normalizedUnlockStorySlug = NormalizeOptionalSlug(request.UnlockStorySlug);
        var normalizedRelatedStorySlugs = NormalizeSlugList(request.RelatedStorySlugs);
        if (!string.IsNullOrWhiteSpace(normalizedUnlockStorySlug) &&
            !normalizedRelatedStorySlugs.Contains(normalizedUnlockStorySlug, StringComparer.OrdinalIgnoreCase))
        {
            normalizedRelatedStorySlugs = [normalizedUnlockStorySlug, .. normalizedRelatedStorySlugs];
        }

        var normalizedAudioClips = NormalizeAudioClipList(request.AudioClips);
        var normalizedUnlockRules = NormalizeUnlockRuleList(request.UnlockRules);

        var payload = new Dictionary<string, object?>
        {
            ["slug"] = normalizedSlug,
            ["display_name"] = normalizedDisplayName,
            ["character_category"] = normalizedCharacterCategory,
            ["unlock_rules"] = normalizedUnlockRules.Select(rule => new Dictionary<string, object?>
            {
                ["rule_type"] = rule.RuleType,
                ["target_slugs"] = rule.TargetSlugs,
                ["target_match_mode"] = rule.TargetMatchMode,
                ["minimum_count"] = rule.MinimumCount,
                ["minimum_seconds"] = rule.MinimumSeconds
            }).ToArray(),
            ["tagline"] = NormalizeOptionalText(request.Tagline, 320),
            ["species"] = NormalizeOptionalText(request.Species, 120),
            ["habitat"] = NormalizeOptionalText(request.Habitat, 180),
            ["catchphrase"] = NormalizeOptionalText(request.Catchphrase, 320),
            ["favorite_thing"] = NormalizeOptionalText(request.FavoriteThing, 320),
            ["character_trait"] = NormalizeOptionalText(request.CharacterTrait, 320),
            ["golden_lesson"] = NormalizeOptionalText(request.GoldenLesson, 320),
            ["core_value"] = NormalizeOptionalText(request.CoreValue, 160),
            ["first_appearance"] = NormalizeOptionalText(request.FirstAppearance, 320),
            ["friends"] = NormalizeOptionalText(request.Friends, 320),
            ["reflection_question"] = NormalizeOptionalText(request.ReflectionQuestion, 512),
            ["challenge_text"] = NormalizeOptionalText(request.ChallengeText, 800),
            ["image_path"] = NormalizeOptionalText(request.ImagePath, 2048),
            ["mystery_image_path"] = NormalizeOptionalText(request.MysteryImagePath, 2048),
            ["image_drive_file_id"] = NormalizeOptionalText(request.ImageDriveFileId, 128),
            ["mystery_image_drive_file_id"] = NormalizeOptionalText(request.MysteryImageDriveFileId, 128),
            ["unlock_story_slug"] = normalizedUnlockStorySlug,
            ["related_story_slugs"] = normalizedRelatedStorySlugs,
            ["audio_clips"] = normalizedAudioClips.Select(clip => new Dictionary<string, object?>
            {
                ["stream_slug"] = clip.StreamSlug,
                ["title"] = clip.Title,
                ["audio_provider"] = clip.AudioProvider,
                ["audio_object_key"] = clip.AudioObjectKey,
                ["audio_content_type"] = clip.AudioContentType,
                ["sort_order"] = clip.SortOrder
            }).ToArray(),
            ["unlock_threshold_seconds"] = Math.Clamp(request.UnlockThresholdSeconds, 1, 3600),
            ["status"] = normalizedStatus,
            ["sort_order"] = Math.Clamp(request.SortOrder, -500_000, 500_000)
        };

        try
        {
            if (request.CharacterId is Guid characterId && characterId != Guid.Empty)
            {
                var updateUri = new Uri(baseUri, $"rest/v1/story_characters?character_id=eq.{Uri.EscapeDataString(characterId.ToString("D"))}");
                using var updateRequest = CreateJsonRequest(new HttpMethod("PATCH"), updateUri, apiKey, payload, "return=representation");
                using var updateResponse = await _httpClient.SendAsync(updateRequest, cancellationToken);
                if (!updateResponse.IsSuccessStatusCode)
                {
                    var body = await updateResponse.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogWarning(
                        "Character update failed. character_id={CharacterId} Status={StatusCode} Body={Body}",
                        characterId,
                        (int)updateResponse.StatusCode,
                        body);
                    return new AdminOperationResult(false, "Kon nie karakter nou opdateer nie.");
                }

                InvalidateCharacterCache();
                return new AdminOperationResult(true, EntityId: characterId);
            }

            var insertUri = new Uri(baseUri, "rest/v1/story_characters?select=character_id");
            using var insertRequest = CreateJsonRequest(HttpMethod.Post, insertUri, apiKey, payload, "return=representation");
            using var insertResponse = await _httpClient.SendAsync(insertRequest, cancellationToken);
            if (!insertResponse.IsSuccessStatusCode)
            {
                var body = await insertResponse.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "Character create failed. slug={Slug} Status={StatusCode} Body={Body}",
                    normalizedSlug,
                    (int)insertResponse.StatusCode,
                    body);
                return new AdminOperationResult(false, "Kon nie karakter nou skep nie.");
            }

            var responseBody = await insertResponse.Content.ReadAsStringAsync(cancellationToken);
            var createdCharacterId = ReadFirstGuidProperty(responseBody, "character_id");
            InvalidateCharacterCache();
            return new AdminOperationResult(true, EntityId: createdCharacterId);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(exception, "Character save failed unexpectedly.");
            return new AdminOperationResult(false, "Kon nie karakter nou stoor nie.");
        }
    }

    private StoryCharacterItem MapToPublicCharacter(StoryCharacterRow row)
    {
        var relatedStorySlugs = NormalizeSlugList(row.RelatedStorySlugs);
        var audioClips = NormalizeAudioClipList(row.AudioClips);

        return new StoryCharacterItem(
            CharacterId: row.CharacterId,
            Slug: row.Slug.Trim(),
            DisplayName: row.DisplayName.Trim(),
            CharacterCategory: NormalizeCharacterCategory(row.CharacterCategory) ?? "ander",
            UnlockRules: NormalizeUnlockRuleList(row.UnlockRules),
            Tagline: NormalizeOptionalText(row.Tagline, 320),
            Species: NormalizeOptionalText(row.Species, 120),
            Habitat: NormalizeOptionalText(row.Habitat, 180),
            Catchphrase: NormalizeOptionalText(row.Catchphrase, 320),
            FavoriteThing: NormalizeOptionalText(row.FavoriteThing, 320),
            CharacterTrait: NormalizeOptionalText(row.CharacterTrait, 320),
            GoldenLesson: NormalizeOptionalText(row.GoldenLesson, 320),
            CoreValue: NormalizeOptionalText(row.CoreValue, 160),
            FirstAppearance: NormalizeOptionalText(row.FirstAppearance, 320),
            Friends: NormalizeOptionalText(row.Friends, 320),
            ReflectionQuestion: NormalizeOptionalText(row.ReflectionQuestion, 512),
            ChallengeText: NormalizeOptionalText(row.ChallengeText, 800),
            ImagePath: NormalizeOptionalText(row.ImagePath, 2048),
            MysteryImagePath: NormalizeOptionalText(row.MysteryImagePath, 2048),
            UnlockStorySlug: NormalizeOptionalSlug(row.UnlockStorySlug),
            RelatedStorySlugs: relatedStorySlugs,
            AudioClips: audioClips,
            UnlockThresholdSeconds: Math.Clamp(row.UnlockThresholdSeconds ?? 30, 1, 3600),
            SortOrder: row.SortOrder,
            UpdatedAt: row.UpdatedAt);
    }

    private AdminCharacterRecord MapToAdminCharacter(StoryCharacterRow row)
    {
        var audioClips = NormalizeAudioClipList(row.AudioClips);

        return new AdminCharacterRecord(
            CharacterId: row.CharacterId,
            Slug: row.Slug.Trim(),
            DisplayName: row.DisplayName.Trim(),
            CharacterCategory: NormalizeCharacterCategory(row.CharacterCategory) ?? "ander",
            UnlockRules: NormalizeUnlockRuleList(row.UnlockRules),
            Tagline: NormalizeOptionalText(row.Tagline, 320),
            Species: NormalizeOptionalText(row.Species, 120),
            Habitat: NormalizeOptionalText(row.Habitat, 180),
            Catchphrase: NormalizeOptionalText(row.Catchphrase, 320),
            FavoriteThing: NormalizeOptionalText(row.FavoriteThing, 320),
            CharacterTrait: NormalizeOptionalText(row.CharacterTrait, 320),
            GoldenLesson: NormalizeOptionalText(row.GoldenLesson, 320),
            CoreValue: NormalizeOptionalText(row.CoreValue, 160),
            FirstAppearance: NormalizeOptionalText(row.FirstAppearance, 320),
            Friends: NormalizeOptionalText(row.Friends, 320),
            ReflectionQuestion: NormalizeOptionalText(row.ReflectionQuestion, 512),
            ChallengeText: NormalizeOptionalText(row.ChallengeText, 800),
            ImagePath: NormalizeOptionalText(row.ImagePath, 2048),
            MysteryImagePath: NormalizeOptionalText(row.MysteryImagePath, 2048),
            ImageDriveFileId: NormalizeOptionalText(row.ImageDriveFileId, 128),
            MysteryImageDriveFileId: NormalizeOptionalText(row.MysteryImageDriveFileId, 128),
            UnlockStorySlug: NormalizeOptionalSlug(row.UnlockStorySlug),
            RelatedStorySlugs: NormalizeSlugList(row.RelatedStorySlugs),
            AudioClips: audioClips,
            UnlockThresholdSeconds: Math.Clamp(row.UnlockThresholdSeconds ?? 30, 1, 3600),
            Status: string.IsNullOrWhiteSpace(row.Status) ? "draft" : row.Status.Trim().ToLowerInvariant(),
            SortOrder: row.SortOrder,
            UpdatedAt: row.UpdatedAt);
    }

    private async Task<bool> TryResolveAdminContextAsync(string? adminEmail, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(adminEmail))
        {
            return false;
        }

        if (!TryBuildSupabaseBaseUri(out var baseUri))
        {
            _logger.LogWarning("Character admin lookup skipped: Supabase URL is not configured.");
            return false;
        }

        var apiKey = ResolveSecretKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("Character admin lookup skipped: SecretKey is not configured.");
            return false;
        }

        var normalizedEmail = adminEmail.Trim().ToLowerInvariant();
        var requestUri = new Uri(baseUri, $"rest/v1/admin_users?select=admin_user_id&email=eq.{Uri.EscapeDataString(normalizedEmail)}&is_enabled=eq.true&limit=1");

        using var request = CreateRequest(HttpMethod.Get, requestUri, apiKey);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "Character admin lookup failed. email={Email} Status={StatusCode} Body={Body}",
                normalizedEmail,
                (int)response.StatusCode,
                body);
            return false;
        }

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        return responseBody.Contains("admin_user_id", StringComparison.OrdinalIgnoreCase);
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, Uri requestUri, string apiKey)
    {
        var request = new HttpRequestMessage(method, requestUri);
        request.Headers.TryAddWithoutValidation("apikey", apiKey);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return request;
    }

    private HttpRequestMessage CreateJsonRequest(
        HttpMethod method,
        Uri requestUri,
        string apiKey,
        object payload,
        string? prefer = null)
    {
        var request = CreateRequest(method, requestUri, apiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");
        if (!string.IsNullOrWhiteSpace(prefer))
        {
            request.Headers.TryAddWithoutValidation("Prefer", prefer);
        }

        return request;
    }

    private bool TryBuildSupabaseBaseUri(out Uri baseUri)
    {
        baseUri = null!;
        var url = _options.Url?.Trim();
        if (string.IsNullOrWhiteSpace(url) ||
            !Uri.TryCreate(url, UriKind.Absolute, out var resolvedUri))
        {
            return false;
        }

        baseUri = resolvedUri;
        return true;
    }

    private string? ResolveReadApiKey() =>
        string.IsNullOrWhiteSpace(_options.PublishableKey)
            ? null
            : _options.PublishableKey.Trim();

    private string? ResolveSecretKey() =>
        string.IsNullOrWhiteSpace(_options.SecretKey)
            ? null
            : _options.SecretKey.Trim();

    private static string? NormalizeOptionalText(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength
            ? trimmed
            : trimmed[..maxLength].TrimEnd();
    }

    private static string? NormalizeOptionalSlug(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim().ToLowerInvariant();
        return CharacterSlugRegex().IsMatch(trimmed) ? trimmed : null;
    }

    private static string? NormalizeCharacterCategory(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "hoofkarakter" => "hoofkarakter",
            "ander" => "ander",
            "bonus" => "bonus",
            "bonus-2" or "bonus 2" => "bonus-2",
            _ => null
        };
    }

    private static CharacterUnlockRuleItem[] NormalizeUnlockRuleList(IReadOnlyList<CharacterUnlockRuleItem>? values)
    {
        return (values ?? Array.Empty<CharacterUnlockRuleItem>())
            .Select(NormalizeUnlockRule)
            .Where(static rule => rule is not null)
            .Cast<CharacterUnlockRuleItem>()
            .ToArray();
    }

    private static CharacterUnlockRuleItem[] NormalizeUnlockRuleList(JsonElement? value)
    {
        if (value is null || value.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return Array.Empty<CharacterUnlockRuleItem>();
        }

        if (value.Value.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<CharacterUnlockRuleItem>();
        }

        var rules = new List<CharacterUnlockRuleItem>();
        foreach (var item in value.Value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var targetSlugs = item.TryGetProperty("target_slugs", out var targetSlugsNode) &&
                              targetSlugsNode.ValueKind == JsonValueKind.Array
                ? targetSlugsNode
                    .EnumerateArray()
                    .Where(entry => entry.ValueKind == JsonValueKind.String)
                    .Select(entry => entry.GetString() ?? string.Empty)
                    .ToArray()
                : Array.Empty<string>();

            var rule = NormalizeUnlockRule(new CharacterUnlockRuleItem(
                RuleType: ReadJsonString(item, "rule_type") ?? string.Empty,
                TargetSlugs: targetSlugs,
                TargetMatchMode: ReadJsonString(item, "target_match_mode") ?? CharacterUnlockEvaluator.MatchModeAny,
                MinimumCount: ReadJsonInt(item, "minimum_count"),
                MinimumSeconds: ReadJsonInt(item, "minimum_seconds")));

            if (rule is not null)
            {
                rules.Add(rule);
            }
        }

        return rules.ToArray();
    }

    private static CharacterUnlockRuleItem? NormalizeUnlockRule(CharacterUnlockRuleItem rule)
    {
        var normalizedRuleType = rule.RuleType?.Trim().ToLowerInvariant() switch
        {
            CharacterUnlockEvaluator.RuleTypeStoryListenSeconds => CharacterUnlockEvaluator.RuleTypeStoryListenSeconds,
            CharacterUnlockEvaluator.RuleTypeStoryRepeatCount => CharacterUnlockEvaluator.RuleTypeStoryRepeatCount,
            CharacterUnlockEvaluator.RuleTypeStoryCount => CharacterUnlockEvaluator.RuleTypeStoryCount,
            CharacterUnlockEvaluator.RuleTypeUnlockedCharacterCount => CharacterUnlockEvaluator.RuleTypeUnlockedCharacterCount,
            CharacterUnlockEvaluator.RuleTypeProfileListenCount => CharacterUnlockEvaluator.RuleTypeProfileListenCount,
            _ => null
        };

        if (string.IsNullOrWhiteSpace(normalizedRuleType))
        {
            return null;
        }

        return new CharacterUnlockRuleItem(
            RuleType: normalizedRuleType,
            TargetSlugs: NormalizeSlugList(rule.TargetSlugs),
            TargetMatchMode: string.Equals(rule.TargetMatchMode, CharacterUnlockEvaluator.MatchModeAll, StringComparison.OrdinalIgnoreCase)
                ? CharacterUnlockEvaluator.MatchModeAll
                : CharacterUnlockEvaluator.MatchModeAny,
            MinimumCount: Math.Max(0, rule.MinimumCount),
            MinimumSeconds: Math.Max(0, rule.MinimumSeconds));
    }

    private static string NormalizeSlugCandidate(string? slug, string fallbackDisplayName)
    {
        var candidate = string.IsNullOrWhiteSpace(slug)
            ? fallbackDisplayName
            : slug;

        var lowerInvariant = candidate.Trim().ToLowerInvariant();
        lowerInvariant = Regex.Replace(lowerInvariant, "[^a-z0-9]+", "-");
        lowerInvariant = Regex.Replace(lowerInvariant, "-{2,}", "-");
        return lowerInvariant.Trim('-');
    }

    private async Task<string?> ResolveOrCreateSubscriberIdAsync(
        Uri baseUri,
        string apiKey,
        string email,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalizedEmail))
        {
            return null;
        }

        var cacheKey = $"{SubscriberCachePrefix}{normalizedEmail}";
        if (_memoryCache.TryGetValue(cacheKey, out string? cachedSubscriberId) &&
            !string.IsNullOrWhiteSpace(cachedSubscriberId))
        {
            return cachedSubscriberId;
        }

        var escapedEmail = Uri.EscapeDataString(normalizedEmail);
        var lookupUri = new Uri(baseUri, $"rest/v1/subscribers?select=subscriber_id&email=eq.{escapedEmail}&limit=1");

        using (var lookupRequest = CreateRequest(HttpMethod.Get, lookupUri, apiKey))
        using (var lookupResponse = await _httpClient.SendAsync(lookupRequest, cancellationToken))
        {
            if (lookupResponse.IsSuccessStatusCode)
            {
                var body = await lookupResponse.Content.ReadAsStringAsync(cancellationToken);
                var subscriberId = ReadFirstStringProperty(body, "subscriber_id");
                if (!string.IsNullOrWhiteSpace(subscriberId))
                {
                    _memoryCache.Set(cacheKey, subscriberId, SubscriberCacheDuration);
                    return subscriberId;
                }
            }
            else
            {
                var body = await lookupResponse.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "Character subscriber lookup failed. Status={StatusCode} Body={Body}",
                    (int)lookupResponse.StatusCode,
                    body);
            }
        }

        var upsertUri = new Uri(baseUri, "rest/v1/subscribers?on_conflict=email&select=subscriber_id");
        using var upsertRequest = CreateJsonRequest(
            HttpMethod.Post,
            upsertUri,
            apiKey,
            new { email = normalizedEmail },
            "resolution=merge-duplicates,return=representation");
        using var upsertResponse = await _httpClient.SendAsync(upsertRequest, cancellationToken);
        if (!upsertResponse.IsSuccessStatusCode)
        {
            var body = await upsertResponse.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "Character subscriber upsert failed. Status={StatusCode} Body={Body}",
                (int)upsertResponse.StatusCode,
                body);
            return null;
        }

        var upsertResponseBody = await upsertResponse.Content.ReadAsStringAsync(cancellationToken);
        var createdSubscriberId = ReadFirstStringProperty(upsertResponseBody, "subscriber_id");
        if (!string.IsNullOrWhiteSpace(createdSubscriberId))
        {
            _memoryCache.Set(cacheKey, createdSubscriberId, SubscriberCacheDuration);
        }

        return createdSubscriberId;
    }

    private static string[] NormalizeSlugList(IEnumerable<string>? values)
    {
        return (values ?? Array.Empty<string>())
            .Select(NormalizeOptionalSlug)
            .Where(static slug => !string.IsNullOrWhiteSpace(slug))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Cast<string>()
            .ToArray();
    }

    private static CharacterAudioClipItem[] NormalizeAudioClipList(IEnumerable<CharacterAudioClipItem>? values)
    {
        return (values ?? Array.Empty<CharacterAudioClipItem>())
            .Select(NormalizeAudioClip)
            .Where(static clip => clip is not null)
            .Cast<CharacterAudioClipItem>()
            .OrderBy(clip => clip.SortOrder)
            .ThenBy(clip => clip.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static CharacterAudioClipItem[] NormalizeAudioClipList(JsonElement? value)
    {
        if (value is null || value.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return Array.Empty<CharacterAudioClipItem>();
        }

        if (value.Value.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<CharacterAudioClipItem>();
        }

        var clips = new List<CharacterAudioClipItem>();
        foreach (var item in value.Value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var clip = NormalizeAudioClip(new CharacterAudioClipItem(
                StreamSlug: ReadJsonString(item, "stream_slug") ?? string.Empty,
                Title: ReadJsonString(item, "title") ?? string.Empty,
                AudioProvider: ReadJsonString(item, "audio_provider") ?? string.Empty,
                AudioObjectKey: ReadJsonString(item, "audio_object_key") ?? string.Empty,
                AudioContentType: ReadJsonString(item, "audio_content_type"),
                SortOrder: ReadJsonInt(item, "sort_order")));

            if (clip is not null)
            {
                clips.Add(clip);
            }
        }

        return clips
            .OrderBy(clip => clip.SortOrder)
            .ThenBy(clip => clip.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static CharacterAudioClipItem? NormalizeAudioClip(CharacterAudioClipItem clip)
    {
        var streamSlug = NormalizeOptionalSlug(clip.StreamSlug);
        var title = NormalizeOptionalText(clip.Title, 160);
        var audioObjectKey = NormalizeOptionalText(clip.AudioObjectKey, 2048);
        var audioProvider = NormalizeAudioProvider(clip.AudioProvider);

        if (string.IsNullOrWhiteSpace(streamSlug) ||
            string.IsNullOrWhiteSpace(title) ||
            string.IsNullOrWhiteSpace(audioObjectKey) ||
            string.IsNullOrWhiteSpace(audioProvider))
        {
            return null;
        }

        return new CharacterAudioClipItem(
            StreamSlug: streamSlug,
            Title: title,
            AudioProvider: audioProvider,
            AudioObjectKey: audioObjectKey,
            AudioContentType: NormalizeAudioContentType(clip.AudioContentType, audioObjectKey),
            SortOrder: Math.Clamp(clip.SortOrder, -500_000, 500_000));
    }

    private static string? NormalizeAudioProvider(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "local" => "local",
            "r2" => "r2",
            _ => null
        };
    }

    private static string? NormalizeAudioContentType(string? configuredContentType, string? audioObjectKey)
    {
        var normalizedContentType = configuredContentType?.Trim().ToLowerInvariant();
        if (normalizedContentType is "audio/mpeg" or "audio/mp3")
        {
            return "audio/mpeg";
        }

        if (normalizedContentType is "audio/mp4" or "audio/x-m4a")
        {
            return "audio/mp4";
        }

        if (normalizedContentType is "audio/wav" or "audio/x-wav" or "audio/wave")
        {
            return "audio/wav";
        }

        if (normalizedContentType is "audio/ogg")
        {
            return "audio/ogg";
        }

        var extension = Path.GetExtension(audioObjectKey ?? string.Empty).ToLowerInvariant();
        return extension switch
        {
            ".mp3" or ".mpeg" => "audio/mpeg",
            ".m4a" => "audio/mp4",
            ".wav" => "audio/wav",
            ".ogg" => "audio/ogg",
            _ => null
        };
    }

    private static string? ReadJsonString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : property.ToString();
    }

    private static int ReadJsonInt(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return 0;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var value))
        {
            return value;
        }

        if (property.ValueKind == JsonValueKind.String &&
            int.TryParse(property.GetString(), out value))
        {
            return value;
        }

        return 0;
    }

    private static string? ReadFirstStringProperty(string json, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(propertyName))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
            {
                return null;
            }

            var first = root[0];
            if (!first.TryGetProperty(propertyName, out var property))
            {
                return null;
            }

            return property.ValueKind == JsonValueKind.String
                ? property.GetString()
                : property.ToString();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static Guid? ReadFirstGuidProperty(string json, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(propertyName))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
            {
                return null;
            }

            var first = root[0];
            if (!first.TryGetProperty(propertyName, out var property))
            {
                return null;
            }

            var rawValue = property.GetString();
            return Guid.TryParse(rawValue, out var value) ? value : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private void InvalidateCharacterCache()
    {
        _memoryCache.Remove(PublishedCharactersCacheKey);
    }

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$")]
    private static partial Regex CharacterSlugRegex();

    private sealed class StoryCharacterRow
    {
        [JsonPropertyName("character_id")]
        public Guid CharacterId { get; set; }

        [JsonPropertyName("slug")]
        public string Slug { get; set; } = string.Empty;

        [JsonPropertyName("display_name")]
        public string DisplayName { get; set; } = string.Empty;

        [JsonPropertyName("unlock_rules")]
        public JsonElement? UnlockRules { get; set; }

        [JsonPropertyName("tagline")]
        public string? Tagline { get; set; }

        [JsonPropertyName("character_category")]
        public string? CharacterCategory { get; set; }

        [JsonPropertyName("species")]
        public string? Species { get; set; }

        [JsonPropertyName("habitat")]
        public string? Habitat { get; set; }

        [JsonPropertyName("catchphrase")]
        public string? Catchphrase { get; set; }

        [JsonPropertyName("favorite_thing")]
        public string? FavoriteThing { get; set; }

        [JsonPropertyName("character_trait")]
        public string? CharacterTrait { get; set; }

        [JsonPropertyName("golden_lesson")]
        public string? GoldenLesson { get; set; }

        [JsonPropertyName("core_value")]
        public string? CoreValue { get; set; }

        [JsonPropertyName("first_appearance")]
        public string? FirstAppearance { get; set; }

        [JsonPropertyName("friends")]
        public string? Friends { get; set; }

        [JsonPropertyName("reflection_question")]
        public string? ReflectionQuestion { get; set; }

        [JsonPropertyName("challenge_text")]
        public string? ChallengeText { get; set; }

        [JsonPropertyName("image_path")]
        public string? ImagePath { get; set; }

        [JsonPropertyName("mystery_image_path")]
        public string? MysteryImagePath { get; set; }

        [JsonPropertyName("image_drive_file_id")]
        public string? ImageDriveFileId { get; set; }

        [JsonPropertyName("mystery_image_drive_file_id")]
        public string? MysteryImageDriveFileId { get; set; }

        [JsonPropertyName("unlock_story_slug")]
        public string? UnlockStorySlug { get; set; }

        [JsonPropertyName("related_story_slugs")]
        public string[]? RelatedStorySlugs { get; set; }

        [JsonPropertyName("audio_clips")]
        public JsonElement? AudioClips { get; set; }

        [JsonPropertyName("unlock_threshold_seconds")]
        public int? UnlockThresholdSeconds { get; set; }

        [JsonPropertyName("sort_order")]
        public int SortOrder { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("updated_at")]
        public DateTimeOffset? UpdatedAt { get; set; }
    }

    private sealed class CharacterAudioPlayRow
    {
        [JsonPropertyName("character_id")]
        public Guid CharacterId { get; set; }

        [JsonPropertyName("character_slug")]
        public string CharacterSlug { get; set; } = string.Empty;

        [JsonPropertyName("occurred_at")]
        public DateTimeOffset OccurredAt { get; set; }
    }
}
