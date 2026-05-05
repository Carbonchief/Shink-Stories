using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace Shink.Services;

public sealed partial class WordPressMigrationService(
    HttpClient httpClient,
    IOptions<WordPressOptions> wordPressOptions,
    IOptions<SupabaseOptions> supabaseOptions,
    PaystackCheckoutService paystackCheckoutService,
    ISubscriberAvatarStorageService subscriberAvatarStorageService,
    ILogger<WordPressMigrationService> logger) : IWordPressMigrationService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly IReadOnlyDictionary<string, string> MembershipTierMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Schink Stories Gratis"] = "gratis",
            ["Schink Stories"] = "all_stories_monthly",
            ["Schink Stories JAAR"] = "all_stories_yearly",
            ["Storie Hoekie"] = "story_corner_monthly"
        };

    private readonly HttpClient _httpClient = httpClient;
    private readonly WordPressOptions _wordPressOptions = wordPressOptions.Value;
    private readonly SupabaseOptions _supabaseOptions = supabaseOptions.Value;
    private readonly PaystackCheckoutService _paystackCheckoutService = paystackCheckoutService;
    private readonly ISubscriberAvatarStorageService _subscriberAvatarStorageService = subscriberAvatarStorageService;
    private readonly ILogger<WordPressMigrationService> _logger = logger;

    public async Task<WordPressSyncResult> SyncAsync(CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();
        if (!TryBuildSupabaseBaseUri(out var baseUri) || string.IsNullOrWhiteSpace(_supabaseOptions.ServiceRoleKey))
        {
            return new WordPressSyncResult(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, ["Supabase ServiceRoleKey or URL is not configured."]);
        }

        if (!TryValidateWordPressConfiguration(out var configurationError))
        {
            return new WordPressSyncResult(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, [configurationError!]);
        }

        try
        {
            var wordPressUsers = await LoadCanonicalWordPressUsersAsync(cancellationToken, errors);
            var membershipLevels = await LoadMembershipLevelsAsync(cancellationToken);
            var membershipPeriods = await LoadMembershipPeriodsAsync(membershipLevels, cancellationToken);
            var membershipOrders = await LoadMembershipOrdersAsync(membershipLevels, cancellationToken);
            var subscriptions = await LoadSubscriptionsAsync(membershipLevels, cancellationToken);
            var discountCodeAccessKeys = await LoadDiscountCodeAccessKeysAsync(cancellationToken);
            var discountCodes = await LoadDiscountCodesAsync(membershipLevels, cancellationToken);
            var groupDiscountCodes = await LoadGroupDiscountCodesAsync(cancellationToken);
            var groupDiscountCodesByOrderId = groupDiscountCodes
                .Where(groupCode => groupCode.OrderId > 0)
                .GroupBy(groupCode => groupCode.OrderId)
                .ToDictionary(group => group.Key, group => group.First());
            var discountCodeUses = await LoadDiscountCodeUsesAsync(membershipLevels, groupDiscountCodesByOrderId, cancellationToken);

            var subscriberRows = await FetchSubscriberRowsAsync(baseUri, cancellationToken);
            var existingSubscribersByEmail = subscriberRows
                .Where(row => !string.IsNullOrWhiteSpace(row.Email))
                .GroupBy(row => row.Email!.Trim().ToLowerInvariant(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

            var uploadedAvatarCount = 0;
            var avatarErrors = new ConcurrentQueue<string>();
            var avatarCandidates = wordPressUsers
                .Where(user => !string.IsNullOrWhiteSpace(user.AvatarSourceUrl))
                .ToList();
            await Parallel.ForEachAsync(
                avatarCandidates,
                new ParallelOptions
                {
                    CancellationToken = cancellationToken,
                    MaxDegreeOfParallelism = 8
                },
                async (user, parallelCancellationToken) =>
                {
                    try
                    {
                        var existingSubscriber = existingSubscribersByEmail.GetValueOrDefault(user.Email);
                        var uploadedAvatar = await UploadAvatarAsync(user, existingSubscriber, parallelCancellationToken);
                        if (uploadedAvatar is null)
                        {
                            return;
                        }

                        user.ProfileImageUrl = uploadedAvatar.PublicUrl;
                        user.ProfileImageObjectKey = uploadedAvatar.ObjectKey;
                        user.ProfileImageContentType = uploadedAvatar.ContentType;
                        Interlocked.Increment(ref uploadedAvatarCount);
                    }
                    catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or InvalidOperationException)
                    {
                        if (exception is HttpRequestException httpRequestException &&
                            httpRequestException.StatusCode == HttpStatusCode.NotFound)
                        {
                            _logger.LogWarning(exception, "WordPress avatar source missing for {Email}.", user.Email);
                            return;
                        }

                        avatarErrors.Enqueue($"Avatar import failed for {user.Email}: {exception.Message}");
                        _logger.LogWarning(exception, "WordPress avatar import failed for {Email}.", user.Email);
                    }
                });
            errors.AddRange(avatarErrors);

            var importedUsers = 0;
            foreach (var batch in Batch(wordPressUsers.Select(BuildWordPressUserImportRow), 200))
            {
                importedUsers += await ImportWordPressUsersBatchAsync(baseUri, batch, cancellationToken);
            }

            var subscriberPayloads = wordPressUsers
                .Select(BuildSubscriberUpsertRow)
                .ToList();
            var subscriberIdsByEmail = await UpsertSubscribersAsync(baseUri, subscriberPayloads, cancellationToken);
            var upsertedSubscribers = subscriberIdsByEmail.Count;

            var importedMembershipPeriods = 0;
            foreach (var batch in Batch(membershipPeriods.Select(BuildMembershipPeriodImportRow), 250))
            {
                importedMembershipPeriods += await ImportWordPressMembershipPeriodsBatchAsync(baseUri, batch, cancellationToken);
            }

            var importedMembershipOrders = 0;
            foreach (var batch in Batch(membershipOrders.Select(BuildMembershipOrderImportRow), 250))
            {
                importedMembershipOrders += await ImportWordPressMembershipOrdersBatchAsync(baseUri, batch, cancellationToken);
            }

            var importedSubscriptions = 0;
            foreach (var batch in Batch(subscriptions.Select(BuildSubscriptionImportRow), 250))
            {
                importedSubscriptions += await ImportWordPressSubscriptionsBatchAsync(baseUri, batch, cancellationToken);
            }

            var importedDiscountCodes = 0;
            foreach (var batch in Batch(BuildDiscountCodeImportRows(discountCodes, groupDiscountCodes), 250))
            {
                importedDiscountCodes += await ImportWordPressSubscriptionDiscountCodesBatchAsync(baseUri, batch, cancellationToken);
            }

            var importedDiscountCodeTiers = 0;
            foreach (var batch in Batch(BuildDiscountCodeTierImportRows(discountCodes, groupDiscountCodes), 500))
            {
                importedDiscountCodeTiers += await ImportWordPressSubscriptionDiscountCodeTiersBatchAsync(baseUri, batch, cancellationToken);
            }

            var importedDiscountCodeRedemptions = 0;
            foreach (var batch in Batch(BuildDiscountCodeRedemptionImportRows(discountCodeUses), 500))
            {
                importedDiscountCodeRedemptions += await ImportWordPressSubscriptionDiscountCodeRedemptionsBatchAsync(baseUri, batch, cancellationToken);
            }

            var activeEntitlements = BuildCurrentEntitlements(wordPressUsers, membershipPeriods, membershipOrders, subscriptions, discountCodeAccessKeys);
            activeEntitlements = await EnrichPaystackEntitlementsAsync(activeEntitlements, cancellationToken);
            activeEntitlements = await FilterNativeSubscriptionDuplicatesAsync(
                baseUri,
                activeEntitlements,
                subscriberIdsByEmail,
                cancellationToken);
            var upsertedCurrentEntitlements = await UpsertCurrentEntitlementsAsync(
                baseUri,
                activeEntitlements,
                subscriberIdsByEmail,
                cancellationToken);
            var reactivatedCurrentEntitlements = await ReactivateCancelledImportedEntitlementsAsync(
                baseUri,
                activeEntitlements,
                cancellationToken);
            var cancelledCurrentEntitlements = await CancelStaleImportedEntitlementsAsync(
                baseUri,
                activeEntitlements,
                cancellationToken);
            var reconciledNativePaystackTokens = await ReconcileNativePaystackRetryTokensAsync(baseUri, cancellationToken);
            if (reactivatedCurrentEntitlements > 0)
            {
                _logger.LogInformation(
                    "Reactivated {Count} imported WordPress runtime subscriptions that should still be active.",
                    reactivatedCurrentEntitlements);
            }
            if (reconciledNativePaystackTokens > 0)
            {
                _logger.LogInformation(
                    "Reconciled Paystack retry tokens onto {Count} native Shink subscription rows from imported WordPress data.",
                    reconciledNativePaystackTokens);
            }

            var backfilledAuthSubscribers = await BackfillAuthUsersWithoutSubscribersAsync(
                baseUri,
                subscriberIdsByEmail,
                cancellationToken);

            return new WordPressSyncResult(
                ImportedUsers: importedUsers,
                UpsertedSubscribers: upsertedSubscribers,
                UploadedAvatars: uploadedAvatarCount,
                UpsertedMembershipPeriods: importedMembershipPeriods,
                UpsertedMembershipOrders: importedMembershipOrders,
                UpsertedSubscriptions: importedSubscriptions,
                ImportedDiscountCodes: importedDiscountCodes,
                ImportedDiscountCodeTiers: importedDiscountCodeTiers,
                ImportedDiscountCodeRedemptions: importedDiscountCodeRedemptions,
                UpsertedCurrentEntitlements: upsertedCurrentEntitlements,
                CancelledCurrentEntitlements: cancelledCurrentEntitlements,
                BackfilledAuthSubscribers: backfilledAuthSubscribers,
                Errors: errors);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or MySqlException or JsonException)
        {
            _logger.LogError(exception, "WordPress sync failed unexpectedly.");
            errors.Add(exception.Message);
            return new WordPressSyncResult(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, errors);
        }
    }

    public async Task<bool> SyncImportedUserProfileAndAccessAsync(string? email, CancellationToken cancellationToken = default)
    {
        var importedUser = await GetImportedUserByEmailAsync(email, cancellationToken);
        if (importedUser is null || !TryBuildSupabaseBaseUri(out var baseUri))
        {
            return false;
        }

        var subscriberIds = await UpsertSubscribersAsync(
            baseUri,
            [new SubscriberUpsertRow(
                Email: importedUser.Email,
                FirstName: importedUser.FirstName,
                LastName: importedUser.LastName,
                DisplayName: importedUser.DisplayName,
                MobileNumber: importedUser.MobileNumber,
                LastLoginAt: importedUser.LastLoginAt,
                ProfileImageUrl: importedUser.ProfileImageUrl,
                ProfileImageObjectKey: importedUser.ProfileImageObjectKey,
                ProfileImageContentType: importedUser.ProfileImageContentType)],
            cancellationToken);
        if (!subscriberIds.TryGetValue(importedUser.Email, out var subscriberId) || string.IsNullOrWhiteSpace(subscriberId))
        {
            return false;
        }

        var currentEntitlements = await FetchImportedCurrentEntitlementsAsync(baseUri, importedUser.Email, cancellationToken);
        currentEntitlements = await EnrichPaystackEntitlementsAsync(currentEntitlements, cancellationToken);
        currentEntitlements = await FilterNativeSubscriptionDuplicatesAsync(
            baseUri,
            currentEntitlements,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [importedUser.Email] = subscriberId
            },
            cancellationToken);
        await UpsertCurrentEntitlementsAsync(
            baseUri,
            currentEntitlements.Select(entitlement => entitlement with { Email = importedUser.Email }).ToList(),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [importedUser.Email] = subscriberId
            },
            cancellationToken);
        await ReactivateCancelledImportedEntitlementsAsync(
            baseUri,
            currentEntitlements,
            cancellationToken);
        await CancelStaleImportedEntitlementsForSubscriberAsync(
            baseUri,
            subscriberId,
            currentEntitlements,
            cancellationToken);
        await ReconcileNativePaystackRetryTokensAsync(baseUri, cancellationToken);
        return true;
    }

    public async Task<WordPressImportedUser?> GetImportedUserByEmailAsync(string? email, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = NormalizeEmail(email);
        if (normalizedEmail is null || !TryBuildSupabaseBaseUri(out var baseUri))
        {
            return null;
        }

        using var request = CreateSupabaseJsonRequest(
            HttpMethod.Post,
            new Uri(baseUri, "rest/v1/rpc/get_wordpress_user_for_auth"),
            new { p_normalized_email = normalizedEmail });
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "WordPress imported user lookup failed. email={Email} Status={StatusCode} Body={Body}",
                normalizedEmail,
                (int)response.StatusCode,
                body);
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var rows = await JsonSerializer.DeserializeAsync<List<WordPressImportedUserRow>>(stream, cancellationToken: cancellationToken) ?? [];
        var row = rows.FirstOrDefault();
        if (row is null || row.WordPressUserId <= 0 || string.IsNullOrWhiteSpace(row.NormalizedEmail))
        {
            return null;
        }

        return new WordPressImportedUser(
            WordPressUserId: row.WordPressUserId,
            Email: row.NormalizedEmail.Trim().ToLowerInvariant(),
            PasswordHash: NullIfWhiteSpace(row.PasswordHash),
            PasswordHashFormat: NullIfWhiteSpace(row.PasswordHashFormat),
            FirstName: NullIfWhiteSpace(row.FirstName),
            LastName: NullIfWhiteSpace(row.LastName),
            DisplayName: NullIfWhiteSpace(row.DisplayName),
            MobileNumber: NullIfWhiteSpace(row.MobileNumber),
            LastLoginAt: row.LastLoginAt,
            ProfileImageUrl: NullIfWhiteSpace(row.ProfileImageUrl),
            ProfileImageObjectKey: NullIfWhiteSpace(row.ProfileImageObjectKey),
            ProfileImageContentType: NullIfWhiteSpace(row.ProfileImageContentType));
    }

    public async Task MarkPasswordMigratedAsync(long wordpressUserId, CancellationToken cancellationToken = default)
    {
        if (wordpressUserId <= 0 || !TryBuildSupabaseBaseUri(out var baseUri))
        {
            return;
        }

        using var request = CreateSupabaseJsonRequest(
            HttpMethod.Post,
            new Uri(baseUri, "rest/v1/rpc/mark_wordpress_user_password_migrated"),
            new { p_wp_user_id = wordpressUserId });
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "WordPress password migration marker failed. wp_user_id={WordPressUserId} Status={StatusCode} Body={Body}",
                wordpressUserId,
                (int)response.StatusCode,
                body);
        }
    }
    private async Task<List<CanonicalWordPressUser>> LoadCanonicalWordPressUsersAsync(
        CancellationToken cancellationToken,
        List<string> errors)
    {
        await using var connection = await OpenWordPressConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            select
                u.ID,
                u.user_login,
                u.user_pass,
                u.user_nicename,
                u.user_email,
                u.user_registered,
                u.display_name,
                first_name.meta_value as first_name,
                last_name.meta_value as last_name,
                mobile.meta_value as mobile,
                billing_phone.meta_value as billing_phone,
                pmpro_logins.meta_value as pmpro_logins,
                session_tokens.meta_value as session_tokens,
                social_users.login_date as social_login_date,
                avatar.meta_value as avatar_attachment_id,
                attachment.guid as avatar_attachment_url,
                basic_avatar.meta_value as basic_avatar_value
            from {Table("users")} u
            left join {Table("usermeta")} first_name
                on first_name.user_id = u.ID and first_name.meta_key = 'first_name'
            left join {Table("usermeta")} last_name
                on last_name.user_id = u.ID and last_name.meta_key = 'last_name'
            left join {Table("usermeta")} mobile
                on mobile.user_id = u.ID and mobile.meta_key = 'mobile'
            left join {Table("usermeta")} billing_phone
                on billing_phone.user_id = u.ID and billing_phone.meta_key = 'billing_phone'
            left join {Table("usermeta")} pmpro_logins
                on pmpro_logins.user_id = u.ID and pmpro_logins.meta_key = 'pmpro_logins'
            left join {Table("usermeta")} session_tokens
                on session_tokens.user_id = u.ID and session_tokens.meta_key = 'session_tokens'
            left join (
                select ID, max(login_date) as login_date
                from {Table("social_users")}
                group by ID
            ) social_users
                on social_users.ID = u.ID
            left join {Table("usermeta")} avatar
                on avatar.user_id = u.ID and avatar.meta_key = 'wplx_user_avatar'
            left join {Table("posts")} attachment
                on attachment.ID = cast(avatar.meta_value as unsigned) and attachment.post_type = 'attachment'
            left join {Table("usermeta")} basic_avatar
                on basic_avatar.user_id = u.ID and basic_avatar.meta_key = 'basic_user_avatar'
            where nullif(trim(u.user_email), '') is not null
            order by u.ID asc;
            """;

        var rows = new List<CanonicalWordPressUser>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var normalizedEmail = NormalizeEmail(reader["user_email"]?.ToString());
            if (normalizedEmail is null)
            {
                continue;
            }

            rows.Add(new CanonicalWordPressUser
            {
                WordPressUserId = ReadInt64(reader, "ID"),
                Email = normalizedEmail,
                UserLogin = ReadTrimmedString(reader, "user_login") ?? string.Empty,
                PasswordHash = ReadTrimmedString(reader, "user_pass"),
                UserNicename = ReadTrimmedString(reader, "user_nicename") ?? string.Empty,
                DisplayName = ReadTrimmedString(reader, "display_name"),
                FirstName = ReadTrimmedString(reader, "first_name"),
                LastName = ReadTrimmedString(reader, "last_name"),
                MobileNumber = ReadTrimmedString(reader, "mobile") ?? ReadTrimmedString(reader, "billing_phone"),
                LastLoginAt = WordPressLastLoginResolver.Resolve(
                    ReadNullableDateTime(reader, "social_login_date"),
                    ReadTrimmedString(reader, "pmpro_logins"),
                    ReadTrimmedString(reader, "session_tokens")),
                UserRegistered = ReadNullableDateTime(reader, "user_registered"),
                AvatarSourceUrl = DetermineAvatarSourceUrl(
                    ReadTrimmedString(reader, "avatar_attachment_url"),
                    ReadTrimmedString(reader, "basic_avatar_value")),
                AvatarSourceAttachmentId = ReadNullableInt64FromString(reader, "avatar_attachment_id"),
                AvatarSourceMetaKey = DetermineAvatarSourceMetaKey(
                    ReadTrimmedString(reader, "avatar_attachment_url"),
                    ReadTrimmedString(reader, "basic_avatar_value"))
            });
        }

        var passwordVerifier = new WordPressPasswordVerifier();
        var duplicates = rows
            .GroupBy(row => row.Email, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .ToList();

        foreach (var duplicate in duplicates)
        {
            var duplicateIds = duplicate.Select(user => user.WordPressUserId.ToString(CultureInfo.InvariantCulture));
            var message = $"Duplicate WordPress email {duplicate.Key} detected for user IDs: {string.Join(", ", duplicateIds)}.";
            _logger.LogWarning(message);
        }

        return rows
            .GroupBy(row => row.Email, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var canonical = group.OrderBy(row => row.WordPressUserId).First();
                canonical.HasDuplicateEmail = group.Count() > 1;
                canonical.PasswordHashFormat = passwordVerifier.DetectFormat(canonical.PasswordHash);
                return canonical;
            })
            .OrderBy(row => row.WordPressUserId)
            .ToList();
    }

    private async Task<Dictionary<int, string>> LoadMembershipLevelsAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenWordPressConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            select id, name
            from {Table("pmpro_membership_levels")};
            """;

        var levels = new Dictionary<int, string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var membershipId = ReadInt32(reader, "id");
            var membershipName = ReadTrimmedString(reader, "name");
            if (membershipId <= 0 || string.IsNullOrWhiteSpace(membershipName))
            {
                continue;
            }

            levels[membershipId] = membershipName;
        }

        return levels;
    }

    private async Task<List<WordPressMembershipPeriod>> LoadMembershipPeriodsAsync(
        IReadOnlyDictionary<int, string> membershipLevels,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenWordPressConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            select
                mu.id,
                mu.user_id,
                lower(trim(u.user_email)) as normalized_email,
                mu.membership_id,
                mu.code_id,
                mu.initial_payment,
                mu.billing_amount,
                mu.cycle_number,
                mu.cycle_period,
                mu.billing_limit,
                mu.trial_amount,
                mu.trial_limit,
                mu.status,
                mu.startdate,
                mu.enddate,
                mu.modified
            from {Table("pmpro_memberships_users")} mu
            inner join {Table("users")} u on u.ID = mu.user_id;
            """;

        var rows = new List<WordPressMembershipPeriod>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var membershipId = ReadInt32(reader, "membership_id");
            membershipLevels.TryGetValue(membershipId, out var membershipName);
            rows.Add(new WordPressMembershipPeriod
            {
                MembershipPeriodId = ReadInt64(reader, "id"),
                WordPressUserId = ReadInt64(reader, "user_id"),
                Email = ReadTrimmedString(reader, "normalized_email"),
                MembershipLevelId = membershipId,
                MembershipLevelName = membershipName,
                TierCode = ResolveTierCode(membershipName),
                CodeId = ReadNullableInt64(reader, "code_id"),
                Status = ReadTrimmedString(reader, "status"),
                StartDate = ReadNullableDateTime(reader, "startdate"),
                EndDate = ReadNullableDateTime(reader, "enddate"),
                ModifiedAt = ReadNullableDateTime(reader, "modified"),
                InitialPayment = ReadNullableDecimal(reader, "initial_payment"),
                BillingAmount = ReadNullableDecimal(reader, "billing_amount"),
                CycleNumber = ReadNullableInt32(reader, "cycle_number"),
                CyclePeriod = ReadTrimmedString(reader, "cycle_period"),
                BillingLimit = ReadNullableInt32(reader, "billing_limit"),
                TrialAmount = ReadNullableDecimal(reader, "trial_amount"),
                TrialLimit = ReadNullableInt32(reader, "trial_limit")
            });
        }

        return rows;
    }

    private async Task<List<WordPressMembershipOrder>> LoadMembershipOrdersAsync(
        IReadOnlyDictionary<int, string> membershipLevels,
        CancellationToken cancellationToken)
    {
        var orderMeta = await LoadOrderMetaAsync(cancellationToken);

        await using var connection = await OpenWordPressConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            select
                o.id,
                o.code,
                o.session_id,
                o.user_id,
                lower(trim(u.user_email)) as normalized_email,
                o.membership_id,
                o.billing_name,
                o.billing_phone,
                o.billing_country,
                o.subtotal,
                o.tax,
                o.couponamount,
                o.total,
                o.payment_type,
                o.status,
                o.gateway,
                o.gateway_environment,
                o.payment_transaction_id,
                o.subscription_transaction_id,
                o.timestamp
            from {Table("pmpro_membership_orders")} o
            inner join {Table("users")} u on u.ID = o.user_id;
            """;

        var rows = new List<WordPressMembershipOrder>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var membershipId = ReadInt32(reader, "membership_id");
            membershipLevels.TryGetValue(membershipId, out var membershipName);
            var orderId = ReadInt64(reader, "id");
            rows.Add(new WordPressMembershipOrder
            {
                OrderId = orderId,
                WordPressUserId = ReadInt64(reader, "user_id"),
                Email = ReadTrimmedString(reader, "normalized_email"),
                MembershipLevelId = membershipId,
                MembershipLevelName = membershipName,
                TierCode = ResolveTierCode(membershipName),
                Code = ReadTrimmedString(reader, "code"),
                SessionId = ReadTrimmedString(reader, "session_id"),
                Status = ReadTrimmedString(reader, "status"),
                Gateway = ReadTrimmedString(reader, "gateway"),
                GatewayEnvironment = ReadTrimmedString(reader, "gateway_environment"),
                PaymentType = ReadTrimmedString(reader, "payment_type"),
                PaymentTransactionId = ReadTrimmedString(reader, "payment_transaction_id"),
                SubscriptionTransactionId = ReadTrimmedString(reader, "subscription_transaction_id"),
                BillingName = ReadTrimmedString(reader, "billing_name"),
                BillingPhone = ReadTrimmedString(reader, "billing_phone"),
                BillingCountry = ReadTrimmedString(reader, "billing_country"),
                Subtotal = ReadTrimmedString(reader, "subtotal"),
                Tax = ReadTrimmedString(reader, "tax"),
                CouponAmount = ReadTrimmedString(reader, "couponamount"),
                Total = ReadTrimmedString(reader, "total"),
                OrderTimestamp = ReadNullableDateTime(reader, "timestamp"),
                RawMeta = orderMeta.GetValueOrDefault(orderId) ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            });
        }

        return rows;
    }

    private async Task<List<WordPressSubscription>> LoadSubscriptionsAsync(
        IReadOnlyDictionary<int, string> membershipLevels,
        CancellationToken cancellationToken)
    {
        var subscriptionMeta = await LoadSubscriptionMetaAsync(cancellationToken);

        await using var connection = await OpenWordPressConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            select
                s.id,
                s.user_id,
                lower(trim(u.user_email)) as normalized_email,
                s.membership_level_id,
                s.gateway,
                s.gateway_environment,
                s.subscription_transaction_id,
                s.status,
                s.startdate,
                s.enddate,
                s.next_payment_date,
                s.modified,
                s.billing_amount,
                s.cycle_number,
                s.cycle_period,
                s.billing_limit,
                s.trial_amount,
                s.trial_limit
            from {Table("pmpro_subscriptions")} s
            inner join {Table("users")} u on u.ID = s.user_id;
            """;

        var rows = new List<WordPressSubscription>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var membershipId = ReadInt32(reader, "membership_level_id");
            membershipLevels.TryGetValue(membershipId, out var membershipName);
            var subscriptionId = ReadInt64(reader, "id");
            rows.Add(new WordPressSubscription
            {
                SubscriptionId = subscriptionId,
                WordPressUserId = ReadInt64(reader, "user_id"),
                Email = ReadTrimmedString(reader, "normalized_email"),
                MembershipLevelId = membershipId,
                MembershipLevelName = membershipName,
                TierCode = ResolveTierCode(membershipName),
                Gateway = ReadTrimmedString(reader, "gateway"),
                GatewayEnvironment = ReadTrimmedString(reader, "gateway_environment"),
                SubscriptionTransactionId = ReadTrimmedString(reader, "subscription_transaction_id"),
                Status = ReadTrimmedString(reader, "status"),
                StartDate = ReadNullableDateTime(reader, "startdate"),
                EndDate = ReadNullableDateTime(reader, "enddate"),
                NextPaymentDate = ReadNullableDateTime(reader, "next_payment_date"),
                ModifiedAt = ReadNullableDateTime(reader, "modified"),
                BillingAmount = ReadNullableDecimal(reader, "billing_amount"),
                CycleNumber = ReadNullableInt32(reader, "cycle_number"),
                CyclePeriod = ReadTrimmedString(reader, "cycle_period"),
                BillingLimit = ReadNullableInt32(reader, "billing_limit"),
                TrialAmount = ReadNullableDecimal(reader, "trial_amount"),
                TrialLimit = ReadNullableInt32(reader, "trial_limit"),
                RawMeta = subscriptionMeta.GetValueOrDefault(subscriptionId) ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            });
        }

        return rows;
    }

    private async Task<HashSet<string>> LoadDiscountCodeAccessKeysAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenWordPressConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            select distinct
                dcu.user_id,
                o.membership_id
            from {Table("pmpro_discount_codes_uses")} dcu
            inner join {Table("pmpro_membership_orders")} o on o.id = dcu.order_id
            where dcu.user_id is not null
              and o.membership_id is not null;
            """;

        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var wordPressUserId = ReadInt64(reader, "user_id");
            var membershipLevelId = ReadInt32(reader, "membership_id");
            if (wordPressUserId <= 0 || membershipLevelId <= 0)
            {
                continue;
            }

            keys.Add(BuildUserLevelKey(wordPressUserId, membershipLevelId));
        }

        return keys;
    }

    private async Task<Dictionary<long, Dictionary<string, string>>> LoadOrderMetaAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenWordPressConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            select pmpro_membership_order_id, meta_key, meta_value
            from {Table("pmpro_membership_ordermeta")};
            """;

        var result = new Dictionary<long, Dictionary<string, string>>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var orderId = ReadInt64(reader, "pmpro_membership_order_id");
            var metaKey = ReadTrimmedString(reader, "meta_key");
            if (orderId <= 0 || string.IsNullOrWhiteSpace(metaKey))
            {
                continue;
            }

            if (!result.TryGetValue(orderId, out var dictionary))
            {
                dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                result[orderId] = dictionary;
            }

            dictionary[metaKey] = ReadTrimmedString(reader, "meta_value") ?? string.Empty;
        }

        return result;
    }

    private async Task<Dictionary<long, Dictionary<string, string>>> LoadSubscriptionMetaAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenWordPressConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            select pmpro_subscription_id, meta_key, meta_value
            from {Table("pmpro_subscriptionmeta")};
            """;

        var result = new Dictionary<long, Dictionary<string, string>>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var subscriptionId = ReadInt64(reader, "pmpro_subscription_id");
            var metaKey = ReadTrimmedString(reader, "meta_key");
            if (subscriptionId <= 0 || string.IsNullOrWhiteSpace(metaKey))
            {
                continue;
            }

            if (!result.TryGetValue(subscriptionId, out var dictionary))
            {
                dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                result[subscriptionId] = dictionary;
            }

            dictionary[metaKey] = ReadTrimmedString(reader, "meta_value") ?? string.Empty;
        }

        return result;
    }

    private async Task<UploadedSubscriberAvatar?> UploadAvatarAsync(
        CanonicalWordPressUser user,
        SubscriberProfileRow? existingSubscriber,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(user.AvatarSourceUrl))
        {
            return null;
        }

        using var upstreamRequest = new HttpRequestMessage(HttpMethod.Get, user.AvatarSourceUrl);
        upstreamRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("image/*"));

        using var upstreamResponse = await _httpClient.SendAsync(
            upstreamRequest,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        upstreamResponse.EnsureSuccessStatusCode();

        var contentType = upstreamResponse.Content.Headers.ContentType?.MediaType;
        var fileName = TryGetFileNameFromUrl(user.AvatarSourceUrl, contentType);
        await using var stream = await upstreamResponse.Content.ReadAsStreamAsync(cancellationToken);
        var uploadedAvatar = await _subscriberAvatarStorageService.UploadAvatarAsync(
            user.Email,
            fileName,
            contentType,
            stream,
            cancellationToken);

        if (!string.IsNullOrWhiteSpace(existingSubscriber?.ProfileImageObjectKey) &&
            !string.Equals(existingSubscriber.ProfileImageObjectKey.Trim(), uploadedAvatar.ObjectKey, StringComparison.Ordinal))
        {
            await _subscriberAvatarStorageService.DeleteObjectIfExistsAsync(existingSubscriber.ProfileImageObjectKey.Trim(), cancellationToken);
        }

        return uploadedAvatar;
    }

    private async Task<int> ImportWordPressUsersBatchAsync(Uri baseUri, IReadOnlyList<WordPressUserImportRow> batch, CancellationToken cancellationToken)
    {
        using var request = CreateSupabaseJsonRequest(
            HttpMethod.Post,
            new Uri(baseUri, "rest/v1/rpc/import_wordpress_users"),
            new { payload = batch });
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        return await ReadRpcCountResponseAsync(response, "WordPress user import", cancellationToken);
    }

    private async Task<int> ImportWordPressMembershipPeriodsBatchAsync(Uri baseUri, IReadOnlyList<MembershipPeriodImportRow> batch, CancellationToken cancellationToken)
    {
        using var request = CreateSupabaseJsonRequest(
            HttpMethod.Post,
            new Uri(baseUri, "rest/v1/rpc/import_wordpress_membership_periods"),
            new { payload = batch });
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        return await ReadRpcCountResponseAsync(response, "WordPress membership period import", cancellationToken);
    }

    private async Task<int> ImportWordPressMembershipOrdersBatchAsync(Uri baseUri, IReadOnlyList<MembershipOrderImportRow> batch, CancellationToken cancellationToken)
    {
        using var request = CreateSupabaseJsonRequest(
            HttpMethod.Post,
            new Uri(baseUri, "rest/v1/rpc/import_wordpress_membership_orders"),
            new { payload = batch });
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        return await ReadRpcCountResponseAsync(response, "WordPress membership order import", cancellationToken);
    }

    private async Task<int> ImportWordPressSubscriptionsBatchAsync(Uri baseUri, IReadOnlyList<SubscriptionImportRow> batch, CancellationToken cancellationToken)
    {
        using var request = CreateSupabaseJsonRequest(
            HttpMethod.Post,
            new Uri(baseUri, "rest/v1/rpc/import_wordpress_subscriptions"),
            new { payload = batch });
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        return await ReadRpcCountResponseAsync(response, "WordPress subscription import", cancellationToken);
    }

    private async Task<Dictionary<string, string>> UpsertSubscribersAsync(
        Uri baseUri,
        IReadOnlyList<SubscriberUpsertRow> payload,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var batch in Batch(payload, 250))
        {
            using var request = CreateSupabaseJsonRequest(
                HttpMethod.Post,
                new Uri(baseUri, "rest/v1/rpc/import_wordpress_subscribers"),
                new { payload = batch });
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "Supabase subscriber bulk upsert failed. Status={StatusCode} Body={Body}",
                    (int)response.StatusCode,
                    body);
                continue;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var rows = await JsonSerializer.DeserializeAsync<List<SubscriberIdLookupRow>>(stream, cancellationToken: cancellationToken) ?? [];
            foreach (var row in rows)
            {
                if (string.IsNullOrWhiteSpace(row.Email) || string.IsNullOrWhiteSpace(row.SubscriberId))
                {
                    continue;
                }

                result[row.Email.Trim().ToLowerInvariant()] = row.SubscriberId.Trim();
            }
        }

        return result;
    }

    private async Task<int> UpsertCurrentEntitlementsAsync(
        Uri baseUri,
        IReadOnlyList<CurrentEntitlement> entitlements,
        IReadOnlyDictionary<string, string> subscriberIdsByEmail,
        CancellationToken cancellationToken)
    {
        var payload = entitlements
            .Where(entitlement => !string.IsNullOrWhiteSpace(entitlement.Email))
            .Where(entitlement => subscriberIdsByEmail.ContainsKey(entitlement.Email))
            .Select(entitlement => new
            {
                subscriber_id = subscriberIdsByEmail[entitlement.Email],
                tier_code = entitlement.TierCode,
                provider = entitlement.Provider,
                provider_payment_id = entitlement.ProviderPaymentId,
                provider_transaction_id = entitlement.ProviderTransactionId,
                provider_token = entitlement.ProviderToken,
                provider_email_token = entitlement.ProviderEmailToken,
                status = "active",
                subscribed_at = entitlement.SubscribedAt?.UtcDateTime ?? DateTime.UtcNow,
                next_renewal_at = entitlement.NextRenewalAt?.UtcDateTime,
                cancelled_at = (DateTime?)null,
                source_system = "wordpress_pmpro"
            })
            .ToList();
        if (payload.Count == 0)
        {
            return 0;
        }

        var upsertedCount = 0;
        foreach (var batch in Batch(payload, 250))
        {
            using var request = CreateSupabaseJsonRequest(
                HttpMethod.Post,
                new Uri(baseUri, "rest/v1/rpc/import_wordpress_current_subscriptions"),
                new { payload = batch });
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "Supabase imported subscription upsert failed. Status={StatusCode} Body={Body}",
                    (int)response.StatusCode,
                    body);
                continue;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var rows = await JsonSerializer.DeserializeAsync<List<SubscriptionIdLookupRow>>(stream, cancellationToken: cancellationToken) ?? [];
            upsertedCount += rows.Count;
        }

        return upsertedCount;
    }

    private async Task<int> CancelStaleImportedEntitlementsAsync(
        Uri baseUri,
        IReadOnlyList<CurrentEntitlement> entitlements,
        CancellationToken cancellationToken)
    {
        var desiredKeys = entitlements
            .Select(entitlement => BuildProviderPaymentKey(entitlement.Provider, entitlement.ProviderPaymentId))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var existingImportedRows = await FetchCurrentImportedSubscriptionRowsAsync(baseUri, cancellationToken);
        var staleRows = existingImportedRows
            .Where(row => !desiredKeys.Contains(BuildProviderPaymentKey(row.Provider, row.ProviderPaymentId)))
            .ToList();

        var cancelled = 0;
        foreach (var staleRow in staleRows)
        {
            cancelled += await CancelImportedSubscriptionAsync(baseUri, staleRow.Provider, staleRow.ProviderPaymentId, cancellationToken) ? 1 : 0;
        }

        return cancelled;
    }

    private async Task CancelStaleImportedEntitlementsForSubscriberAsync(
        Uri baseUri,
        string subscriberId,
        IReadOnlyList<CurrentEntitlement> currentEntitlements,
        CancellationToken cancellationToken)
    {
        var desiredKeys = currentEntitlements
            .Select(entitlement => BuildProviderPaymentKey(entitlement.Provider, entitlement.ProviderPaymentId))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var escapedSubscriberId = Uri.EscapeDataString(subscriberId);
        var uri = new Uri(
            baseUri,
            $"rest/v1/subscriptions?select=provider,provider_payment_id&subscriber_id=eq.{escapedSubscriberId}&source_system=eq.wordpress_pmpro&status=eq.active&limit=200");
        using var request = CreateSupabaseRequest(HttpMethod.Get, uri);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var rows = await JsonSerializer.DeserializeAsync<List<ImportedSubscriptionLookupRow>>(stream, cancellationToken: cancellationToken) ?? [];
        foreach (var row in rows)
        {
            if (!desiredKeys.Contains(BuildProviderPaymentKey(row.Provider, row.ProviderPaymentId)))
            {
                await CancelImportedSubscriptionAsync(baseUri, row.Provider, row.ProviderPaymentId, cancellationToken);
            }
        }
    }

    private async Task<bool> CancelImportedSubscriptionAsync(
        Uri baseUri,
        string? provider,
        string? providerPaymentId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(providerPaymentId))
        {
            return false;
        }

        var filterProvider = Uri.EscapeDataString(provider.Trim().ToLowerInvariant());
        var filterPaymentId = Uri.EscapeDataString(providerPaymentId.Trim());
        var uri = new Uri(
            baseUri,
            $"rest/v1/subscriptions?provider=eq.{filterProvider}&provider_payment_id=eq.{filterPaymentId}&source_system=eq.wordpress_pmpro");
        using var request = CreateSupabaseJsonRequest(
            new HttpMethod("PATCH"),
            uri,
            new
            {
                status = "cancelled",
                cancelled_at = DateTime.UtcNow,
                next_renewal_at = (DateTime?)null
            },
            "return=minimal");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return true;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        _logger.LogWarning(
            "Supabase imported subscription cancel failed. provider={Provider} payment_id={ProviderPaymentId} Status={StatusCode} Body={Body}",
            provider,
            providerPaymentId,
            (int)response.StatusCode,
            body);
        return false;
    }

    private async Task<List<ImportedSubscriptionLookupRow>> FetchCurrentImportedSubscriptionRowsAsync(Uri baseUri, CancellationToken cancellationToken)
    {
        var uri = new Uri(
            baseUri,
            "rest/v1/subscriptions?select=provider,provider_payment_id&source_system=eq.wordpress_pmpro&status=eq.active&limit=5000");
        using var request = CreateSupabaseRequest(HttpMethod.Get, uri);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return [];
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<List<ImportedSubscriptionLookupRow>>(stream, cancellationToken: cancellationToken) ?? [];
    }

    private async Task<List<CurrentEntitlement>> FilterNativeSubscriptionDuplicatesAsync(
        Uri baseUri,
        IReadOnlyList<CurrentEntitlement> entitlements,
        IReadOnlyDictionary<string, string> subscriberIdsByEmail,
        CancellationToken cancellationToken)
    {
        if (entitlements.Count == 0 || subscriberIdsByEmail.Count == 0)
        {
            return entitlements.ToList();
        }

        var subscriberIds = subscriberIdsByEmail.Values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (subscriberIds.Length == 0)
        {
            return entitlements.ToList();
        }

        var nativeSubscriptionsBySubscriberId = await FetchActiveNativeSubscriptionsBySubscriberIdAsync(
            baseUri,
            subscriberIds,
            cancellationToken);
        if (nativeSubscriptionsBySubscriberId.Count == 0)
        {
            return entitlements.ToList();
        }

        var filtered = entitlements
            .Where(entitlement =>
            {
                if (!subscriberIdsByEmail.TryGetValue(entitlement.Email, out var subscriberId) ||
                    string.IsNullOrWhiteSpace(subscriberId))
                {
                    return true;
                }

                return !nativeSubscriptionsBySubscriberId.TryGetValue(subscriberId, out var nativeSubscriptions) ||
                       !HasMatchingNativeSubscription(entitlement, nativeSubscriptions);
            })
            .ToList();

        var suppressedCount = entitlements.Count - filtered.Count;
        if (suppressedCount > 0)
        {
            _logger.LogInformation(
                "Suppressed {Count} imported WordPress entitlements because matching native subscriptions already exist.",
                suppressedCount);
        }

        return filtered;
    }

    private async Task<Dictionary<string, List<NativeSubscriptionLookupRow>>> FetchActiveNativeSubscriptionsBySubscriberIdAsync(
        Uri baseUri,
        IReadOnlyList<string> subscriberIds,
        CancellationToken cancellationToken)
    {
        var normalizedSubscriberIds = subscriberIds
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (normalizedSubscriberIds.Length == 0)
        {
            return new Dictionary<string, List<NativeSubscriptionLookupRow>>(StringComparer.OrdinalIgnoreCase);
        }

        var inClause = string.Join(',', normalizedSubscriberIds.Select(value => $"\"{value.Replace("\"", "\\\"")}\""));
        var uri = new Uri(
            baseUri,
            $"rest/v1/subscriptions?select=subscriber_id,tier_code,provider,provider_payment_id,provider_transaction_id,provider_token,provider_email_token&subscriber_id=in.({inClause})&source_system=eq.shink_app&status=eq.active&limit=5000");
        using var request = CreateSupabaseRequest(HttpMethod.Get, uri);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "Native subscription lookup for WordPress entitlement filtering failed. Status={StatusCode} Body={Body}",
                (int)response.StatusCode,
                body);
            return new Dictionary<string, List<NativeSubscriptionLookupRow>>(StringComparer.OrdinalIgnoreCase);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var rows = await JsonSerializer.DeserializeAsync<List<NativeSubscriptionLookupRow>>(stream, cancellationToken: cancellationToken) ?? [];
        return rows
            .Where(row => !string.IsNullOrWhiteSpace(row.SubscriberId))
            .GroupBy(row => row.SubscriberId!.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);
    }

    private static bool HasMatchingNativeSubscription(
        CurrentEntitlement entitlement,
        IReadOnlyList<NativeSubscriptionLookupRow> nativeSubscriptions)
    {
        if (nativeSubscriptions.Count == 0)
        {
            return false;
        }

        return nativeSubscriptions.Any(nativeSubscription =>
            string.Equals(nativeSubscription.TierCode, entitlement.TierCode, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(nativeSubscription.Provider, entitlement.Provider, StringComparison.OrdinalIgnoreCase) &&
            BuildSubscriptionMatchKeys(entitlement.ProviderPaymentId, entitlement.ProviderTransactionId, entitlement.ProviderToken)
                .Overlaps(BuildSubscriptionMatchKeys(
                    nativeSubscription.ProviderPaymentId,
                    nativeSubscription.ProviderTransactionId,
                    nativeSubscription.ProviderToken,
                    nativeSubscription.ProviderEmailToken)));
    }

    private static HashSet<string> BuildSubscriptionMatchKeys(params string?[] values)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                keys.Add(value.Trim());
            }
        }

        return keys;
    }

    private async Task<List<CurrentEntitlement>> FetchImportedCurrentEntitlementsAsync(
        Uri baseUri,
        string email,
        CancellationToken cancellationToken)
    {
        using var request = CreateSupabaseJsonRequest(
            HttpMethod.Post,
            new Uri(baseUri, "rest/v1/rpc/get_wordpress_current_entitlements"),
            new { p_normalized_email = email.Trim().ToLowerInvariant() });
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "WordPress imported entitlement lookup failed. email={Email} Status={StatusCode} Body={Body}",
                email,
                (int)response.StatusCode,
                body);
            return [];
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var rows = await JsonSerializer.DeserializeAsync<List<CurrentEntitlementRpcRow>>(stream, cancellationToken: cancellationToken) ?? [];
        return rows
            .Where(row => !string.IsNullOrWhiteSpace(row.TierCode) && !string.IsNullOrWhiteSpace(row.ProviderPaymentId))
            .Select(row => new CurrentEntitlement(
                Email: email.Trim().ToLowerInvariant(),
                TierCode: row.TierCode!.Trim(),
                Provider: NormalizeProvider(row.Provider),
                ProviderPaymentId: row.ProviderPaymentId!.Trim(),
                ProviderTransactionId: NullIfWhiteSpace(row.ProviderTransactionId),
                ProviderToken: NullIfWhiteSpace(row.ProviderToken),
                ProviderEmailToken: null,
                SubscribedAt: row.SubscribedAt,
                NextRenewalAt: row.NextRenewalAt))
            .ToList();
    }

    private async Task<int> ReconcileNativePaystackRetryTokensAsync(Uri baseUri, CancellationToken cancellationToken)
    {
        using var request = CreateSupabaseJsonRequest(
            HttpMethod.Post,
            new Uri(baseUri, "rest/v1/rpc/reconcile_native_paystack_retry_tokens"),
            new { });
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "Native Paystack retry token reconciliation failed. Status={StatusCode} Body={Body}",
                (int)response.StatusCode,
                body);
            return 0;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var rows = await JsonSerializer.DeserializeAsync<List<UpdatedSubscriptionIdRow>>(stream, cancellationToken: cancellationToken) ?? [];
        return rows.Count;
    }

    private async Task<int> ReactivateCancelledImportedEntitlementsAsync(
        Uri baseUri,
        IReadOnlyList<CurrentEntitlement> entitlements,
        CancellationToken cancellationToken)
    {
        var payload = entitlements
            .Where(entitlement =>
                !string.IsNullOrWhiteSpace(entitlement.Provider) &&
                !string.IsNullOrWhiteSpace(entitlement.ProviderPaymentId))
            .Select(entitlement => new
            {
                provider = NormalizeProvider(entitlement.Provider),
                provider_payment_id = entitlement.ProviderPaymentId.Trim(),
                next_renewal_at = entitlement.NextRenewalAt?.UtcDateTime,
                source_system = "wordpress_pmpro"
            })
            .Distinct()
            .ToList();
        if (payload.Count == 0)
        {
            return 0;
        }

        var reactivatedCount = 0;
        foreach (var batch in Batch(payload, 250))
        {
            using var request = CreateSupabaseJsonRequest(
                HttpMethod.Post,
                new Uri(baseUri, "rest/v1/rpc/reactivate_wordpress_current_subscriptions"),
                new { payload = batch });
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "Supabase imported subscription reactivation failed. Status={StatusCode} Body={Body}",
                    (int)response.StatusCode,
                    body);
                continue;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var rows = await JsonSerializer.DeserializeAsync<List<UpdatedSubscriptionIdRow>>(stream, cancellationToken: cancellationToken) ?? [];
            reactivatedCount += rows.Count;
        }

        return reactivatedCount;
    }

    private async Task<List<CurrentEntitlement>> EnrichPaystackEntitlementsAsync(
        IReadOnlyList<CurrentEntitlement> entitlements,
        CancellationToken cancellationToken)
    {
        if (entitlements.Count == 0)
        {
            return [];
        }

        var enriched = new List<CurrentEntitlement>(entitlements.Count);
        var lastRequestAtUtc = DateTimeOffset.MinValue;

        foreach (var entitlement in entitlements)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!string.Equals(entitlement.Provider, "paystack", StringComparison.OrdinalIgnoreCase))
            {
                enriched.Add(entitlement);
                continue;
            }

            var subscriptionCode = PaystackSubscriptionCodeResolver.ResolveSubscriptionCode(
                entitlement.Provider,
                "wordpress_pmpro",
                entitlement.ProviderPaymentId,
                entitlement.ProviderTransactionId);
            var transactionReference = NullIfWhiteSpace(entitlement.ProviderTransactionId);
            var authorizationCode = NullIfWhiteSpace(entitlement.ProviderToken);
            string? emailToken = NullIfWhiteSpace(entitlement.ProviderEmailToken);
            string? resolvedTransactionId = transactionReference;

            if (subscriptionCode is not null)
            {
                await DelayForPaystackRateLimitAsync(lastRequestAtUtc, cancellationToken);
                lastRequestAtUtc = DateTimeOffset.UtcNow;

                var subscription = await _paystackCheckoutService.GetSubscriptionAsync(subscriptionCode, cancellationToken);
                if (subscription.IsSuccess)
                {
                    authorizationCode = NullIfWhiteSpace(subscription.AuthorizationCode) ?? authorizationCode;
                    emailToken = NullIfWhiteSpace(subscription.EmailToken) ?? emailToken;
                    resolvedTransactionId = NullIfWhiteSpace(subscription.SubscriptionCode) ?? resolvedTransactionId;
                }
                else
                {
                    _logger.LogWarning(
                        "Paystack subscription lookup failed for imported WordPress entitlement. email={Email} payment_id={ProviderPaymentId} transaction_id={ProviderTransactionId} message={Message}",
                        entitlement.Email,
                        entitlement.ProviderPaymentId,
                        entitlement.ProviderTransactionId,
                        subscription.ErrorMessage);
                }
            }
            else if (!string.IsNullOrWhiteSpace(transactionReference))
            {
                await DelayForPaystackRateLimitAsync(lastRequestAtUtc, cancellationToken);
                lastRequestAtUtc = DateTimeOffset.UtcNow;

                var verification = await _paystackCheckoutService.VerifyTransactionAsync(transactionReference, cancellationToken);
                if (verification.IsSuccess)
                {
                    authorizationCode = NullIfWhiteSpace(verification.AuthorizationCode) ?? authorizationCode;
                    emailToken = NullIfWhiteSpace(verification.EmailToken) ?? emailToken;
                    resolvedTransactionId = NullIfWhiteSpace(verification.SubscriptionCode)
                        ?? resolvedTransactionId;

                    var verifiedSubscriptionCode = PaystackSubscriptionCodeResolver.ResolveSubscriptionCode(
                        entitlement.Provider,
                        "wordpress_pmpro",
                        entitlement.ProviderPaymentId,
                        resolvedTransactionId);
                    if (verifiedSubscriptionCode is not null && string.IsNullOrWhiteSpace(emailToken))
                    {
                        await DelayForPaystackRateLimitAsync(lastRequestAtUtc, cancellationToken);
                        lastRequestAtUtc = DateTimeOffset.UtcNow;

                        var subscription = await _paystackCheckoutService.GetSubscriptionAsync(verifiedSubscriptionCode, cancellationToken);
                        if (subscription.IsSuccess)
                        {
                            authorizationCode = NullIfWhiteSpace(subscription.AuthorizationCode) ?? authorizationCode;
                            emailToken = NullIfWhiteSpace(subscription.EmailToken) ?? emailToken;
                            resolvedTransactionId = NullIfWhiteSpace(subscription.SubscriptionCode) ?? resolvedTransactionId;
                        }
                    }
                }
                else
                {
                    _logger.LogWarning(
                        "Paystack transaction verify failed for imported WordPress entitlement. email={Email} payment_id={ProviderPaymentId} transaction_id={ProviderTransactionId} message={Message}",
                        entitlement.Email,
                        entitlement.ProviderPaymentId,
                        entitlement.ProviderTransactionId,
                        verification.ErrorMessage);
                }
            }

            enriched.Add(entitlement with
            {
                ProviderTransactionId = resolvedTransactionId,
                ProviderToken = authorizationCode,
                ProviderEmailToken = emailToken
            });
        }

        return enriched;
    }

    private static async Task DelayForPaystackRateLimitAsync(DateTimeOffset lastRequestAtUtc, CancellationToken cancellationToken)
    {
        if (lastRequestAtUtc == DateTimeOffset.MinValue)
        {
            return;
        }

        var elapsed = DateTimeOffset.UtcNow - lastRequestAtUtc;
        var minimumSpacing = TimeSpan.FromMilliseconds(350);
        if (elapsed < minimumSpacing)
        {
            await Task.Delay(minimumSpacing - elapsed, cancellationToken);
        }
    }

    private async Task<int> BackfillAuthUsersWithoutSubscribersAsync(
        Uri baseUri,
        IReadOnlyDictionary<string, string> knownSubscribersByEmail,
        CancellationToken cancellationToken)
    {
        var authEmails = await ListAuthUserEmailsAsync(cancellationToken);
        var missingEmails = authEmails
            .Where(email => !knownSubscribersByEmail.ContainsKey(email))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (missingEmails.Count == 0)
        {
            return 0;
        }

        var payload = missingEmails
            .Select(email => new SubscriberUpsertRow(
                Email: email,
                FirstName: null,
                LastName: null,
                DisplayName: null,
                MobileNumber: null,
                LastLoginAt: null,
                ProfileImageUrl: null,
                ProfileImageObjectKey: null,
                ProfileImageContentType: null))
            .ToList();
        var rows = await UpsertSubscribersAsync(baseUri, payload, cancellationToken);
        return rows.Count;
    }

    private async Task<HashSet<string>> ListAuthUserEmailsAsync(CancellationToken cancellationToken)
    {
        var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!TryBuildSupabaseBaseUri(out var baseUri))
        {
            return results;
        }

        var page = 1;
        while (true)
        {
            var uri = new Uri(baseUri, $"auth/v1/admin/users?page={page}&per_page=1000");
            using var request = CreateSupabaseRequest(HttpMethod.Get, uri);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "Supabase auth user list failed. page={Page} Status={StatusCode} Body={Body}",
                    page,
                    (int)response.StatusCode,
                    body);
                break;
            }

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            if (!document.RootElement.TryGetProperty("users", out var usersNode) || usersNode.ValueKind != JsonValueKind.Array)
            {
                break;
            }

            var count = 0;
            foreach (var userNode in usersNode.EnumerateArray())
            {
                count++;
                if (!userNode.TryGetProperty("email", out var emailNode) || emailNode.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var normalizedEmail = NormalizeEmail(emailNode.GetString());
                if (normalizedEmail is not null)
                {
                    results.Add(normalizedEmail);
                }
            }

            if (count < 1000)
            {
                break;
            }

            page++;
        }

        return results;
    }

    private async Task<List<SubscriberProfileRow>> FetchSubscriberRowsAsync(Uri baseUri, CancellationToken cancellationToken)
    {
        var uri = new Uri(
            baseUri,
            "rest/v1/subscribers?select=subscriber_id,email,profile_image_url,profile_image_object_key,profile_image_content_type&limit=5000");
        using var request = CreateSupabaseRequest(HttpMethod.Get, uri);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return [];
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<List<SubscriberProfileRow>>(stream, cancellationToken: cancellationToken) ?? [];
    }

    private static List<CurrentEntitlement> BuildCurrentEntitlements(
        IReadOnlyList<CanonicalWordPressUser> wordPressUsers,
        IReadOnlyList<WordPressMembershipPeriod> membershipPeriods,
        IReadOnlyList<WordPressMembershipOrder> membershipOrders,
        IReadOnlyList<WordPressSubscription> subscriptions,
        IReadOnlySet<string> discountCodeAccessKeys)
    {
        var now = DateTimeOffset.UtcNow;
        var userRegisteredById = wordPressUsers.ToDictionary(
            user => user.WordPressUserId,
            user => user.UserRegistered);

        var earliestMembershipStartByUserAndLevel = membershipPeriods
            .GroupBy(period => BuildUserLevelKey(period.WordPressUserId, period.MembershipLevelId), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(period => period.StartDate)
                    .Where(value => value.HasValue)
                    .Select(value => value!.Value)
                    .OrderBy(value => value)
                    .Cast<DateTimeOffset?>()
                    .FirstOrDefault(),
                StringComparer.OrdinalIgnoreCase);

        var earliestMembershipModifiedByUserAndLevel = membershipPeriods
            .GroupBy(period => BuildUserLevelKey(period.WordPressUserId, period.MembershipLevelId), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(period => period.ModifiedAt)
                    .Where(value => value.HasValue)
                    .Select(value => value!.Value)
                    .OrderBy(value => value)
                    .Cast<DateTimeOffset?>()
                    .FirstOrDefault(),
                StringComparer.OrdinalIgnoreCase);

        var earliestSuccessfulOrderTimestampByUserAndLevel = membershipOrders
            .Where(order => string.Equals(order.Status, "success", StringComparison.OrdinalIgnoreCase))
            .GroupBy(order => BuildUserLevelKey(order.WordPressUserId, order.MembershipLevelId), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(order => order.OrderTimestamp)
                    .Where(value => value.HasValue)
                    .Select(value => value!.Value)
                    .OrderBy(value => value)
                    .Cast<DateTimeOffset?>()
                    .FirstOrDefault(),
                StringComparer.OrdinalIgnoreCase);

        var earliestSubscriptionStartByUserAndLevel = subscriptions
            .GroupBy(subscription => BuildUserLevelKey(subscription.WordPressUserId, subscription.MembershipLevelId), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(subscription => subscription.StartDate)
                    .Where(value => value.HasValue)
                    .Select(value => value!.Value)
                    .OrderBy(value => value)
                    .Cast<DateTimeOffset?>()
                    .FirstOrDefault(),
                StringComparer.OrdinalIgnoreCase);

        var earliestSubscriptionModifiedByUserAndLevel = subscriptions
            .GroupBy(subscription => BuildUserLevelKey(subscription.WordPressUserId, subscription.MembershipLevelId), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(subscription => subscription.ModifiedAt)
                    .Where(value => value.HasValue)
                    .Select(value => value!.Value)
                    .OrderBy(value => value)
                    .Cast<DateTimeOffset?>()
                    .FirstOrDefault(),
                StringComparer.OrdinalIgnoreCase);

        var successfulOrdersByUserAndLevel = membershipOrders
            .Where(order => string.Equals(order.Status, "success", StringComparison.OrdinalIgnoreCase))
            .GroupBy(order => BuildUserLevelKey(order.WordPressUserId, order.MembershipLevelId), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(order => order.OrderTimestamp ?? DateTimeOffset.MinValue)
                    .ThenByDescending(order => order.OrderId)
                    .First(),
                StringComparer.OrdinalIgnoreCase);

        var activeSubscriptionsByUserAndLevel = subscriptions
            .Where(subscription => string.Equals(subscription.Status, "active", StringComparison.OrdinalIgnoreCase))
            .GroupBy(subscription => BuildUserLevelKey(subscription.WordPressUserId, subscription.MembershipLevelId), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(subscription => subscription.ModifiedAt ?? subscription.StartDate ?? DateTimeOffset.MinValue)
                    .ThenByDescending(subscription => subscription.SubscriptionId)
                    .First(),
                StringComparer.OrdinalIgnoreCase);

        return membershipPeriods
            .Where(period =>
                string.Equals(period.Status, "active", StringComparison.OrdinalIgnoreCase) ||
                HasFutureDiscountCodeAccess(period, discountCodeAccessKeys, now))
            .Where(period => !string.IsNullOrWhiteSpace(period.TierCode))
            .Select(period =>
            {
                var key = BuildUserLevelKey(period.WordPressUserId, period.MembershipLevelId);
                activeSubscriptionsByUserAndLevel.TryGetValue(key, out var subscription);
                successfulOrdersByUserAndLevel.TryGetValue(key, out var order);
                var provider = NormalizeProvider(subscription?.Gateway ?? order?.Gateway ?? "free");
                var providerTransactionId = PaystackSubscriptionCodeResolver.ResolveImportedProviderTransactionId(
                    provider,
                    subscription?.SubscriptionTransactionId,
                    order?.SubscriptionTransactionId,
                    order?.PaymentTransactionId);
                return new CurrentEntitlement(
                    Email: period.Email ?? string.Empty,
                    TierCode: period.TierCode!,
                    Provider: provider,
                    ProviderPaymentId: $"wp-pmpro-current-{period.MembershipPeriodId.ToString(CultureInfo.InvariantCulture)}",
                    ProviderTransactionId: providerTransactionId,
                    ProviderToken: string.Equals(provider, "payfast", StringComparison.OrdinalIgnoreCase)
                        ? providerTransactionId
                        : null,
                    ProviderEmailToken: null,
                    SubscribedAt: WordPressImportDateConverter.ResolveSubscribedAt(
                        userRegisteredById.GetValueOrDefault(period.WordPressUserId),
                        earliestMembershipStartByUserAndLevel.GetValueOrDefault(key),
                        earliestSubscriptionStartByUserAndLevel.GetValueOrDefault(key),
                        earliestSuccessfulOrderTimestampByUserAndLevel.GetValueOrDefault(key),
                        earliestMembershipModifiedByUserAndLevel.GetValueOrDefault(key),
                        earliestSubscriptionModifiedByUserAndLevel.GetValueOrDefault(key)),
                    NextRenewalAt: subscription?.NextPaymentDate ?? period.EndDate);
            })
            .Where(entitlement => !string.IsNullOrWhiteSpace(entitlement.Email))
            .ToList();
    }

    private static bool HasFutureDiscountCodeAccess(
        WordPressMembershipPeriod period,
        IReadOnlySet<string> discountCodeAccessKeys,
        DateTimeOffset now)
    {
        if (!period.EndDate.HasValue || period.EndDate.Value <= now)
        {
            return false;
        }

        if (period.CodeId.HasValue)
        {
            return true;
        }

        return discountCodeAccessKeys.Contains(BuildUserLevelKey(period.WordPressUserId, period.MembershipLevelId));
    }

    private static string BuildUserLevelKey(long wordPressUserId, int membershipLevelId) =>
        $"{wordPressUserId}:{membershipLevelId}";

    private static WordPressUserImportRow BuildWordPressUserImportRow(CanonicalWordPressUser user) => new(
        WordPressUserId: user.WordPressUserId,
        NormalizedEmail: user.Email,
        UserLogin: user.UserLogin,
        UserNicename: user.UserNicename,
        DisplayName: user.DisplayName,
        FirstName: user.FirstName,
        LastName: user.LastName,
        MobileNumber: NormalizeMobileNumber(user.MobileNumber),
        UserRegistered: user.UserRegistered,
        PasswordHash: user.PasswordHash,
        PasswordHashFormat: user.PasswordHashFormat,
        IsPasswordMigrated: false,
        HasDuplicateEmail: user.HasDuplicateEmail,
        LastLoginAt: user.LastLoginAt,
        AvatarSourceUrl: user.AvatarSourceUrl,
        AvatarSourceAttachmentId: user.AvatarSourceAttachmentId,
        AvatarSourceMetaKey: user.AvatarSourceMetaKey,
        ProfileImageUrl: user.ProfileImageUrl,
        ProfileImageObjectKey: user.ProfileImageObjectKey,
        ProfileImageContentType: user.ProfileImageContentType);

    private static SubscriberUpsertRow BuildSubscriberUpsertRow(CanonicalWordPressUser user) => new(
        Email: user.Email,
        FirstName: user.FirstName,
        LastName: user.LastName,
        DisplayName: user.DisplayName,
        MobileNumber: NormalizeMobileNumber(user.MobileNumber),
        LastLoginAt: user.LastLoginAt,
        ProfileImageUrl: user.ProfileImageUrl,
        ProfileImageObjectKey: user.ProfileImageObjectKey,
        ProfileImageContentType: user.ProfileImageContentType);

    private static MembershipPeriodImportRow BuildMembershipPeriodImportRow(WordPressMembershipPeriod row) => new(
        MembershipPeriodId: row.MembershipPeriodId,
        WordPressUserId: row.WordPressUserId,
        Email: row.Email,
        MembershipLevelId: row.MembershipLevelId,
        MembershipLevelName: row.MembershipLevelName,
        TierCode: row.TierCode,
        CodeId: row.CodeId,
        Status: row.Status,
        StartDate: row.StartDate,
        EndDate: row.EndDate,
        ModifiedAt: row.ModifiedAt,
        InitialPayment: row.InitialPayment,
        BillingAmount: row.BillingAmount,
        CycleNumber: row.CycleNumber,
        CyclePeriod: row.CyclePeriod,
        BillingLimit: row.BillingLimit,
        TrialAmount: row.TrialAmount,
        TrialLimit: row.TrialLimit,
        RawRow: JsonSerializer.SerializeToElement(row, SerializerOptions));

    private static MembershipOrderImportRow BuildMembershipOrderImportRow(WordPressMembershipOrder row) => new(
        OrderId: row.OrderId,
        WordPressUserId: row.WordPressUserId,
        Email: row.Email,
        MembershipLevelId: row.MembershipLevelId,
        MembershipLevelName: row.MembershipLevelName,
        TierCode: row.TierCode,
        Code: row.Code,
        SessionId: row.SessionId,
        Status: row.Status,
        Gateway: NormalizeProvider(row.Gateway),
        GatewayEnvironment: row.GatewayEnvironment,
        PaymentType: row.PaymentType,
        PaymentTransactionId: row.PaymentTransactionId,
        SubscriptionTransactionId: row.SubscriptionTransactionId,
        BillingName: row.BillingName,
        BillingPhone: NormalizeMobileNumber(row.BillingPhone),
        BillingCountry: row.BillingCountry,
        Subtotal: row.Subtotal,
        Tax: row.Tax,
        CouponAmount: row.CouponAmount,
        Total: row.Total,
        OrderTimestamp: row.OrderTimestamp,
        RawMeta: JsonSerializer.SerializeToElement(row.RawMeta, SerializerOptions),
        RawRow: JsonSerializer.SerializeToElement(row, SerializerOptions));

    private static SubscriptionImportRow BuildSubscriptionImportRow(WordPressSubscription row) => new(
        SubscriptionId: row.SubscriptionId,
        WordPressUserId: row.WordPressUserId,
        Email: row.Email,
        MembershipLevelId: row.MembershipLevelId,
        MembershipLevelName: row.MembershipLevelName,
        TierCode: row.TierCode,
        Gateway: NormalizeProvider(row.Gateway),
        GatewayEnvironment: row.GatewayEnvironment,
        SubscriptionTransactionId: row.SubscriptionTransactionId,
        Status: row.Status,
        StartDate: row.StartDate,
        EndDate: row.EndDate,
        NextPaymentDate: row.NextPaymentDate,
        ModifiedAt: row.ModifiedAt,
        BillingAmount: row.BillingAmount,
        CycleNumber: row.CycleNumber,
        CyclePeriod: row.CyclePeriod,
        BillingLimit: row.BillingLimit,
        TrialAmount: row.TrialAmount,
        TrialLimit: row.TrialLimit,
        RawMeta: JsonSerializer.SerializeToElement(row.RawMeta, SerializerOptions),
        RawRow: JsonSerializer.SerializeToElement(row, SerializerOptions));

    private static string ResolveTierCode(string? membershipLevelName) =>
        membershipLevelName is not null && MembershipTierMap.TryGetValue(membershipLevelName, out var tierCode)
            ? tierCode
            : string.Empty;

    private static string NormalizeProvider(string? provider)
    {
        var normalized = provider?.Trim().ToLowerInvariant();
        return normalized switch
        {
            "payfast" => "payfast",
            "paystack" => "paystack",
            "free" => "free",
            _ => "free"
        };
    }

    private static string? NormalizeEmail(string? email)
    {
        var normalized = email?.Trim().ToLowerInvariant();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static string? NormalizeMobileNumber(string? mobileNumber)
    {
        if (string.IsNullOrWhiteSpace(mobileNumber))
        {
            return null;
        }

        var sanitized = new string(mobileNumber.Trim().Where(character => char.IsDigit(character) || character == '+').ToArray());
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            return null;
        }

        if (sanitized.StartsWith("00", StringComparison.Ordinal))
        {
            sanitized = "+" + sanitized[2..];
        }

        return sanitized.Length is >= 7 and <= 20 ? sanitized : null;
    }

    private static string? DetermineAvatarSourceUrl(string? attachmentUrl, string? basicAvatarValue)
    {
        if (!string.IsNullOrWhiteSpace(attachmentUrl))
        {
            return attachmentUrl.Trim();
        }

        if (string.IsNullOrWhiteSpace(basicAvatarValue))
        {
            return null;
        }

        var match = BasicAvatarUrlRegex().Match(basicAvatarValue);
        return match.Success ? match.Groups["url"].Value.Trim() : null;
    }

    private static string? DetermineAvatarSourceMetaKey(string? attachmentUrl, string? basicAvatarValue)
    {
        if (!string.IsNullOrWhiteSpace(attachmentUrl))
        {
            return "wplx_user_avatar";
        }

        return string.IsNullOrWhiteSpace(basicAvatarValue) ? null : "basic_user_avatar";
    }

    private static string TryGetFileNameFromUrl(string url, string? contentType)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            var fileName = Path.GetFileName(uri.LocalPath);
            if (!string.IsNullOrWhiteSpace(fileName))
            {
                return fileName;
            }
        }

        return contentType switch
        {
            "image/jpeg" => "avatar.jpg",
            "image/png" => "avatar.png",
            "image/webp" => "avatar.webp",
            "image/gif" => "avatar.gif",
            _ => "avatar.img"
        };
    }

    private static string BuildProviderPaymentKey(string? provider, string? providerPaymentId) =>
        $"{provider?.Trim().ToLowerInvariant()}|{providerPaymentId?.Trim()}";

    private async Task<MySqlConnection> OpenWordPressConnectionAsync(CancellationToken cancellationToken)
    {
        var builder = new MySqlConnectionStringBuilder
        {
            Server = _wordPressOptions.Host.Trim(),
            Port = (uint)Math.Max(_wordPressOptions.Port, 1),
            Database = _wordPressOptions.Database.Trim(),
            UserID = _wordPressOptions.Username.Trim(),
            Password = _wordPressOptions.Password,
            CharacterSet = "utf8mb4",
            ConnectionTimeout = 30,
            DefaultCommandTimeout = 120,
            AllowZeroDateTime = true,
            ConvertZeroDateTime = true
        };

        var connection = new MySqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private bool TryValidateWordPressConfiguration(out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(_wordPressOptions.Host) ||
            string.IsNullOrWhiteSpace(_wordPressOptions.Database) ||
            string.IsNullOrWhiteSpace(_wordPressOptions.Username) ||
            string.IsNullOrWhiteSpace(_wordPressOptions.Password))
        {
            error = "WordPress database configuration is incomplete.";
            return false;
        }

        if (!TablePrefixRegex().IsMatch(_wordPressOptions.TablePrefix ?? string.Empty))
        {
            error = "WordPress table prefix contains unsupported characters.";
            return false;
        }

        return true;
    }

    private bool TryBuildSupabaseBaseUri(out Uri baseUri)
    {
        baseUri = default!;
        if (string.IsNullOrWhiteSpace(_supabaseOptions.Url) || string.IsNullOrWhiteSpace(_supabaseOptions.ServiceRoleKey))
        {
            return false;
        }

        if (!Uri.TryCreate(_supabaseOptions.Url, UriKind.Absolute, out var parsedBaseUri))
        {
            _logger.LogWarning("Supabase URL is invalid: {SupabaseUrl}", _supabaseOptions.Url);
            return false;
        }

        baseUri = parsedBaseUri;
        return true;
    }

    private string Table(string suffix) => $"{(_wordPressOptions.TablePrefix ?? string.Empty).Trim()}{suffix}";

    private HttpRequestMessage CreateSupabaseRequest(HttpMethod method, Uri uri)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.TryAddWithoutValidation("apikey", _supabaseOptions.ServiceRoleKey);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _supabaseOptions.ServiceRoleKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private HttpRequestMessage CreateSupabaseJsonRequest(
        HttpMethod method,
        Uri uri,
        object? payload,
        string? preferHeader = null)
    {
        var request = CreateSupabaseRequest(method, uri);
        request.Content = JsonContent.Create(payload, options: SerializerOptions);
        if (!string.IsNullOrWhiteSpace(preferHeader))
        {
            request.Headers.TryAddWithoutValidation("Prefer", preferHeader);
        }

        return request;
    }

    private async Task<int> ReadRpcCountResponseAsync(
        HttpResponseMessage response,
        string operationName,
        CancellationToken cancellationToken)
    {
        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "{OperationName} failed. Status={StatusCode} Body={Body}",
                    operationName,
                    (int)response.StatusCode,
                    body);
                return 0;
            }

            if (int.TryParse(body, NumberStyles.Integer, CultureInfo.InvariantCulture, out var directValue))
            {
                return directValue;
            }

            try
            {
                using var document = JsonDocument.Parse(body);
                return document.RootElement.ValueKind switch
                {
                    JsonValueKind.Number when document.RootElement.TryGetInt32(out var scalar) => scalar,
                    JsonValueKind.Array when document.RootElement.GetArrayLength() > 0 &&
                                           document.RootElement[0].ValueKind == JsonValueKind.Number &&
                                           document.RootElement[0].TryGetInt32(out var arrayScalar) => arrayScalar,
                    _ => 0
                };
            }
            catch (JsonException)
            {
                return 0;
            }
        }
    }

    private static IEnumerable<List<T>> Batch<T>(IEnumerable<T> source, int size)
    {
        var batch = new List<T>(size);
        foreach (var item in source)
        {
            batch.Add(item);
            if (batch.Count < size)
            {
                continue;
            }

            yield return batch;
            batch = new List<T>(size);
        }

        if (batch.Count > 0)
        {
            yield return batch;
        }
    }

    private static string? ReadTrimmedString(MySqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        var value = reader.GetString(ordinal).Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static long ReadInt64(MySqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? 0 : Convert.ToInt64(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
    }

    private static int ReadInt32(MySqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? 0 : Convert.ToInt32(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
    }

    private static long? ReadNullableInt64(MySqlDataReader reader, string columnName)
    {
        var value = ReadInt64(reader, columnName);
        return value == 0 ? null : value;
    }

    private static long? ReadNullableInt64FromString(MySqlDataReader reader, string columnName)
    {
        var value = ReadTrimmedString(reader, columnName);
        return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }

    private static int? ReadNullableInt32(MySqlDataReader reader, string columnName)
    {
        var value = ReadInt32(reader, columnName);
        return value == 0 ? null : value;
    }

    private static decimal? ReadNullableDecimal(MySqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        return Convert.ToDecimal(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
    }

    private static DateTimeOffset? ReadNullableDateTime(MySqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        return WordPressImportDateConverter.ConvertToNullableDateTime(reader.GetValue(ordinal));
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [GeneratedRegex("s:4:\"full\";s:\\d+:\"(?<url>[^\"]+)\"", RegexOptions.CultureInvariant)]
    private static partial Regex BasicAvatarUrlRegex();

    [GeneratedRegex("^[A-Za-z0-9_]+$", RegexOptions.CultureInvariant)]
    private static partial Regex TablePrefixRegex();

    private sealed class CanonicalWordPressUser
    {
        public long WordPressUserId { get; init; }
        public string Email { get; init; } = string.Empty;
        public string UserLogin { get; init; } = string.Empty;
        public string UserNicename { get; init; } = string.Empty;
        public string? PasswordHash { get; init; }
        public string? PasswordHashFormat { get; set; }
        public string? DisplayName { get; init; }
        public string? FirstName { get; init; }
        public string? LastName { get; init; }
        public string? MobileNumber { get; init; }
        public DateTimeOffset? LastLoginAt { get; init; }
        public DateTimeOffset? UserRegistered { get; init; }
        public string? AvatarSourceUrl { get; init; }
        public long? AvatarSourceAttachmentId { get; init; }
        public string? AvatarSourceMetaKey { get; init; }
        public string? ProfileImageUrl { get; set; }
        public string? ProfileImageObjectKey { get; set; }
        public string? ProfileImageContentType { get; set; }
        public bool HasDuplicateEmail { get; set; }
    }

    private sealed class WordPressMembershipPeriod
    {
        public long MembershipPeriodId { get; init; }
        public long WordPressUserId { get; init; }
        public string? Email { get; init; }
        public int MembershipLevelId { get; init; }
        public string? MembershipLevelName { get; init; }
        public string? TierCode { get; init; }
        public long? CodeId { get; init; }
        public string? Status { get; init; }
        public DateTimeOffset? StartDate { get; init; }
        public DateTimeOffset? EndDate { get; init; }
        public DateTimeOffset? ModifiedAt { get; init; }
        public decimal? InitialPayment { get; init; }
        public decimal? BillingAmount { get; init; }
        public int? CycleNumber { get; init; }
        public string? CyclePeriod { get; init; }
        public int? BillingLimit { get; init; }
        public decimal? TrialAmount { get; init; }
        public int? TrialLimit { get; init; }
    }

    private sealed class WordPressMembershipOrder
    {
        public long OrderId { get; init; }
        public long WordPressUserId { get; init; }
        public string? Email { get; init; }
        public int MembershipLevelId { get; init; }
        public string? MembershipLevelName { get; init; }
        public string? TierCode { get; init; }
        public string? Code { get; init; }
        public string? SessionId { get; init; }
        public string? Status { get; init; }
        public string? Gateway { get; init; }
        public string? GatewayEnvironment { get; init; }
        public string? PaymentType { get; init; }
        public string? PaymentTransactionId { get; init; }
        public string? SubscriptionTransactionId { get; init; }
        public string? BillingName { get; init; }
        public string? BillingPhone { get; init; }
        public string? BillingCountry { get; init; }
        public string? Subtotal { get; init; }
        public string? Tax { get; init; }
        public string? CouponAmount { get; init; }
        public string? Total { get; init; }
        public DateTimeOffset? OrderTimestamp { get; init; }
        public IReadOnlyDictionary<string, string> RawMeta { get; init; } = new Dictionary<string, string>();
    }

    private sealed class WordPressSubscription
    {
        public long SubscriptionId { get; init; }
        public long WordPressUserId { get; init; }
        public string? Email { get; init; }
        public int MembershipLevelId { get; init; }
        public string? MembershipLevelName { get; init; }
        public string? TierCode { get; init; }
        public string? Gateway { get; init; }
        public string? GatewayEnvironment { get; init; }
        public string? SubscriptionTransactionId { get; init; }
        public string? Status { get; init; }
        public DateTimeOffset? StartDate { get; init; }
        public DateTimeOffset? EndDate { get; init; }
        public DateTimeOffset? NextPaymentDate { get; init; }
        public DateTimeOffset? ModifiedAt { get; init; }
        public decimal? BillingAmount { get; init; }
        public int? CycleNumber { get; init; }
        public string? CyclePeriod { get; init; }
        public int? BillingLimit { get; init; }
        public decimal? TrialAmount { get; init; }
        public int? TrialLimit { get; init; }
        public IReadOnlyDictionary<string, string> RawMeta { get; init; } = new Dictionary<string, string>();
    }

    private sealed record CurrentEntitlement(
        string Email,
        string TierCode,
        string Provider,
        string ProviderPaymentId,
        string? ProviderTransactionId,
        string? ProviderToken,
        string? ProviderEmailToken,
        DateTimeOffset? SubscribedAt,
        DateTimeOffset? NextRenewalAt);

    private sealed record WordPressUserImportRow(
        [property: JsonPropertyName("wp_user_id")] long WordPressUserId,
        [property: JsonPropertyName("normalized_email")] string NormalizedEmail,
        [property: JsonPropertyName("user_login")] string UserLogin,
        [property: JsonPropertyName("user_nicename")] string UserNicename,
        [property: JsonPropertyName("display_name")] string? DisplayName,
        [property: JsonPropertyName("first_name")] string? FirstName,
        [property: JsonPropertyName("last_name")] string? LastName,
        [property: JsonPropertyName("mobile_number")] string? MobileNumber,
        [property: JsonPropertyName("user_registered")] DateTimeOffset? UserRegistered,
        [property: JsonPropertyName("password_hash")] string? PasswordHash,
        [property: JsonPropertyName("password_hash_format")] string? PasswordHashFormat,
        [property: JsonPropertyName("is_password_migrated")] bool IsPasswordMigrated,
        [property: JsonPropertyName("has_duplicate_email")] bool HasDuplicateEmail,
        [property: JsonPropertyName("last_login_at")] DateTimeOffset? LastLoginAt,
        [property: JsonPropertyName("avatar_source_url")] string? AvatarSourceUrl,
        [property: JsonPropertyName("avatar_source_attachment_id")] long? AvatarSourceAttachmentId,
        [property: JsonPropertyName("avatar_source_meta_key")] string? AvatarSourceMetaKey,
        [property: JsonPropertyName("profile_image_url")] string? ProfileImageUrl,
        [property: JsonPropertyName("profile_image_object_key")] string? ProfileImageObjectKey,
        [property: JsonPropertyName("profile_image_content_type")] string? ProfileImageContentType);

    private sealed record MembershipPeriodImportRow(
        [property: JsonPropertyName("wp_membership_period_id")] long MembershipPeriodId,
        [property: JsonPropertyName("wp_user_id")] long WordPressUserId,
        [property: JsonPropertyName("normalized_email")] string? Email,
        [property: JsonPropertyName("membership_level_id")] int MembershipLevelId,
        [property: JsonPropertyName("membership_level_name")] string? MembershipLevelName,
        [property: JsonPropertyName("tier_code")] string? TierCode,
        [property: JsonPropertyName("code_id")] long? CodeId,
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("startdate")] DateTimeOffset? StartDate,
        [property: JsonPropertyName("enddate")] DateTimeOffset? EndDate,
        [property: JsonPropertyName("modified_at")] DateTimeOffset? ModifiedAt,
        [property: JsonPropertyName("initial_payment")] decimal? InitialPayment,
        [property: JsonPropertyName("billing_amount")] decimal? BillingAmount,
        [property: JsonPropertyName("cycle_number")] int? CycleNumber,
        [property: JsonPropertyName("cycle_period")] string? CyclePeriod,
        [property: JsonPropertyName("billing_limit")] int? BillingLimit,
        [property: JsonPropertyName("trial_amount")] decimal? TrialAmount,
        [property: JsonPropertyName("trial_limit")] int? TrialLimit,
        [property: JsonPropertyName("raw_row")] JsonElement RawRow);

    private sealed record MembershipOrderImportRow(
        [property: JsonPropertyName("wp_order_id")] long OrderId,
        [property: JsonPropertyName("wp_user_id")] long WordPressUserId,
        [property: JsonPropertyName("normalized_email")] string? Email,
        [property: JsonPropertyName("membership_level_id")] int MembershipLevelId,
        [property: JsonPropertyName("membership_level_name")] string? MembershipLevelName,
        [property: JsonPropertyName("tier_code")] string? TierCode,
        [property: JsonPropertyName("code")] string? Code,
        [property: JsonPropertyName("session_id")] string? SessionId,
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("gateway")] string? Gateway,
        [property: JsonPropertyName("gateway_environment")] string? GatewayEnvironment,
        [property: JsonPropertyName("payment_type")] string? PaymentType,
        [property: JsonPropertyName("payment_transaction_id")] string? PaymentTransactionId,
        [property: JsonPropertyName("subscription_transaction_id")] string? SubscriptionTransactionId,
        [property: JsonPropertyName("billing_name")] string? BillingName,
        [property: JsonPropertyName("billing_phone")] string? BillingPhone,
        [property: JsonPropertyName("billing_country")] string? BillingCountry,
        [property: JsonPropertyName("subtotal")] string? Subtotal,
        [property: JsonPropertyName("tax")] string? Tax,
        [property: JsonPropertyName("couponamount")] string? CouponAmount,
        [property: JsonPropertyName("total")] string? Total,
        [property: JsonPropertyName("order_timestamp")] DateTimeOffset? OrderTimestamp,
        [property: JsonPropertyName("raw_meta")] JsonElement RawMeta,
        [property: JsonPropertyName("raw_row")] JsonElement RawRow);

    private sealed record SubscriptionImportRow(
        [property: JsonPropertyName("wp_subscription_id")] long SubscriptionId,
        [property: JsonPropertyName("wp_user_id")] long WordPressUserId,
        [property: JsonPropertyName("normalized_email")] string? Email,
        [property: JsonPropertyName("membership_level_id")] int MembershipLevelId,
        [property: JsonPropertyName("membership_level_name")] string? MembershipLevelName,
        [property: JsonPropertyName("tier_code")] string? TierCode,
        [property: JsonPropertyName("gateway")] string? Gateway,
        [property: JsonPropertyName("gateway_environment")] string? GatewayEnvironment,
        [property: JsonPropertyName("subscription_transaction_id")] string? SubscriptionTransactionId,
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("startdate")] DateTimeOffset? StartDate,
        [property: JsonPropertyName("enddate")] DateTimeOffset? EndDate,
        [property: JsonPropertyName("next_payment_date")] DateTimeOffset? NextPaymentDate,
        [property: JsonPropertyName("modified_at")] DateTimeOffset? ModifiedAt,
        [property: JsonPropertyName("billing_amount")] decimal? BillingAmount,
        [property: JsonPropertyName("cycle_number")] int? CycleNumber,
        [property: JsonPropertyName("cycle_period")] string? CyclePeriod,
        [property: JsonPropertyName("billing_limit")] int? BillingLimit,
        [property: JsonPropertyName("trial_amount")] decimal? TrialAmount,
        [property: JsonPropertyName("trial_limit")] int? TrialLimit,
        [property: JsonPropertyName("raw_meta")] JsonElement RawMeta,
        [property: JsonPropertyName("raw_row")] JsonElement RawRow);

    private sealed record SubscriberUpsertRow(
        [property: JsonPropertyName("email")] string Email,
        [property: JsonPropertyName("first_name")] string? FirstName,
        [property: JsonPropertyName("last_name")] string? LastName,
        [property: JsonPropertyName("display_name")] string? DisplayName,
        [property: JsonPropertyName("mobile_number")] string? MobileNumber,
        [property: JsonPropertyName("last_login_at")] DateTimeOffset? LastLoginAt,
        [property: JsonPropertyName("profile_image_url")] string? ProfileImageUrl,
        [property: JsonPropertyName("profile_image_object_key")] string? ProfileImageObjectKey,
        [property: JsonPropertyName("profile_image_content_type")] string? ProfileImageContentType);

    private sealed class SubscriberProfileRow
    {
        [JsonPropertyName("subscriber_id")]
        public string? SubscriberId { get; set; }

        [JsonPropertyName("email")]
        public string? Email { get; set; }

        [JsonPropertyName("profile_image_url")]
        public string? ProfileImageUrl { get; set; }

        [JsonPropertyName("profile_image_object_key")]
        public string? ProfileImageObjectKey { get; set; }

        [JsonPropertyName("profile_image_content_type")]
        public string? ProfileImageContentType { get; set; }
    }

    private sealed class SubscriberIdLookupRow
    {
        [JsonPropertyName("subscriber_id")]
        public string? SubscriberId { get; set; }

        [JsonPropertyName("email")]
        public string? Email { get; set; }
    }

    private sealed class SubscriptionIdLookupRow
    {
        [JsonPropertyName("subscription_id")]
        public string? SubscriptionId { get; set; }
    }

    private sealed class UpdatedSubscriptionIdRow
    {
        [JsonPropertyName("updated_subscription_id")]
        public string? UpdatedSubscriptionId { get; set; }
    }

    private sealed class ImportedSubscriptionLookupRow
    {
        [JsonPropertyName("provider")]
        public string? Provider { get; set; }

        [JsonPropertyName("provider_payment_id")]
        public string? ProviderPaymentId { get; set; }
    }

    private sealed class NativeSubscriptionLookupRow
    {
        [JsonPropertyName("subscriber_id")]
        public string? SubscriberId { get; set; }

        [JsonPropertyName("tier_code")]
        public string? TierCode { get; set; }

        [JsonPropertyName("provider")]
        public string? Provider { get; set; }

        [JsonPropertyName("provider_payment_id")]
        public string? ProviderPaymentId { get; set; }

        [JsonPropertyName("provider_transaction_id")]
        public string? ProviderTransactionId { get; set; }

        [JsonPropertyName("provider_token")]
        public string? ProviderToken { get; set; }

        [JsonPropertyName("provider_email_token")]
        public string? ProviderEmailToken { get; set; }
    }

    private sealed class CurrentEntitlementRpcRow
    {
        [JsonPropertyName("tier_code")]
        public string? TierCode { get; set; }

        [JsonPropertyName("provider")]
        public string? Provider { get; set; }

        [JsonPropertyName("provider_payment_id")]
        public string? ProviderPaymentId { get; set; }

        [JsonPropertyName("provider_transaction_id")]
        public string? ProviderTransactionId { get; set; }

        [JsonPropertyName("provider_token")]
        public string? ProviderToken { get; set; }

        [JsonPropertyName("subscribed_at")]
        public DateTimeOffset? SubscribedAt { get; set; }

        [JsonPropertyName("next_renewal_at")]
        public DateTimeOffset? NextRenewalAt { get; set; }
    }

    private sealed class WordPressImportedUserRow
    {
        [JsonPropertyName("wp_user_id")]
        public long WordPressUserId { get; set; }

        [JsonPropertyName("normalized_email")]
        public string? NormalizedEmail { get; set; }

        [JsonPropertyName("password_hash")]
        public string? PasswordHash { get; set; }

        [JsonPropertyName("password_hash_format")]
        public string? PasswordHashFormat { get; set; }

        [JsonPropertyName("first_name")]
        public string? FirstName { get; set; }

        [JsonPropertyName("last_name")]
        public string? LastName { get; set; }

        [JsonPropertyName("display_name")]
        public string? DisplayName { get; set; }

        [JsonPropertyName("mobile_number")]
        public string? MobileNumber { get; set; }

        [JsonPropertyName("last_login_at")]
        public DateTimeOffset? LastLoginAt { get; set; }

        [JsonPropertyName("profile_image_url")]
        public string? ProfileImageUrl { get; set; }

        [JsonPropertyName("profile_image_object_key")]
        public string? ProfileImageObjectKey { get; set; }

        [JsonPropertyName("profile_image_content_type")]
        public string? ProfileImageContentType { get; set; }
    }
}
