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
public class SupabaseUserNotificationCharacterUnlockTests
{
    private static readonly Guid CharacterId = Guid.Parse("f1f07405-3c9b-4746-8d3e-a1deb33b1395");
    private const string SubscriberId = "11111111-1111-1111-1111-111111111111";
    private const string SubscriberEmail = "listener@example.com";

    [TestMethod]
    public async Task SyncCharacterUnlockNotificationsAsync_PersistsLockedState_WhenPreviouslyTrackedAsUnlocked()
    {
        var handler = new CharacterUnlockSyncHandler
        {
            StateResponseJson =
                $$"""
                [
                  {
                    "character_id": "{{CharacterId}}",
                    "is_unlocked": true,
                    "unlock_count": 1
                  }
                ]
                """
        };

        var service = CreateService(
            handler,
            [CreateCharacter()],
            progressItems: [],
            profileListenItems: []);

        var result = await service.SyncCharacterUnlockNotificationsAsync(SubscriberEmail);

        Assert.AreEqual(0, result.CreatedCount);
        Assert.IsNull(handler.NotificationInsertPayload);
        Assert.IsNotNull(handler.StateUpsertPayload);

        var statePayload = JsonSerializer.Deserialize<JsonElement>(handler.StateUpsertPayload!);
        Assert.AreEqual(1, statePayload.GetArrayLength());
        Assert.AreEqual(CharacterId.ToString(), statePayload[0].GetProperty("character_id").GetString());
        Assert.IsFalse(statePayload[0].GetProperty("is_unlocked").GetBoolean());
        Assert.AreEqual(1, statePayload[0].GetProperty("unlock_count").GetInt32());
    }

    [TestMethod]
    public async Task SyncCharacterUnlockNotificationsAsync_CreatesNewNotification_WhenCharacterUnlocksAfterRelock()
    {
        var legacySourceKey = $"character-unlocked-{CharacterId:N}";
        var handler = new CharacterUnlockSyncHandler
        {
            StateResponseJson =
                $$"""
                [
                  {
                    "character_id": "{{CharacterId}}",
                    "is_unlocked": false,
                    "unlock_count": 1
                  }
                ]
                """,
            NotificationHistoryResponseJson =
                $$"""
                [
                  {
                    "source_key": "{{legacySourceKey}}"
                  }
                ]
                """
        };

        var service = CreateService(
            handler,
            [CreateCharacter()],
            progressItems:
            [
                new UserStoryProgressItem(
                    "diekwaaigrommel",
                    "/luister/diekwaaigrommel",
                    "luister",
                    DateTimeOffset.UtcNow,
                    301.2m,
                    3,
                    1,
                    285.832m,
                    285.858m,
                    true)
            ],
            profileListenItems: []);

        var result = await service.SyncCharacterUnlockNotificationsAsync(SubscriberEmail);

        Assert.AreEqual(1, result.CreatedCount);
        Assert.IsNotNull(handler.NotificationInsertPayload);
        Assert.IsNotNull(handler.StateUpsertPayload);

        var notificationPayload = JsonSerializer.Deserialize<JsonElement>(handler.NotificationInsertPayload!);
        Assert.AreEqual(1, notificationPayload.GetArrayLength());
        Assert.AreEqual(
            $"character-unlocked-{CharacterId:N}-2",
            notificationPayload[0].GetProperty("source_key").GetString());

        var statePayload = JsonSerializer.Deserialize<JsonElement>(handler.StateUpsertPayload!);
        Assert.AreEqual(1, statePayload.GetArrayLength());
        Assert.IsTrue(statePayload[0].GetProperty("is_unlocked").GetBoolean());
        Assert.AreEqual(2, statePayload[0].GetProperty("unlock_count").GetInt32());
    }

    [TestMethod]
    public async Task SyncCharacterUnlockNotificationsAsync_SeedsTrackedUnlockedState_FromLegacyNotificationWithoutDuplicating()
    {
        var legacySourceKey = $"character-unlocked-{CharacterId:N}";
        var handler = new CharacterUnlockSyncHandler
        {
            StateResponseJson = "[]",
            NotificationHistoryResponseJson =
                $$"""
                [
                  {
                    "source_key": "{{legacySourceKey}}"
                  }
                ]
                """
        };

        var service = CreateService(
            handler,
            [CreateCharacter()],
            progressItems:
            [
                new UserStoryProgressItem(
                    "diekwaaigrommel",
                    "/luister/diekwaaigrommel",
                    "luister",
                    DateTimeOffset.UtcNow,
                    301.2m,
                    3,
                    1,
                    285.832m,
                    285.858m,
                    true)
            ],
            profileListenItems: []);

        var result = await service.SyncCharacterUnlockNotificationsAsync(SubscriberEmail);

        Assert.AreEqual(0, result.CreatedCount);
        Assert.IsNull(handler.NotificationInsertPayload);
        Assert.IsNotNull(handler.StateUpsertPayload);

        var statePayload = JsonSerializer.Deserialize<JsonElement>(handler.StateUpsertPayload!);
        Assert.AreEqual(1, statePayload.GetArrayLength());
        Assert.IsTrue(statePayload[0].GetProperty("is_unlocked").GetBoolean());
        Assert.AreEqual(1, statePayload[0].GetProperty("unlock_count").GetInt32());
    }

    private static SupabaseUserNotificationService CreateService(
        CharacterUnlockSyncHandler handler,
        IReadOnlyList<StoryCharacterItem> characters,
        IReadOnlyList<UserStoryProgressItem> progressItems,
        IReadOnlyList<UserCharacterProfileListenItem> profileListenItems)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://example.supabase.co/")
        };
        var memoryCache = new MemoryCache(new MemoryCacheOptions());

        return new SupabaseUserNotificationService(
            httpClient,
            Options.Create(new SupabaseOptions
            {
                Url = "https://example.supabase.co/",
                ServiceRoleKey = "service-role-key"
            }),
            memoryCache,
            new StubCharacterCatalogService(characters),
            new StubCharacterTrackingService(profileListenItems),
            new StubStoryTrackingService(progressItems),
            NullLogger<SupabaseUserNotificationService>.Instance);
    }

    private static StoryCharacterItem CreateCharacter() =>
        new(
            CharacterId,
            "grommel",
            "Grommel",
            "friend",
            [
                new CharacterUnlockRuleItem(
                    CharacterUnlockEvaluator.RuleTypeStoryListenSeconds,
                    ["diekwaaigrommel"],
                    CharacterUnlockEvaluator.MatchModeAny,
                    0,
                    240)
            ],
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            "/branding/characters/grommel.png",
            null,
            "diekwaaigrommel",
            ["diekwaaigrommel"],
            [],
            240,
            1,
            DateTimeOffset.UtcNow);

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class CharacterUnlockSyncHandler : HttpMessageHandler
    {
        public string StateResponseJson { get; init; } = "[]";
        public string NotificationHistoryResponseJson { get; init; } = "[]";
        public string? NotificationInsertPayload { get; private set; }
        public string? StateUpsertPayload { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            var query = request.RequestUri?.Query ?? string.Empty;

            if (request.Method == HttpMethod.Get &&
                path.EndsWith("/rest/v1/subscribers", StringComparison.Ordinal))
            {
                return Task.FromResult(JsonResponse(
                    $$"""
                    [
                      {
                        "subscriber_id": "{{SubscriberId}}",
                        "subscriptions": [
                          { "status": "active", "next_renewal_at": null, "cancelled_at": null }
                        ]
                      }
                    ]
                    """));
            }

            if (request.Method == HttpMethod.Get &&
                path.EndsWith("/rest/v1/subscriber_character_unlock_states", StringComparison.Ordinal))
            {
                return Task.FromResult(JsonResponse(StateResponseJson));
            }

            if (request.Method == HttpMethod.Get &&
                path.EndsWith("/rest/v1/subscriber_notifications", StringComparison.Ordinal) &&
                query.Contains("select=source_key", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(JsonResponse(NotificationHistoryResponseJson));
            }

            if (request.Method == HttpMethod.Post &&
                path.EndsWith("/rest/v1/subscriber_notifications", StringComparison.Ordinal))
            {
                NotificationInsertPayload = request.Content?.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
                return Task.FromResult(JsonResponse(
                    """
                    [
                      { "notification_id": "33333333-3333-3333-3333-333333333333" }
                    ]
                    """));
            }

            if (request.Method == HttpMethod.Post &&
                path.EndsWith("/rest/v1/subscriber_character_unlock_states", StringComparison.Ordinal))
            {
                StateUpsertPayload = request.Content?.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = new StringContent("[]", Encoding.UTF8, "application/json")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private sealed class StubCharacterCatalogService(IReadOnlyList<StoryCharacterItem> characters) : ICharacterCatalogService
    {
        public Task<IReadOnlyList<StoryCharacterItem>> GetPublishedCharactersAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(characters);

        public Task<CharacterAudioClipItem?> FindPublishedAudioClipByStreamSlugAsync(
            string? streamSlug,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<CharacterAudioClipItem?>(null);
    }

    private sealed class StubCharacterTrackingService(IReadOnlyList<UserCharacterProfileListenItem> profileListenItems) : ICharacterTrackingService
    {
        public Task<bool> RecordProfileListenAsync(
            string? email,
            CharacterProfileListenTrackingRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<IReadOnlyList<UserCharacterProfileListenItem>> GetUserProfileListenStatsAsync(
            string? email,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(profileListenItems);
    }

    private sealed class StubStoryTrackingService(IReadOnlyList<UserStoryProgressItem> progressItems) : IStoryTrackingService
    {
        public Task<bool> RecordStoryViewAsync(string? email, StoryViewTrackingRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<bool> RecordStoryListenAsync(string? email, StoryListenTrackingRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<IReadOnlyList<UserStoryProgressItem>> GetUserStoryProgressAsync(string? email, CancellationToken cancellationToken = default) =>
            Task.FromResult(progressItems);
    }
}
