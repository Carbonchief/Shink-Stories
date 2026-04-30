using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Shink.Services;

public sealed partial class WordPressMigrationService
{
    private async Task<List<WordPressDiscountCode>> LoadDiscountCodesAsync(
        IReadOnlyDictionary<int, string> membershipLevels,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenWordPressConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            select
                c.id,
                c.code,
                c.starts,
                c.expires,
                c.uses,
                c.one_use_per_user,
                l.level_id,
                l.initial_payment,
                l.billing_amount,
                l.cycle_number,
                l.cycle_period,
                l.billing_limit,
                l.trial_amount,
                l.trial_limit,
                l.expiration_number,
                l.expiration_period
            from {Table("pmpro_discount_codes")} c
            left join {Table("pmpro_discount_codes_levels")} l on l.code_id = c.id
            order by c.id asc, l.level_id asc;
            """;

        var rows = new List<WordPressDiscountCode>();
        var rowsById = new Dictionary<long, WordPressDiscountCode>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var discountCodeId = ReadInt64(reader, "id");
            if (!rowsById.TryGetValue(discountCodeId, out var row))
            {
                row = new WordPressDiscountCode(
                    DiscountCodeId: discountCodeId,
                    Code: ReadTrimmedString(reader, "code") ?? string.Empty,
                    StartsAt: ReadNullableDateTime(reader, "starts"),
                    ExpiresAt: ReadNullableDateTime(reader, "expires"),
                    MaxUses: ReadInt32(reader, "uses"),
                    OneUsePerUser: ReadInt32(reader, "one_use_per_user") != 0,
                    Tiers: []);
                rowsById[discountCodeId] = row;
                rows.Add(row);
            }

            var levelId = ReadNullableInt32(reader, "level_id");
            if (!levelId.HasValue || levelId.Value <= 0)
            {
                continue;
            }

            membershipLevels.TryGetValue(levelId.Value, out var membershipLevelName);
            row.Tiers.Add(new WordPressDiscountCodeTier(
                TierCode: ResolveTierCode(membershipLevelName),
                SourceLevelId: levelId.Value,
                SourceMembershipLevelName: membershipLevelName,
                InitialPaymentZar: ReadNullableDecimal(reader, "initial_payment") ?? 0,
                BillingAmountZar: ReadNullableDecimal(reader, "billing_amount") ?? 0,
                CycleNumber: ReadInt32(reader, "cycle_number"),
                CyclePeriod: ReadTrimmedString(reader, "cycle_period"),
                BillingLimit: ReadNullableInt32(reader, "billing_limit"),
                TrialAmountZar: ReadNullableDecimal(reader, "trial_amount") ?? 0,
                TrialLimit: ReadInt32(reader, "trial_limit"),
                ExpirationNumber: ReadNullableInt32(reader, "expiration_number"),
                ExpirationPeriod: ReadTrimmedString(reader, "expiration_period")));
        }

        return rows;
    }

    private async Task<List<WordPressGroupDiscountCode>> LoadGroupDiscountCodesAsync(
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenWordPressConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            select
                g.id,
                g.code,
                g.code_parent,
                parent.code as parent_code,
                g.order_id,
                o.user_id,
                lower(trim(u.user_email)) as normalized_email,
                o.timestamp as order_timestamp
            from {Table("pmpro_group_discount_codes")} g
            left join {Table("pmpro_discount_codes")} parent on parent.id = g.code_parent
            left join {Table("pmpro_membership_orders")} o on o.id = g.order_id
            left join {Table("users")} u on u.ID = o.user_id
            order by g.id asc;
            """;

        var rows = new List<WordPressGroupDiscountCode>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new WordPressGroupDiscountCode(
                GroupCodeId: ReadInt64(reader, "id"),
                Code: ReadTrimmedString(reader, "code"),
                ParentDiscountCodeId: ReadInt64(reader, "code_parent"),
                ParentCode: ReadTrimmedString(reader, "parent_code"),
                OrderId: ReadInt64(reader, "order_id"),
                WordPressUserId: ReadNullableInt64(reader, "user_id"),
                Email: ReadTrimmedString(reader, "normalized_email"),
                OrderTimestamp: ReadNullableDateTime(reader, "order_timestamp")));
        }

        return rows;
    }

    private async Task<List<WordPressDiscountCodeUse>> LoadDiscountCodeUsesAsync(
        IReadOnlyDictionary<int, string> membershipLevels,
        IReadOnlyDictionary<long, WordPressGroupDiscountCode> groupCodesByOrderId,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenWordPressConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            select
                dcu.id,
                dcu.code_id,
                dcu.user_id,
                dcu.order_id,
                dcu.timestamp,
                lower(trim(u.user_email)) as normalized_email,
                o.membership_id
            from {Table("pmpro_discount_codes_uses")} dcu
            left join {Table("users")} u on u.ID = dcu.user_id
            left join {Table("pmpro_membership_orders")} o on o.id = dcu.order_id
            order by dcu.id asc;
            """;

        var rows = new List<WordPressDiscountCodeUse>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var orderId = ReadInt64(reader, "order_id");
            var membershipLevelId = ReadNullableInt32(reader, "membership_id");
            membershipLevels.TryGetValue(membershipLevelId ?? 0, out var membershipLevelName);
            groupCodesByOrderId.TryGetValue(orderId, out var groupCode);

            rows.Add(new WordPressDiscountCodeUse(
                RedemptionId: ReadInt64(reader, "id"),
                SourceDiscountCodeId: ReadInt64(reader, "code_id"),
                SourceGroupCodeId: groupCode?.GroupCodeId,
                SourceOrderId: orderId,
                WordPressUserId: ReadInt64(reader, "user_id"),
                Email: ReadTrimmedString(reader, "normalized_email") ?? string.Empty,
                TierCode: ResolveTierCode(membershipLevelName),
                RedeemedAt: ReadNullableDateTime(reader, "timestamp"),
                AccessExpiresAt: null));
        }

        return rows;
    }

    private static IReadOnlyList<SubscriptionDiscountCodeImportRow> BuildDiscountCodeImportRows(
        IReadOnlyList<WordPressDiscountCode> discountCodes,
        IReadOnlyList<WordPressGroupDiscountCode> groupCodes)
    {
        var rows = new List<SubscriptionDiscountCodeImportRow>(discountCodes.Count + groupCodes.Count);

        rows.AddRange(discountCodes.Select(code => new SubscriptionDiscountCodeImportRow(
            Code: code.Code,
            DisplayName: code.Code,
            Description: null,
            IsGroupCode: false,
            StartsAt: code.StartsAt,
            ExpiresAt: code.ExpiresAt,
            MaxUses: Math.Max(0, code.MaxUses),
            OneUsePerUser: code.OneUsePerUser,
            BypassPayment: true,
            IsActive: true,
            SourceSystem: "wordpress_pmpro",
            SourceDiscountCodeId: code.DiscountCodeId,
            SourceGroupCodeId: null,
            SourceParentDiscountCodeId: null,
            SourceOrderId: null,
            RawSource: JsonSerializer.SerializeToElement(code, SerializerOptions))));

        rows.AddRange(groupCodes.Select(groupCode => new SubscriptionDiscountCodeImportRow(
            Code: groupCode.Code,
            DisplayName: groupCode.Code ?? groupCode.ParentCode,
            Description: null,
            IsGroupCode: true,
            StartsAt: discountCodes.FirstOrDefault(code => code.DiscountCodeId == groupCode.ParentDiscountCodeId)?.StartsAt,
            ExpiresAt: discountCodes.FirstOrDefault(code => code.DiscountCodeId == groupCode.ParentDiscountCodeId)?.ExpiresAt,
            MaxUses: 1,
            OneUsePerUser: true,
            BypassPayment: true,
            IsActive: true,
            SourceSystem: "wordpress_pmpro",
            SourceDiscountCodeId: null,
            SourceGroupCodeId: groupCode.GroupCodeId,
            SourceParentDiscountCodeId: groupCode.ParentDiscountCodeId,
            SourceOrderId: groupCode.OrderId > 0 ? groupCode.OrderId : null,
            RawSource: JsonSerializer.SerializeToElement(groupCode, SerializerOptions))));

        return rows;
    }

    private static IReadOnlyList<SubscriptionDiscountCodeTierImportRow> BuildDiscountCodeTierImportRows(
        IReadOnlyList<WordPressDiscountCode> discountCodes,
        IReadOnlyList<WordPressGroupDiscountCode> groupCodes)
    {
        var rows = new List<SubscriptionDiscountCodeTierImportRow>();
        var parentLookup = discountCodes.ToDictionary(code => code.DiscountCodeId);

        foreach (var code in discountCodes)
        {
            rows.AddRange(code.Tiers
                .Where(tier => !string.IsNullOrWhiteSpace(tier.TierCode))
                .Select(tier => new SubscriptionDiscountCodeTierImportRow(
                    SourceDiscountCodeId: code.DiscountCodeId,
                    SourceGroupCodeId: null,
                    TierCode: tier.TierCode,
                    InitialPaymentZar: tier.InitialPaymentZar,
                    BillingAmountZar: tier.BillingAmountZar,
                    CycleNumber: tier.CycleNumber,
                    CyclePeriod: tier.CyclePeriod,
                    BillingLimit: tier.BillingLimit,
                    TrialAmountZar: tier.TrialAmountZar,
                    TrialLimit: tier.TrialLimit,
                    ExpirationNumber: tier.ExpirationNumber,
                    ExpirationPeriod: tier.ExpirationPeriod,
                    SourceLevelId: tier.SourceLevelId,
                    SourceMembershipLevelName: tier.SourceMembershipLevelName)));
        }

        foreach (var groupCode in groupCodes)
        {
            if (!parentLookup.TryGetValue(groupCode.ParentDiscountCodeId, out var parentCode))
            {
                continue;
            }

            rows.AddRange(parentCode.Tiers
                .Where(tier => !string.IsNullOrWhiteSpace(tier.TierCode))
                .Select(tier => new SubscriptionDiscountCodeTierImportRow(
                    SourceDiscountCodeId: null,
                    SourceGroupCodeId: groupCode.GroupCodeId,
                    TierCode: tier.TierCode,
                    InitialPaymentZar: tier.InitialPaymentZar,
                    BillingAmountZar: tier.BillingAmountZar,
                    CycleNumber: tier.CycleNumber,
                    CyclePeriod: tier.CyclePeriod,
                    BillingLimit: tier.BillingLimit,
                    TrialAmountZar: tier.TrialAmountZar,
                    TrialLimit: tier.TrialLimit,
                    ExpirationNumber: tier.ExpirationNumber,
                    ExpirationPeriod: tier.ExpirationPeriod,
                    SourceLevelId: tier.SourceLevelId,
                    SourceMembershipLevelName: tier.SourceMembershipLevelName)));
        }

        return rows;
    }

    private static IReadOnlyList<SubscriptionDiscountCodeRedemptionImportRow> BuildDiscountCodeRedemptionImportRows(
        IReadOnlyList<WordPressDiscountCodeUse> uses) =>
        uses
            .Where(use => !string.IsNullOrWhiteSpace(use.Email))
            .Where(use => use.RedeemedAt.HasValue)
            .Select(use => new SubscriptionDiscountCodeRedemptionImportRow(
                SourceRedemptionId: use.RedemptionId,
                SourceDiscountCodeId: use.SourceGroupCodeId.HasValue ? null : use.SourceDiscountCodeId,
                SourceGroupCodeId: use.SourceGroupCodeId,
                SourceOrderId: use.SourceOrderId > 0 ? use.SourceOrderId : null,
                SourceWordPressUserId: use.WordPressUserId,
                Email: use.Email,
                TierCode: use.TierCode,
                RedeemedAt: use.RedeemedAt,
                AccessExpiresAt: use.AccessExpiresAt,
                BypassedPayment: true,
                Metadata: JsonSerializer.SerializeToElement(use, SerializerOptions)))
            .ToArray();

    private async Task<int> ImportWordPressSubscriptionDiscountCodesBatchAsync(
        Uri baseUri,
        IReadOnlyList<SubscriptionDiscountCodeImportRow> batch,
        CancellationToken cancellationToken)
    {
        using var request = CreateSupabaseJsonRequest(
            HttpMethod.Post,
            new Uri(baseUri, "rest/v1/rpc/import_wordpress_subscription_discount_codes"),
            new { payload = batch });
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        return await ReadRpcCountResponseAsync(response, "WordPress subscription discount code import", cancellationToken);
    }

    private async Task<int> ImportWordPressSubscriptionDiscountCodeTiersBatchAsync(
        Uri baseUri,
        IReadOnlyList<SubscriptionDiscountCodeTierImportRow> batch,
        CancellationToken cancellationToken)
    {
        using var request = CreateSupabaseJsonRequest(
            HttpMethod.Post,
            new Uri(baseUri, "rest/v1/rpc/import_wordpress_subscription_discount_code_tiers"),
            new { payload = batch });
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        return await ReadRpcCountResponseAsync(response, "WordPress subscription discount code tier import", cancellationToken);
    }

    private async Task<int> ImportWordPressSubscriptionDiscountCodeRedemptionsBatchAsync(
        Uri baseUri,
        IReadOnlyList<SubscriptionDiscountCodeRedemptionImportRow> batch,
        CancellationToken cancellationToken)
    {
        using var request = CreateSupabaseJsonRequest(
            HttpMethod.Post,
            new Uri(baseUri, "rest/v1/rpc/import_wordpress_subscription_discount_code_redemptions"),
            new { payload = batch });
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        return await ReadRpcCountResponseAsync(response, "WordPress subscription discount code redemption import", cancellationToken);
    }

    private sealed record WordPressDiscountCode(
        long DiscountCodeId,
        string Code,
        DateTimeOffset? StartsAt,
        DateTimeOffset? ExpiresAt,
        int MaxUses,
        bool OneUsePerUser,
        List<WordPressDiscountCodeTier> Tiers);

    private sealed record WordPressDiscountCodeTier(
        string TierCode,
        int SourceLevelId,
        string? SourceMembershipLevelName,
        decimal InitialPaymentZar,
        decimal BillingAmountZar,
        int CycleNumber,
        string? CyclePeriod,
        int? BillingLimit,
        decimal TrialAmountZar,
        int TrialLimit,
        int? ExpirationNumber,
        string? ExpirationPeriod);

    private sealed record WordPressGroupDiscountCode(
        long GroupCodeId,
        string? Code,
        long ParentDiscountCodeId,
        string? ParentCode,
        long OrderId,
        long? WordPressUserId,
        string? Email,
        DateTimeOffset? OrderTimestamp);

    private sealed record WordPressDiscountCodeUse(
        long RedemptionId,
        long SourceDiscountCodeId,
        long? SourceGroupCodeId,
        long SourceOrderId,
        long WordPressUserId,
        string Email,
        string TierCode,
        DateTimeOffset? RedeemedAt,
        DateTimeOffset? AccessExpiresAt);

    private sealed record SubscriptionDiscountCodeImportRow(
        [property: JsonPropertyName("code")] string? Code,
        [property: JsonPropertyName("display_name")] string? DisplayName,
        [property: JsonPropertyName("description")] string? Description,
        [property: JsonPropertyName("is_group_code")] bool IsGroupCode,
        [property: JsonPropertyName("starts_at")] DateTimeOffset? StartsAt,
        [property: JsonPropertyName("expires_at")] DateTimeOffset? ExpiresAt,
        [property: JsonPropertyName("max_uses")] int MaxUses,
        [property: JsonPropertyName("one_use_per_user")] bool OneUsePerUser,
        [property: JsonPropertyName("bypass_payment")] bool BypassPayment,
        [property: JsonPropertyName("is_active")] bool IsActive,
        [property: JsonPropertyName("source_system")] string SourceSystem,
        [property: JsonPropertyName("source_discount_code_id")] long? SourceDiscountCodeId,
        [property: JsonPropertyName("source_group_code_id")] long? SourceGroupCodeId,
        [property: JsonPropertyName("source_parent_discount_code_id")] long? SourceParentDiscountCodeId,
        [property: JsonPropertyName("source_order_id")] long? SourceOrderId,
        [property: JsonPropertyName("raw_source")] JsonElement RawSource);

    private sealed record SubscriptionDiscountCodeTierImportRow(
        [property: JsonPropertyName("source_discount_code_id")] long? SourceDiscountCodeId,
        [property: JsonPropertyName("source_group_code_id")] long? SourceGroupCodeId,
        [property: JsonPropertyName("tier_code")] string TierCode,
        [property: JsonPropertyName("initial_payment_zar")] decimal InitialPaymentZar,
        [property: JsonPropertyName("billing_amount_zar")] decimal BillingAmountZar,
        [property: JsonPropertyName("cycle_number")] int CycleNumber,
        [property: JsonPropertyName("cycle_period")] string? CyclePeriod,
        [property: JsonPropertyName("billing_limit")] int? BillingLimit,
        [property: JsonPropertyName("trial_amount_zar")] decimal TrialAmountZar,
        [property: JsonPropertyName("trial_limit")] int TrialLimit,
        [property: JsonPropertyName("expiration_number")] int? ExpirationNumber,
        [property: JsonPropertyName("expiration_period")] string? ExpirationPeriod,
        [property: JsonPropertyName("source_level_id")] int SourceLevelId,
        [property: JsonPropertyName("source_membership_level_name")] string? SourceMembershipLevelName);

    private sealed record SubscriptionDiscountCodeRedemptionImportRow(
        [property: JsonPropertyName("source_redemption_id")] long SourceRedemptionId,
        [property: JsonPropertyName("source_discount_code_id")] long? SourceDiscountCodeId,
        [property: JsonPropertyName("source_group_code_id")] long? SourceGroupCodeId,
        [property: JsonPropertyName("source_order_id")] long? SourceOrderId,
        [property: JsonPropertyName("source_wordpress_user_id")] long SourceWordPressUserId,
        [property: JsonPropertyName("email")] string Email,
        [property: JsonPropertyName("tier_code")] string TierCode,
        [property: JsonPropertyName("redeemed_at")] DateTimeOffset? RedeemedAt,
        [property: JsonPropertyName("access_expires_at")] DateTimeOffset? AccessExpiresAt,
        [property: JsonPropertyName("bypassed_payment")] bool BypassedPayment,
        [property: JsonPropertyName("metadata")] JsonElement Metadata);
}
