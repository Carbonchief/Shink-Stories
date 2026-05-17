using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shink.Services;

namespace Shink.Tests;

[TestClass]
public class SupabaseTrackingPaginationTests
{
    private const string SubscriberId = "11111111-1111-1111-1111-111111111111";
    private const string SubscriberEmail = "listener@example.com";

    [TestMethod]
    public async Task GetUserStoryProgressAsync_IncludesOlderRowsBeyondFirstPage()
    {
        var handler = new PagedTrackingHandler
        {
            StoryFirstPageJson = BuildStoryListenEventsJson(1000, "recent-story"),
            StorySecondPageJson = BuildStoryListenEventsJson(1, "unlock-story", seconds: 240m)
        };
        var service = CreateStoryTrackingService(handler);

        var progressItems = await service.GetUserStoryProgressAsync(SubscriberEmail);

        var unlockProgress = progressItems.FirstOrDefault(item => item.StorySlug == "unlock-story");
        Assert.IsNotNull(unlockProgress);
        Assert.AreEqual(240m, unlockProgress.TotalListenedSeconds);
    }

    [TestMethod]
    public async Task GetUserProfileListenStatsAsync_IncludesOlderRowsBeyondFirstPage()
    {
        var handler = new PagedTrackingHandler
        {
            CharacterFirstPageJson = BuildCharacterAudioPlaysJson(1000, "recent-character"),
            CharacterSecondPageJson = BuildCharacterAudioPlaysJson(1, "unlock-character")
        };
        var service = CreateCharacterTrackingService(handler);

        var stats = await service.GetUserProfileListenStatsAsync(SubscriberEmail);

        var unlockStats = stats.FirstOrDefault(item => item.CharacterSlug == "unlock-character");
        Assert.IsNotNull(unlockStats);
        Assert.AreEqual(1, unlockStats.ListenCount);
    }

    private static SupabaseStoryTrackingService CreateStoryTrackingService(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://example.supabase.co/")
        };

        return new SupabaseStoryTrackingService(
            httpClient,
            Options.Create(CreateSupabaseOptions()),
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<SupabaseStoryTrackingService>.Instance);
    }

    private static SupabaseCharacterService CreateCharacterTrackingService(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://example.supabase.co/")
        };

        return new SupabaseCharacterService(
            httpClient,
            Options.Create(CreateSupabaseOptions()),
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<SupabaseCharacterService>.Instance);
    }

    private static SupabaseOptions CreateSupabaseOptions() =>
        new()
        {
            Url = "https://example.supabase.co/",
            SecretKey = "secret-key",
            PublishableKey = "publishable-key"
        };

    private static string BuildStoryListenEventsJson(int count, string storySlug, decimal seconds = 1m)
    {
        var rows = Enumerable.Range(0, count)
            .Select(index => new
            {
                story_slug = storySlug,
                story_path = $"/luister/{storySlug}",
                session_id = Guid.NewGuid(),
                event_type = "progress",
                listened_seconds = seconds,
                position_seconds = seconds,
                duration_seconds = 300,
                occurred_at = DateTimeOffset.UtcNow.AddSeconds(-index),
                metadata = new { source = "luister", is_completed = false }
            });

        return JsonSerializer.Serialize(rows);
    }

    private static string BuildCharacterAudioPlaysJson(int count, string characterSlug)
    {
        var rows = Enumerable.Range(0, count)
            .Select(index => new
            {
                character_id = Guid.NewGuid(),
                character_slug = characterSlug,
                occurred_at = DateTimeOffset.UtcNow.AddSeconds(-index)
            });

        return JsonSerializer.Serialize(rows);
    }

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class PagedTrackingHandler : HttpMessageHandler
    {
        public string StoryFirstPageJson { get; init; } = "[]";
        public string StorySecondPageJson { get; init; } = "[]";
        public string CharacterFirstPageJson { get; init; } = "[]";
        public string CharacterSecondPageJson { get; init; } = "[]";

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            var query = request.RequestUri?.Query ?? string.Empty;
            var offset = ReadOffset(query);

            if (request.Method == HttpMethod.Get &&
                path.EndsWith("/rest/v1/subscribers", StringComparison.Ordinal))
            {
                return Task.FromResult(JsonResponse(
                    $$"""
                    [
                      {
                        "subscriber_id": "{{SubscriberId}}"
                      }
                    ]
                    """));
            }

            if (request.Method == HttpMethod.Get &&
                path.EndsWith("/rest/v1/story_listen_events", StringComparison.Ordinal))
            {
                return Task.FromResult(JsonResponse(offset == 0 ? StoryFirstPageJson : StorySecondPageJson));
            }

            if (request.Method == HttpMethod.Get &&
                path.EndsWith("/rest/v1/character_audio_plays", StringComparison.Ordinal))
            {
                return Task.FromResult(JsonResponse(offset == 0 ? CharacterFirstPageJson : CharacterSecondPageJson));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static int ReadOffset(string query)
        {
            var values = System.Web.HttpUtility.ParseQueryString(query);
            return int.TryParse(values["offset"], out var offset) ? offset : 0;
        }
    }
}
