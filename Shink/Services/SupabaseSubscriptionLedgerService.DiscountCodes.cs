using System.Text.Json;
using System.Text.Json.Serialization;

namespace Shink.Services;

public sealed partial class SupabaseSubscriptionLedgerService
{
    private const string SubscriptionCodeBypassSettingKey = "subscription_code_signup_bypass_enabled";
    private const string DiscountCodeSourceSystem = "discount_code";

    public async Task<SubscriptionCodeSignupPreviewResult> PreviewSignupDiscountCodeAsync(
        string? code,
        string? selectedTierCode,
        CancellationToken cancellationToken = default)
    {
        var normalizedCode = NormalizeDiscountCodeInput(code);
        if (normalizedCode is null)
        {
            return new SubscriptionCodeSignupPreviewResult(false, "Voer asseblief 'n geldige kode in.");
        }

        var resolution = await ResolveDiscountCodeSelectionAsync(
            normalizedCode,
            NormalizeDiscountTierCode(selectedTierCode),
            email: null,
            nowUtc: DateTimeOffset.UtcNow,
            cancellationToken);

        if (!resolution.IsSuccess || resolution.Code is null || resolution.Mapping is null)
        {
            return new SubscriptionCodeSignupPreviewResult(
                false,
                resolution.ErrorMessage ?? "Kode kon nie gevalideer word nie.",
                TierOptions: resolution.TierOptions);
        }

        return new SubscriptionCodeSignupPreviewResult(
            true,
            ResolvedTierCode: resolution.Mapping.TierCode,
            ResolvedTierName: resolution.TierName,
            AccessEndsAtUtc: ResolveDiscountCodeAccessEndsAt(DateTimeOffset.UtcNow, resolution.Mapping),
            CodeExpiresAtUtc: resolution.Code.ExpiresAt,
            BypassesPayment: resolution.Code.BypassPayment,
            TierOptions: resolution.TierOptions);
    }

    public async Task<SubscriptionCodeApplicationResult> ApplySignupDiscountCodeAsync(
        string? email,
        string? code,
        string? selectedTierCode,
        CancellationToken cancellationToken = default)
    {
        var normalizedEmail = NormalizeEmail(email);
        if (normalizedEmail is null)
        {
            return new SubscriptionCodeApplicationResult(false, "Gebruik asseblief 'n geldige e-posadres.");
        }

        var normalizedCode = NormalizeDiscountCodeInput(code);
        if (normalizedCode is null)
        {
            return new SubscriptionCodeApplicationResult(false, "Voer asseblief 'n geldige kode in.");
        }

        var nowUtc = DateTimeOffset.UtcNow;
        var resolution = await ResolveDiscountCodeSelectionAsync(
            normalizedCode,
            NormalizeDiscountTierCode(selectedTierCode),
            normalizedEmail,
            nowUtc,
            cancellationToken);

        if (!resolution.IsSuccess || resolution.Code is null || resolution.Mapping is null)
        {
            return new SubscriptionCodeApplicationResult(false, resolution.ErrorMessage ?? "Kode kon nie toegepas word nie.");
        }

        var context = await TryResolveSelfServiceContextAsync(normalizedEmail, cancellationToken);
        if (context is null)
        {
            return new SubscriptionCodeApplicationResult(false, "Kon nie jou intekenaarprofiel vind nie.");
        }

        var accessEndsAtUtc = ResolveDiscountCodeAccessEndsAt(nowUtc, resolution.Mapping);
        string? grantedSubscriptionId = null;
        if (!string.Equals(resolution.Mapping.TierCode, GratisTierCode, StringComparison.OrdinalIgnoreCase))
        {
            var providerPaymentId = $"discount-code-{Guid.NewGuid():N}";
            grantedSubscriptionId = await UpsertDiscountCodeSubscriptionAsync(
                context.BaseUri,
                context.ApiKey,
                context.SubscriberId,
                resolution.Mapping.TierCode,
                providerPaymentId,
                nowUtc,
                accessEndsAtUtc,
                cancellationToken);
            if (string.IsNullOrWhiteSpace(grantedSubscriptionId))
            {
                return new SubscriptionCodeApplicationResult(false, "Kon nie kode-toegang nou aktiveer nie.");
            }
        }

        var redemptionStored = await InsertDiscountCodeRedemptionAsync(
            context.BaseUri,
            context.ApiKey,
            resolution.Code.DiscountCodeId,
            context.SubscriberId,
            normalizedEmail,
            resolution.Mapping.TierCode,
            nowUtc,
            accessEndsAtUtc,
            grantedSubscriptionId,
            cancellationToken);
        if (!redemptionStored)
        {
            _logger.LogWarning(
                "Signup discount code redemption history insert failed after access grant. email={Email} code={Code} tier={TierCode}",
                normalizedEmail,
                resolution.Code.Code,
                resolution.Mapping.TierCode);
        }

        return new SubscriptionCodeApplicationResult(
            true,
            TierCode: resolution.Mapping.TierCode,
            AccessEndsAtUtc: accessEndsAtUtc);
    }

    private async Task<DiscountCodeSelectionResolution> ResolveDiscountCodeSelectionAsync(
        string normalizedCode,
        string? selectedTierCode,
        string? email,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        if (!TryBuildSupabaseBaseUri(out var baseUri))
        {
            return new DiscountCodeSelectionResolution(false, ErrorMessage: "Supabase URL is not configured.");
        }

        var apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new DiscountCodeSelectionResolution(false, ErrorMessage: "Supabase ServiceRoleKey is not configured.");
        }

        var bypassEnabled = await IsSignupCodeBypassEnabledAsync(baseUri, apiKey, cancellationToken);
        if (!bypassEnabled)
        {
            return new DiscountCodeSelectionResolution(false, ErrorMessage: "Kodebetalings is tans afgeskakel.");
        }

        var code = await FetchDiscountCodeByNormalizedCodeAsync(baseUri, apiKey, normalizedCode, cancellationToken);
        if (code is null)
        {
            return new DiscountCodeSelectionResolution(false, ErrorMessage: "Daardie kode bestaan nie of is nie beskikbaar nie.");
        }

        if (!code.IsActive)
        {
            return new DiscountCodeSelectionResolution(false, ErrorMessage: "Hierdie kode is nie aktief nie.");
        }

        if (code.StartsAt is not null && code.StartsAt > nowUtc)
        {
            return new DiscountCodeSelectionResolution(false, ErrorMessage: "Hierdie kode is nog nie beskikbaar nie.");
        }

        if (code.ExpiresAt is not null && code.ExpiresAt < nowUtc)
        {
            return new DiscountCodeSelectionResolution(false, ErrorMessage: "Hierdie kode het verval.");
        }

        if (!code.BypassPayment)
        {
            return new DiscountCodeSelectionResolution(false, ErrorMessage: "Hierdie kode kan nie nou betaling omseil nie.");
        }

        var mappings = await FetchDiscountCodeTierMappingsAsync(baseUri, apiKey, code.DiscountCodeId, cancellationToken);
        if (mappings.Count == 0)
        {
            return new DiscountCodeSelectionResolution(false, ErrorMessage: "Hierdie kode het nie 'n intekeningsoort nie.");
        }

        var tierNameLookup = await FetchTierNameLookupAsync(baseUri, apiKey, mappings.Select(row => row.TierCode).ToArray(), cancellationToken);
        var tierOptions = mappings
            .Where(mapping => !string.IsNullOrWhiteSpace(mapping.TierCode))
            .Select(mapping => new SubscriptionCodeTierOption(
                mapping.TierCode,
                tierNameLookup.GetValueOrDefault(mapping.TierCode, mapping.TierCode)))
            .DistinctBy(option => option.TierCode, StringComparer.OrdinalIgnoreCase)
            .OrderBy(option => option.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var redemptionCount = await FetchDiscountCodeRedemptionCountAsync(baseUri, apiKey, code.DiscountCodeId, cancellationToken);
        if (code.MaxUses > 0 && redemptionCount >= code.MaxUses)
        {
            return new DiscountCodeSelectionResolution(false, ErrorMessage: "Hierdie kode het sy maksimum gebruike bereik.", TierOptions: tierOptions);
        }

        if (email is not null)
        {
            var emailUses = await FetchDiscountCodeRedemptionsForEmailAsync(baseUri, apiKey, code.DiscountCodeId, email, cancellationToken);
            if (code.OneUsePerUser && emailUses.Count > 0)
            {
                return new DiscountCodeSelectionResolution(false, ErrorMessage: "Hierdie kode is reeds vir hierdie e-pos gebruik.", TierOptions: tierOptions);
            }
        }

        var resolvedMapping = ResolveSelectedTierMapping(mappings, selectedTierCode);
        if (resolvedMapping is null)
        {
            return new DiscountCodeSelectionResolution(
                false,
                ErrorMessage: "Kies asseblief 'n geldige intekeningsoort vir hierdie kode.",
                TierOptions: tierOptions);
        }

        return new DiscountCodeSelectionResolution(
            true,
            Code: code,
            Mapping: resolvedMapping,
            TierName: tierNameLookup.GetValueOrDefault(resolvedMapping.TierCode, resolvedMapping.TierCode),
            TierOptions: tierOptions);
    }

    private static DiscountCodeTierMappingRow? ResolveSelectedTierMapping(
        IReadOnlyList<DiscountCodeTierMappingRow> mappings,
        string? selectedTierCode)
    {
        if (!string.IsNullOrWhiteSpace(selectedTierCode))
        {
            return mappings.FirstOrDefault(mapping =>
                string.Equals(mapping.TierCode, selectedTierCode, StringComparison.OrdinalIgnoreCase));
        }

        var nonGratisMappings = mappings
            .Where(mapping => !string.Equals(mapping.TierCode, GratisTierCode, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (nonGratisMappings.Length == 1)
        {
            return nonGratisMappings[0];
        }

        return mappings.Count == 1 ? mappings[0] : null;
    }

    private static DateTimeOffset? ResolveDiscountCodeAccessEndsAt(
        DateTimeOffset nowUtc,
        DiscountCodeTierMappingRow mapping)
    {
        if (!mapping.ExpirationNumber.HasValue || mapping.ExpirationNumber.Value <= 0)
        {
            return null;
        }

        return NormalizeDiscountPeriod(mapping.ExpirationPeriod) switch
        {
            "hour" => nowUtc.AddHours(mapping.ExpirationNumber.Value),
            "day" => nowUtc.AddDays(mapping.ExpirationNumber.Value),
            "week" => nowUtc.AddDays(mapping.ExpirationNumber.Value * 7d),
            "month" => nowUtc.AddMonths(mapping.ExpirationNumber.Value),
            "year" => nowUtc.AddYears(mapping.ExpirationNumber.Value),
            _ => null
        };
    }

    private static string? NormalizeDiscountCodeInput(string? code)
    {
        var normalized = code?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized.ToLowerInvariant();
    }

    private static string? NormalizeDiscountTierCode(string? tierCode)
    {
        var normalized = tierCode?.Trim().ToLowerInvariant();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static string? NormalizeEmail(string? email)
    {
        var normalized = email?.Trim().ToLowerInvariant();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static string NormalizeDiscountPeriod(string? period) =>
        period?.Trim().ToLowerInvariant() switch
        {
            "hour" or "hours" => "hour",
            "day" or "days" => "day",
            "week" or "weeks" => "week",
            "month" or "months" => "month",
            "year" or "years" => "year",
            _ => string.Empty
        };

    private async Task<bool> IsSignupCodeBypassEnabledAsync(Uri baseUri, string apiKey, CancellationToken cancellationToken)
    {
        var uri = new Uri(
            baseUri,
            $"rest/v1/site_settings?setting_key=eq.{Uri.EscapeDataString(SubscriptionCodeBypassSettingKey)}&select=setting_value&limit=1");
        var rows = await FetchRowsAsync<SiteSettingToggleRow>(uri, apiKey, cancellationToken);
        var row = rows.FirstOrDefault();
        if (row is null)
        {
            return true;
        }

        try
        {
            return row.SettingValue.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String when bool.TryParse(row.SettingValue.GetString(), out var parsed) => parsed,
                _ => true
            };
        }
        catch
        {
            return true;
        }
    }

    private async Task<DiscountCodeRow?> FetchDiscountCodeByNormalizedCodeAsync(
        Uri baseUri,
        string apiKey,
        string normalizedCode,
        CancellationToken cancellationToken)
    {
        var uri = new Uri(
            baseUri,
            "rest/v1/subscription_discount_codes" +
            "?select=discount_code_id,code,normalized_code,starts_at,expires_at,max_uses,one_use_per_user,bypass_payment,is_active" +
            $"&normalized_code=eq.{Uri.EscapeDataString(normalizedCode)}&limit=1");
        return (await FetchRowsAsync<DiscountCodeRow>(uri, apiKey, cancellationToken)).FirstOrDefault();
    }

    private async Task<IReadOnlyList<DiscountCodeTierMappingRow>> FetchDiscountCodeTierMappingsAsync(
        Uri baseUri,
        string apiKey,
        Guid discountCodeId,
        CancellationToken cancellationToken)
    {
        var escapedCodeId = Uri.EscapeDataString(discountCodeId.ToString("D"));
        var uri = new Uri(
            baseUri,
            "rest/v1/subscription_discount_code_tiers" +
            "?select=tier_code,expiration_number,expiration_period" +
            $"&discount_code_id=eq.{escapedCodeId}&limit=100");
        return await FetchRowsAsync<DiscountCodeTierMappingRow>(uri, apiKey, cancellationToken);
    }

    private async Task<IReadOnlyDictionary<string, string>> FetchTierNameLookupAsync(
        Uri baseUri,
        string apiKey,
        IReadOnlyList<string> tierCodes,
        CancellationToken cancellationToken)
    {
        var normalizedTierCodes = tierCodes
            .Where(tierCode => !string.IsNullOrWhiteSpace(tierCode))
            .Select(tierCode => tierCode.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (normalizedTierCodes.Length == 0)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var filter = string.Join(",", normalizedTierCodes.Select(Uri.EscapeDataString));
        var uri = new Uri(
            baseUri,
            "rest/v1/subscription_tiers" +
            "?select=tier_code,display_name" +
            $"&tier_code=in.({filter})&limit={normalizedTierCodes.Length}");
        var rows = await FetchRowsAsync<TierNameLookupRow>(uri, apiKey, cancellationToken);
        return rows
            .Where(row => !string.IsNullOrWhiteSpace(row.TierCode))
            .ToDictionary(
                row => row.TierCode!.Trim(),
                row => string.IsNullOrWhiteSpace(row.DisplayName) ? row.TierCode!.Trim() : row.DisplayName.Trim(),
                StringComparer.OrdinalIgnoreCase);
    }

    private async Task<int> FetchDiscountCodeRedemptionCountAsync(
        Uri baseUri,
        string apiKey,
        Guid discountCodeId,
        CancellationToken cancellationToken)
    {
        var escapedCodeId = Uri.EscapeDataString(discountCodeId.ToString("D"));
        var uri = new Uri(
            baseUri,
            "rest/v1/subscription_discount_code_redemptions" +
            "?select=redemption_id" +
            $"&discount_code_id=eq.{escapedCodeId}&limit=5000");
        return (await FetchRowsAsync<RedemptionLookupRow>(uri, apiKey, cancellationToken)).Count;
    }

    private async Task<IReadOnlyList<RedemptionLookupRow>> FetchDiscountCodeRedemptionsForEmailAsync(
        Uri baseUri,
        string apiKey,
        Guid discountCodeId,
        string email,
        CancellationToken cancellationToken)
    {
        var escapedCodeId = Uri.EscapeDataString(discountCodeId.ToString("D"));
        var escapedEmail = Uri.EscapeDataString(email.Trim().ToLowerInvariant());
        var uri = new Uri(
            baseUri,
            "rest/v1/subscription_discount_code_redemptions" +
            "?select=redemption_id" +
            $"&discount_code_id=eq.{escapedCodeId}&email=eq.{escapedEmail}&limit=100");
        return await FetchRowsAsync<RedemptionLookupRow>(uri, apiKey, cancellationToken);
    }

    private async Task<string?> UpsertDiscountCodeSubscriptionAsync(
        Uri baseUri,
        string apiKey,
        string subscriberId,
        string tierCode,
        string providerPaymentId,
        DateTimeOffset subscribedAtUtc,
        DateTimeOffset? accessEndsAtUtc,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            subscriber_id = subscriberId,
            tier_code = tierCode,
            provider = "free",
            provider_payment_id = providerPaymentId,
            provider_transaction_id = (string?)null,
            provider_token = (string?)null,
            provider_email_token = (string?)null,
            status = "active",
            subscribed_at = subscribedAtUtc.UtcDateTime,
            next_renewal_at = accessEndsAtUtc?.UtcDateTime,
            cancelled_at = (DateTime?)null,
            source_system = DiscountCodeSourceSystem
        };

        var uri = new Uri(baseUri, "rest/v1/subscriptions?select=subscription_id");
        using var request = CreateJsonRequest(HttpMethod.Post, uri, apiKey, payload, "return=representation");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "Discount code subscription create failed. subscriber_id={SubscriberId} tier={TierCode} Status={StatusCode} Body={Body}",
                subscriberId,
                tierCode,
                (int)response.StatusCode,
                body);
            return null;
        }

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        return ReadFirstStringProperty(responseBody, "subscription_id");
    }

    private async Task<bool> InsertDiscountCodeRedemptionAsync(
        Uri baseUri,
        string apiKey,
        Guid discountCodeId,
        string subscriberId,
        string email,
        string tierCode,
        DateTimeOffset redeemedAtUtc,
        DateTimeOffset? accessEndsAtUtc,
        string? grantedSubscriptionId,
        CancellationToken cancellationToken)
    {
        var payload = new[]
        {
            new
            {
                discount_code_id = discountCodeId,
                subscriber_id = subscriberId,
                email,
                tier_code = tierCode,
                redeemed_at = redeemedAtUtc.UtcDateTime,
                access_expires_at = accessEndsAtUtc?.UtcDateTime,
                granted_subscription_id = string.IsNullOrWhiteSpace(grantedSubscriptionId) ? null : grantedSubscriptionId,
                source_system = "shink_app",
                bypassed_payment = true
            }
        };

        var uri = new Uri(baseUri, "rest/v1/subscription_discount_code_redemptions");
        using var request = CreateJsonRequest(HttpMethod.Post, uri, apiKey, payload, "return=minimal");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return true;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        _logger.LogWarning(
            "Discount code redemption insert failed. email={Email} discount_code_id={DiscountCodeId} Status={StatusCode} Body={Body}",
            email,
            discountCodeId,
            (int)response.StatusCode,
            body);
        return false;
    }

    private async Task<IReadOnlyList<T>> FetchRowsAsync<T>(Uri uri, string apiKey, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Get, uri, apiKey);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "Supabase fetch failed. uri={Uri} Status={StatusCode} Body={Body}",
                uri,
                (int)response.StatusCode,
                body);
            return [];
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<List<T>>(stream, cancellationToken: cancellationToken) ?? [];
    }

    private sealed record DiscountCodeSelectionResolution(
        bool IsSuccess,
        DiscountCodeRow? Code = null,
        DiscountCodeTierMappingRow? Mapping = null,
        string? TierName = null,
        string? ErrorMessage = null,
        IReadOnlyList<SubscriptionCodeTierOption>? TierOptions = null);

    private sealed class SiteSettingToggleRow
    {
        [JsonPropertyName("setting_value")]
        public JsonElement SettingValue { get; set; }
    }

    private sealed class DiscountCodeRow
    {
        [JsonPropertyName("discount_code_id")]
        public Guid DiscountCodeId { get; set; }

        [JsonPropertyName("code")]
        public string? Code { get; set; }

        [JsonPropertyName("normalized_code")]
        public string? NormalizedCode { get; set; }

        [JsonPropertyName("starts_at")]
        public DateTimeOffset? StartsAt { get; set; }

        [JsonPropertyName("expires_at")]
        public DateTimeOffset? ExpiresAt { get; set; }

        [JsonPropertyName("max_uses")]
        public int MaxUses { get; set; }

        [JsonPropertyName("one_use_per_user")]
        public bool OneUsePerUser { get; set; }

        [JsonPropertyName("bypass_payment")]
        public bool BypassPayment { get; set; }

        [JsonPropertyName("is_active")]
        public bool IsActive { get; set; }
    }

    private sealed class DiscountCodeTierMappingRow
    {
        [JsonPropertyName("tier_code")]
        public string TierCode { get; set; } = string.Empty;

        [JsonPropertyName("expiration_number")]
        public int? ExpirationNumber { get; set; }

        [JsonPropertyName("expiration_period")]
        public string? ExpirationPeriod { get; set; }
    }

    private sealed class TierNameLookupRow
    {
        [JsonPropertyName("tier_code")]
        public string? TierCode { get; set; }

        [JsonPropertyName("display_name")]
        public string? DisplayName { get; set; }
    }

    private sealed class RedemptionLookupRow
    {
        [JsonPropertyName("redemption_id")]
        public Guid RedemptionId { get; set; }
    }
}
