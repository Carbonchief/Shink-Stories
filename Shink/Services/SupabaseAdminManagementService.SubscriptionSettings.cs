using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Shink.Services;

public sealed partial class SupabaseAdminManagementService
{
    public async Task<AdminSubscriptionSettingsSnapshot> GetSubscriptionSettingsAsync(
        string? adminEmail,
        CancellationToken cancellationToken = default)
    {
        if (!await TryResolveAdminContextAsync(adminEmail, cancellationToken))
        {
            return new AdminSubscriptionSettingsSnapshot(false, [], []);
        }

        if (!TryBuildSupabaseBaseUri(out var baseUri))
        {
            return new AdminSubscriptionSettingsSnapshot(false, [], []);
        }

        var apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new AdminSubscriptionSettingsSnapshot(false, [], []);
        }

        var siteSettingsTask = FetchSiteSettingsAsync(baseUri, apiKey, cancellationToken);
        var subscriptionTypesTask = FetchSubscriptionTypeSettingsAsync(baseUri, apiKey, cancellationToken);
        var codeRowsTask = FetchSubscriptionDiscountCodesAsync(baseUri, apiKey, cancellationToken);
        var codeTierRowsTask = FetchSubscriptionDiscountCodeTierRowsAsync(baseUri, apiKey, cancellationToken);
        var codeUseRowsTask = FetchSubscriptionDiscountCodeUseRowsAsync(baseUri, apiKey, cancellationToken);

        await Task.WhenAll(siteSettingsTask, subscriptionTypesTask, codeRowsTask, codeTierRowsTask, codeUseRowsTask);

        var subscriptionTypes = subscriptionTypesTask.Result
            .Where(row => !string.IsNullOrWhiteSpace(row.TierCode))
            .Select(row => new AdminSubscriptionTypeRecord(
                row.TierCode!.Trim(),
                NormalizeOptionalText(row.DisplayName, 120) ?? row.TierCode!.Trim(),
                NormalizeOptionalText(row.Description, 500),
                Math.Max(1, row.BillingPeriodMonths),
                row.PriceZar,
                NormalizeOptionalText(row.PayFastPlanSlug, 120) ?? string.Empty,
                NormalizeOptionalText(row.PaystackPlanCode, 120),
                row.IsActive))
            .OrderBy(row => row.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var tierNameLookup = subscriptionTypes
            .ToDictionary(row => row.TierCode, row => row.DisplayName, StringComparer.OrdinalIgnoreCase);

        var codeRows = codeRowsTask.Result;
        var codeLookup = codeRows
            .Where(row => row.DiscountCodeId != Guid.Empty)
            .ToDictionary(row => row.DiscountCodeId);

        var tierRowsByCode = codeTierRowsTask.Result
            .Where(row => row.DiscountCodeId != Guid.Empty)
            .GroupBy(row => row.DiscountCodeId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<AdminSubscriptionDiscountCodeTierRecord>)group
                    .Where(row => !string.IsNullOrWhiteSpace(row.TierCode))
                    .Select(row => new AdminSubscriptionDiscountCodeTierRecord(
                        row.TierCode!.Trim(),
                        tierNameLookup.GetValueOrDefault(row.TierCode!.Trim(), row.TierCode!.Trim()),
                        row.InitialPaymentZar,
                        row.BillingAmountZar,
                        row.CycleNumber,
                        NormalizeOptionalText(row.CyclePeriod, 24),
                        row.BillingLimit,
                        row.TrialAmountZar,
                        row.TrialLimit,
                        row.ExpirationNumber,
                        NormalizeOptionalText(row.ExpirationPeriod, 24)))
                    .OrderBy(row => row.TierName, StringComparer.OrdinalIgnoreCase)
                    .ToArray());

        var usesByCode = codeUseRowsTask.Result
            .Where(row => row.DiscountCodeId != Guid.Empty)
            .GroupBy(row => row.DiscountCodeId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<AdminSubscriptionDiscountCodeUseRecord>)group
                    .Where(row => !string.IsNullOrWhiteSpace(row.Email))
                    .Select(row => new AdminSubscriptionDiscountCodeUseRecord(
                        row.RedemptionId == Guid.Empty ? null : row.RedemptionId,
                        row.Email!.Trim().ToLowerInvariant(),
                        NormalizeOptionalText(row.TierCode, 80),
                        row.RedeemedAt,
                        row.AccessExpiresAt,
                        NormalizeOptionalText(row.SourceSystem, 40) ?? "shink_app"))
                    .OrderByDescending(row => row.RedeemedAt)
                    .ToArray());

        var groupCodeCounts = codeRows
            .Where(row => row.ParentDiscountCodeId.HasValue && row.ParentDiscountCodeId.Value != Guid.Empty)
            .GroupBy(row => row.ParentDiscountCodeId!.Value)
            .ToDictionary(group => group.Key, group => group.Count());

        var discountCodes = codeRows
            .Where(row => row.DiscountCodeId != Guid.Empty)
            .Select(row =>
            {
                codeLookup.TryGetValue(row.ParentDiscountCodeId ?? Guid.Empty, out var parentRow);
                return new AdminSubscriptionDiscountCodeRecord(
                    row.DiscountCodeId,
                    NormalizeOptionalText(row.Code, 80) ?? string.Empty,
                    NormalizeOptionalText(row.DisplayName, 120),
                    NormalizeOptionalText(row.Description, 1000),
                    row.IsGroupCode,
                    row.ParentDiscountCodeId,
                    NormalizeOptionalText(parentRow?.Code, 80),
                    row.StartsAt,
                    row.ExpiresAt,
                    Math.Max(0, row.MaxUses),
                    row.OneUsePerUser,
                    row.BypassPayment,
                    row.IsActive,
                    NormalizeOptionalText(row.SourceSystem, 40) ?? "shink_app",
                    usesByCode.GetValueOrDefault(row.DiscountCodeId)?.Count ?? 0,
                    groupCodeCounts.GetValueOrDefault(row.DiscountCodeId),
                    tierRowsByCode.GetValueOrDefault(row.DiscountCodeId) ?? [],
                    usesByCode.GetValueOrDefault(row.DiscountCodeId) ?? []);
            })
            .OrderBy(row => row.IsGroupCode)
            .ThenBy(row => row.ParentCode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.Code, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var signupCodeBypassEnabled = ReadBooleanSiteSetting(
            siteSettingsTask.Result,
            "subscription_code_signup_bypass_enabled",
            defaultValue: true);

        return new AdminSubscriptionSettingsSnapshot(
            signupCodeBypassEnabled,
            subscriptionTypes,
            discountCodes);
    }

    public async Task<AdminOperationResult> SaveSubscriptionSettingsAsync(
        string? adminEmail,
        AdminSubscriptionSettingsUpdateRequest request,
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

        var apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new AdminOperationResult(false, "Supabase ServiceRoleKey is nog nie opgestel nie.");
        }

        var payload = new[]
        {
            new
            {
                setting_key = "subscription_code_signup_bypass_enabled",
                setting_value = request.SignupCodeBypassEnabled
            }
        };

        var uri = new Uri(baseUri, "rest/v1/site_settings?on_conflict=setting_key");
        using var saveRequest = CreateJsonRequest(
            HttpMethod.Post,
            uri,
            apiKey,
            payload,
            "resolution=merge-duplicates,return=minimal");
        using var saveResponse = await _httpClient.SendAsync(saveRequest, cancellationToken);
        if (saveResponse.IsSuccessStatusCode)
        {
            return new AdminOperationResult(true);
        }

        var responseBody = await saveResponse.Content.ReadAsStringAsync(cancellationToken);
        _logger.LogWarning(
            "Subscription settings save failed. Status={StatusCode} Body={Body}",
            (int)saveResponse.StatusCode,
            responseBody);
        return new AdminOperationResult(false, "Kon nie intekening-instellings nou stoor nie.");
    }

    public async Task<AdminOperationResult> SaveSubscriptionTypeAsync(
        string? adminEmail,
        AdminSubscriptionTypeSaveRequest request,
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

        var apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new AdminOperationResult(false, "Supabase ServiceRoleKey is nog nie opgestel nie.");
        }

        var normalizedTierCode = NormalizeSubscriptionTierCode(request.TierCode);
        if (normalizedTierCode is null)
        {
            return new AdminOperationResult(false, "Gebruik asseblief 'n geldige tier kode.");
        }

        var normalizedDisplayName = NormalizeOptionalText(request.DisplayName, 120);
        if (string.IsNullOrWhiteSpace(normalizedDisplayName))
        {
            return new AdminOperationResult(false, "Intekening naam is verpligtend.");
        }

        var normalizedPayFastPlanSlug = NormalizeRequiredSlugValue(request.PayFastPlanSlug);
        if (normalizedPayFastPlanSlug is null)
        {
            return new AdminOperationResult(false, "Gebruik asseblief 'n geldige PayFast plan-sleutel.");
        }

        if (request.BillingPeriodMonths <= 0)
        {
            return new AdminOperationResult(false, "Betaalperiode moet groter as nul wees.");
        }

        if (request.PriceZar < 0)
        {
            return new AdminOperationResult(false, "Prys kan nie negatief wees nie.");
        }

        var payload = new[]
        {
            new
            {
                tier_code = normalizedTierCode,
                display_name = normalizedDisplayName,
                description = NormalizeOptionalText(request.Description, 500),
                billing_period_months = request.BillingPeriodMonths,
                price_zar = decimal.Round(request.PriceZar, 2),
                payfast_plan_slug = normalizedPayFastPlanSlug,
                paystack_plan_code = NormalizeOptionalText(request.PaystackPlanCode, 120),
                is_active = request.IsActive
            }
        };

        var uri = new Uri(baseUri, "rest/v1/subscription_tiers?on_conflict=tier_code");
        using var saveRequest = CreateJsonRequest(
            HttpMethod.Post,
            uri,
            apiKey,
            payload,
            "resolution=merge-duplicates,return=minimal");
        using var saveResponse = await _httpClient.SendAsync(saveRequest, cancellationToken);
        if (saveResponse.IsSuccessStatusCode)
        {
            return new AdminOperationResult(true);
        }

        var responseBody = await saveResponse.Content.ReadAsStringAsync(cancellationToken);
        _logger.LogWarning(
            "Subscription type save failed. tier_code={TierCode} Status={StatusCode} Body={Body}",
            normalizedTierCode,
            (int)saveResponse.StatusCode,
            responseBody);
        return new AdminOperationResult(false, "Kon nie intekeningsoort nou stoor nie.");
    }

    public async Task<AdminOperationResult> SaveSubscriptionDiscountCodeAsync(
        string? adminEmail,
        AdminSubscriptionDiscountCodeSaveRequest request,
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

        var apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new AdminOperationResult(false, "Supabase ServiceRoleKey is nog nie opgestel nie.");
        }

        var normalizedCode = NormalizeOptionalText(request.Code, 80);
        if (string.IsNullOrWhiteSpace(normalizedCode))
        {
            return new AdminOperationResult(false, "Kode is verpligtend.");
        }

        if (request.MaxUses < 0)
        {
            return new AdminOperationResult(false, "Maksimum gebruike kan nie negatief wees nie.");
        }

        if (request.IsGroupCode && request.ParentDiscountCodeId is null)
        {
            return new AdminOperationResult(false, "Groepkodes moet aan 'n ouer kode gekoppel wees.");
        }

        var tierMappings = request.TierMappings?
            .Where(mapping => !string.IsNullOrWhiteSpace(mapping.TierCode))
            .GroupBy(mapping => mapping.TierCode.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray()
            ?? [];
        if (tierMappings.Length == 0)
        {
            return new AdminOperationResult(false, "Kies ten minste een intekeningsoort vir die kode.");
        }

        Guid discountCodeId;
        if (request.DiscountCodeId is Guid existingId && existingId != Guid.Empty)
        {
            discountCodeId = existingId;
            var escapedDiscountCodeId = Uri.EscapeDataString(existingId.ToString("D"));
            using var updateRequest = CreateJsonRequest(
                new HttpMethod("PATCH"),
                new Uri(baseUri, $"rest/v1/subscription_discount_codes?discount_code_id=eq.{escapedDiscountCodeId}"),
                apiKey,
                new
                {
                    code = normalizedCode,
                    display_name = NormalizeOptionalText(request.DisplayName, 120),
                    description = NormalizeOptionalText(request.Description, 1000),
                    is_group_code = request.IsGroupCode,
                    parent_discount_code_id = request.ParentDiscountCodeId,
                    starts_at = request.StartsAt?.UtcDateTime,
                    expires_at = request.ExpiresAt?.UtcDateTime,
                    max_uses = request.MaxUses,
                    one_use_per_user = request.OneUsePerUser,
                    bypass_payment = request.BypassPayment,
                    is_active = request.IsActive
                },
                "return=minimal");
            using var updateResponse = await _httpClient.SendAsync(updateRequest, cancellationToken);
            if (!updateResponse.IsSuccessStatusCode)
            {
                var updateBody = await updateResponse.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "Subscription discount code update failed. discount_code_id={DiscountCodeId} Status={StatusCode} Body={Body}",
                    existingId,
                    (int)updateResponse.StatusCode,
                    updateBody);
                return new AdminOperationResult(false, "Kon nie kode nou opdateer nie.");
            }
        }
        else
        {
            using var createRequest = CreateJsonRequest(
                HttpMethod.Post,
                new Uri(baseUri, "rest/v1/subscription_discount_codes?select=discount_code_id"),
                apiKey,
                new[]
                {
                    new
                    {
                        code = normalizedCode,
                        display_name = NormalizeOptionalText(request.DisplayName, 120),
                        description = NormalizeOptionalText(request.Description, 1000),
                        is_group_code = request.IsGroupCode,
                        parent_discount_code_id = request.ParentDiscountCodeId,
                        starts_at = request.StartsAt?.UtcDateTime,
                        expires_at = request.ExpiresAt?.UtcDateTime,
                        max_uses = request.MaxUses,
                        one_use_per_user = request.OneUsePerUser,
                        bypass_payment = request.BypassPayment,
                        is_active = request.IsActive,
                        source_system = "shink_app"
                    }
                },
                "return=representation");
            using var createResponse = await _httpClient.SendAsync(createRequest, cancellationToken);
            if (!createResponse.IsSuccessStatusCode)
            {
                var createBody = await createResponse.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "Subscription discount code create failed. code={Code} Status={StatusCode} Body={Body}",
                    normalizedCode,
                    (int)createResponse.StatusCode,
                    createBody);
                return new AdminOperationResult(false, "Kon nie kode nou skep nie.");
            }

            await using var createStream = await createResponse.Content.ReadAsStreamAsync(cancellationToken);
            var rows = await JsonSerializer.DeserializeAsync<List<DiscountCodeIdRow>>(createStream, JsonOptions, cancellationToken) ?? [];
            discountCodeId = rows.FirstOrDefault()?.DiscountCodeId ?? Guid.Empty;
            if (discountCodeId == Guid.Empty)
            {
                return new AdminOperationResult(false, "Kon nie die nuwe kode ID bevestig nie.");
            }
        }

        var escapedCodeId = Uri.EscapeDataString(discountCodeId.ToString("D"));
        using var deleteTierRequest = CreateRequest(
            HttpMethod.Delete,
            new Uri(baseUri, $"rest/v1/subscription_discount_code_tiers?discount_code_id=eq.{escapedCodeId}"),
            apiKey);
        using var deleteTierResponse = await _httpClient.SendAsync(deleteTierRequest, cancellationToken);
        if (!deleteTierResponse.IsSuccessStatusCode)
        {
            var deleteBody = await deleteTierResponse.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "Subscription discount code tier reset failed. discount_code_id={DiscountCodeId} Status={StatusCode} Body={Body}",
                discountCodeId,
                (int)deleteTierResponse.StatusCode,
                deleteBody);
            return new AdminOperationResult(false, "Kon nie kode se intekeningsoorte nou opdateer nie.");
        }

        var tierPayload = tierMappings
            .Select(mapping => new
            {
                discount_code_id = discountCodeId,
                tier_code = mapping.TierCode.Trim().ToLowerInvariant(),
                initial_payment_zar = mapping.InitialPaymentZar,
                billing_amount_zar = mapping.BillingAmountZar,
                cycle_number = Math.Max(0, mapping.CycleNumber),
                cycle_period = NormalizeOptionalText(mapping.CyclePeriod, 24),
                billing_limit = mapping.BillingLimit,
                trial_amount_zar = mapping.TrialAmountZar,
                trial_limit = Math.Max(0, mapping.TrialLimit),
                expiration_number = mapping.ExpirationNumber,
                expiration_period = NormalizeOptionalText(mapping.ExpirationPeriod, 24)
            })
            .ToArray();

        using var createTierRequest = CreateJsonRequest(
            HttpMethod.Post,
            new Uri(baseUri, "rest/v1/subscription_discount_code_tiers"),
            apiKey,
            tierPayload,
            "return=minimal");
        using var createTierResponse = await _httpClient.SendAsync(createTierRequest, cancellationToken);
        if (createTierResponse.IsSuccessStatusCode)
        {
            return new AdminOperationResult(true, EntityId: discountCodeId);
        }

        var createTierBody = await createTierResponse.Content.ReadAsStringAsync(cancellationToken);
        _logger.LogWarning(
            "Subscription discount code tiers save failed. discount_code_id={DiscountCodeId} Status={StatusCode} Body={Body}",
            discountCodeId,
            (int)createTierResponse.StatusCode,
            createTierBody);
        return new AdminOperationResult(false, "Kon nie kode se intekeningsoorte nou stoor nie.");
    }

    public async Task<AdminOperationResult> ImportWordPressSubscriptionDiscountCodesAsync(
        string? adminEmail,
        CancellationToken cancellationToken = default)
    {
        if (!await TryResolveAdminContextAsync(adminEmail, cancellationToken))
        {
            return new AdminOperationResult(false, "Jy het nie admin toegang nie.");
        }

        var result = await _wordPressMigrationService.SyncAsync(cancellationToken);
        return result.Errors.Count == 0
            ? new AdminOperationResult(true)
            : new AdminOperationResult(false, string.Join(" ", result.Errors));
    }

    private async Task<IReadOnlyList<SiteSettingRow>> FetchSiteSettingsAsync(Uri baseUri, string apiKey, CancellationToken cancellationToken)
    {
        var uri = new Uri(
            baseUri,
            "rest/v1/site_settings" +
            "?select=setting_key,setting_value" +
            "&limit=200");
        return await FetchRowsAsync<SiteSettingRow>(uri, apiKey, cancellationToken);
    }

    private async Task<IReadOnlyList<SubscriptionTypeSettingsRow>> FetchSubscriptionTypeSettingsAsync(
        Uri baseUri,
        string apiKey,
        CancellationToken cancellationToken)
    {
        var uri = new Uri(
            baseUri,
            "rest/v1/subscription_tiers" +
            "?select=tier_code,display_name,description,billing_period_months,price_zar,payfast_plan_slug,paystack_plan_code,is_active" +
            "&order=display_name.asc" +
            "&limit=200");
        return await FetchRowsAsync<SubscriptionTypeSettingsRow>(uri, apiKey, cancellationToken);
    }

    private async Task<IReadOnlyList<SubscriptionDiscountCodeRow>> FetchSubscriptionDiscountCodesAsync(
        Uri baseUri,
        string apiKey,
        CancellationToken cancellationToken)
    {
        var uri = new Uri(
            baseUri,
            "rest/v1/subscription_discount_codes" +
            "?select=discount_code_id,code,display_name,description,is_group_code,parent_discount_code_id,starts_at,expires_at,max_uses,one_use_per_user,bypass_payment,is_active,source_system" +
            "&order=is_group_code.asc" +
            "&order=code.asc" +
            "&limit=5000");
        return await FetchRowsAsync<SubscriptionDiscountCodeRow>(uri, apiKey, cancellationToken);
    }

    private async Task<IReadOnlyList<SubscriptionDiscountCodeTierRow>> FetchSubscriptionDiscountCodeTierRowsAsync(
        Uri baseUri,
        string apiKey,
        CancellationToken cancellationToken)
    {
        var uri = new Uri(
            baseUri,
            "rest/v1/subscription_discount_code_tiers" +
            "?select=discount_code_id,tier_code,initial_payment_zar,billing_amount_zar,cycle_number,cycle_period,billing_limit,trial_amount_zar,trial_limit,expiration_number,expiration_period" +
            "&limit=10000");
        return await FetchRowsAsync<SubscriptionDiscountCodeTierRow>(uri, apiKey, cancellationToken);
    }

    private async Task<IReadOnlyList<SubscriptionDiscountCodeUseRow>> FetchSubscriptionDiscountCodeUseRowsAsync(
        Uri baseUri,
        string apiKey,
        CancellationToken cancellationToken)
    {
        var uri = new Uri(
            baseUri,
            "rest/v1/subscription_discount_code_redemptions" +
            "?select=redemption_id,discount_code_id,email,tier_code,redeemed_at,access_expires_at,source_system" +
            "&order=redeemed_at.desc" +
            "&limit=20000");
        return await FetchRowsAsync<SubscriptionDiscountCodeUseRow>(uri, apiKey, cancellationToken);
    }

    private static bool ReadBooleanSiteSetting(
        IReadOnlyList<SiteSettingRow> settings,
        string settingKey,
        bool defaultValue)
    {
        var row = settings.FirstOrDefault(candidate =>
            string.Equals(candidate.SettingKey, settingKey, StringComparison.OrdinalIgnoreCase));
        if (row?.SettingValue is null)
        {
            return defaultValue;
        }

        try
        {
            return row.SettingValue.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String when bool.TryParse(row.SettingValue.GetString(), out var parsed) => parsed,
                _ => defaultValue
            };
        }
        catch
        {
            return defaultValue;
        }
    }

    private static string? NormalizeSubscriptionTierCode(string? value)
    {
        var normalized = NormalizeOptionalText(value, 80)?.Trim().ToLowerInvariant();
        return string.IsNullOrWhiteSpace(normalized) || !Regex.IsMatch(normalized, "^[a-z0-9_]+$")
            ? null
            : normalized;
    }

    private static string? NormalizeRequiredSlugValue(string? value)
    {
        var normalized = NormalizeOptionalText(value, 120)?.Trim().ToLowerInvariant();
        return string.IsNullOrWhiteSpace(normalized) || !Regex.IsMatch(normalized, "^[a-z0-9-]+$")
            ? null
            : normalized;
    }

    private sealed class SiteSettingRow
    {
        [JsonPropertyName("setting_key")]
        public string? SettingKey { get; set; }

        [JsonPropertyName("setting_value")]
        public JsonElement SettingValue { get; set; }
    }

    private sealed class SubscriptionTypeSettingsRow
    {
        [JsonPropertyName("tier_code")]
        public string? TierCode { get; set; }

        [JsonPropertyName("display_name")]
        public string? DisplayName { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("billing_period_months")]
        public int BillingPeriodMonths { get; set; }

        [JsonPropertyName("price_zar")]
        public decimal PriceZar { get; set; }

        [JsonPropertyName("payfast_plan_slug")]
        public string? PayFastPlanSlug { get; set; }

        [JsonPropertyName("paystack_plan_code")]
        public string? PaystackPlanCode { get; set; }

        [JsonPropertyName("is_active")]
        public bool IsActive { get; set; }
    }

    private sealed class SubscriptionDiscountCodeRow
    {
        [JsonPropertyName("discount_code_id")]
        public Guid DiscountCodeId { get; set; }

        [JsonPropertyName("code")]
        public string? Code { get; set; }

        [JsonPropertyName("display_name")]
        public string? DisplayName { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("is_group_code")]
        public bool IsGroupCode { get; set; }

        [JsonPropertyName("parent_discount_code_id")]
        public Guid? ParentDiscountCodeId { get; set; }

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

        [JsonPropertyName("source_system")]
        public string? SourceSystem { get; set; }
    }

    private sealed class SubscriptionDiscountCodeTierRow
    {
        [JsonPropertyName("discount_code_id")]
        public Guid DiscountCodeId { get; set; }

        [JsonPropertyName("tier_code")]
        public string? TierCode { get; set; }

        [JsonPropertyName("initial_payment_zar")]
        public decimal InitialPaymentZar { get; set; }

        [JsonPropertyName("billing_amount_zar")]
        public decimal BillingAmountZar { get; set; }

        [JsonPropertyName("cycle_number")]
        public int CycleNumber { get; set; }

        [JsonPropertyName("cycle_period")]
        public string? CyclePeriod { get; set; }

        [JsonPropertyName("billing_limit")]
        public int? BillingLimit { get; set; }

        [JsonPropertyName("trial_amount_zar")]
        public decimal TrialAmountZar { get; set; }

        [JsonPropertyName("trial_limit")]
        public int TrialLimit { get; set; }

        [JsonPropertyName("expiration_number")]
        public int? ExpirationNumber { get; set; }

        [JsonPropertyName("expiration_period")]
        public string? ExpirationPeriod { get; set; }
    }

    private sealed class SubscriptionDiscountCodeUseRow
    {
        [JsonPropertyName("redemption_id")]
        public Guid RedemptionId { get; set; }

        [JsonPropertyName("discount_code_id")]
        public Guid DiscountCodeId { get; set; }

        [JsonPropertyName("email")]
        public string? Email { get; set; }

        [JsonPropertyName("tier_code")]
        public string? TierCode { get; set; }

        [JsonPropertyName("redeemed_at")]
        public DateTimeOffset RedeemedAt { get; set; }

        [JsonPropertyName("access_expires_at")]
        public DateTimeOffset? AccessExpiresAt { get; set; }

        [JsonPropertyName("source_system")]
        public string? SourceSystem { get; set; }
    }

    private sealed class DiscountCodeIdRow
    {
        [JsonPropertyName("discount_code_id")]
        public Guid DiscountCodeId { get; set; }
    }
}
