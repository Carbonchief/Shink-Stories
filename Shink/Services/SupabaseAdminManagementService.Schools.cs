using System.Text.Json;
using System.Text.Json.Serialization;
using Shink.Components.Content;

namespace Shink.Services;

public sealed partial class SupabaseAdminManagementService
{
    private static readonly HashSet<string> SchoolSetupStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "active",
        "paused",
        "cancelled"
    };

    public async Task<AdminSchoolSetupSnapshot> GetSchoolSetupsAsync(
        string? adminEmail,
        CancellationToken cancellationToken = default)
    {
        var context = await TryCreateAdminOperationContextAsync(adminEmail, cancellationToken);
        if (context is null)
        {
            return BuildEmptySchoolSetupSnapshot();
        }

        var accountsTask = FetchRowsAsync<AdminSchoolAccountRow>(
            new Uri(
                context.BaseUri,
                "rest/v1/school_accounts" +
                "?select=school_account_id,school_name,admin_email,plan_tier_code,plan_name,slot_limit,admin_uses_slot,status,created_at,updated_at" +
                "&order=updated_at.desc" +
                "&limit=500"),
            context.ApiKey,
            cancellationToken);
        var seatsTask = FetchRowsAsync<AdminSchoolSeatSummaryRow>(
            new Uri(
                context.BaseUri,
                "rest/v1/school_seats?select=school_account_id,status,role&status=neq.removed&limit=5000"),
            context.ApiKey,
            cancellationToken);
        var subscribersTask = FetchRowsAsync<AdminSchoolSubscriberRow>(
            new Uri(
                context.BaseUri,
                "rest/v1/subscribers?select=subscriber_id,email&limit=5000"),
            context.ApiKey,
            cancellationToken);
        var subscriptionsTask = FetchRowsAsync<AdminSchoolSubscriptionRow>(
            new Uri(
                context.BaseUri,
                $"rest/v1/subscriptions?select=subscriber_id,tier_code,status,next_renewal_at,source_system&status=eq.active&source_system=neq.school_seat&tier_code=in.({BuildSchoolTierFilter()})&limit=5000"),
            context.ApiKey,
            cancellationToken);

        await Task.WhenAll(accountsTask, seatsTask, subscribersTask, subscriptionsTask);

        var seatGroups = seatsTask.Result
            .Where(seat => seat.SchoolAccountId != Guid.Empty)
            .GroupBy(seat => seat.SchoolAccountId)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var subscriberEmailById = subscribersTask.Result
            .Where(row => row.SubscriberId != Guid.Empty && NormalizeEmail(row.Email) is not null)
            .GroupBy(row => row.SubscriberId)
            .ToDictionary(group => group.Key, group => NormalizeEmail(group.First().Email)!);
        var accessExpiryByEmailAndTier = subscriptionsTask.Result
            .Where(row => row.SubscriberId != Guid.Empty && !string.IsNullOrWhiteSpace(row.TierCode))
            .Select(row => new
            {
                Email = subscriberEmailById.TryGetValue(row.SubscriberId, out var email) ? email : null,
                TierCode = row.TierCode,
                row.NextRenewalAt
            })
            .Where(row => row.Email is not null)
            .GroupBy(row => $"{row.Email}|{row.TierCode}", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(row => row.NextRenewalAt)
                    .Where(value => value.HasValue)
                    .DefaultIfEmpty()
                    .Max(),
                StringComparer.OrdinalIgnoreCase);

        var schools = accountsTask.Result
            .Where(row => row.SchoolAccountId != Guid.Empty)
            .Select(row =>
            {
                seatGroups.TryGetValue(row.SchoolAccountId, out var seats);
                var normalizedEmail = NormalizeEmail(row.AdminEmail) ?? row.AdminEmail.Trim();
                var accessKey = $"{normalizedEmail}|{row.PlanTierCode}";
                accessExpiryByEmailAndTier.TryGetValue(accessKey, out var accessExpiresAt);

                return new AdminSchoolAccountRecord(
                    row.SchoolAccountId,
                    row.SchoolName,
                    normalizedEmail,
                    row.PlanTierCode,
                    string.IsNullOrWhiteSpace(row.PlanName) ? ResolveSchoolPlanName(row.PlanTierCode) : row.PlanName,
                    row.SlotLimit,
                    row.AdminUsesSlot,
                    string.IsNullOrWhiteSpace(row.Status) ? "active" : row.Status,
                    seats?.Length ?? 0,
                    seats?.Count(seat => string.Equals(seat.Status, "invited", StringComparison.OrdinalIgnoreCase)) ?? 0,
                    seats?.Count(seat => string.Equals(seat.Status, "accepted", StringComparison.OrdinalIgnoreCase)) ?? 0,
                    row.CreatedAt,
                    row.UpdatedAt,
                    accessExpiresAt);
            })
            .ToArray();

        return new AdminSchoolSetupSnapshot(schools, BuildSchoolPlanOptions());
    }

    public async Task<AdminOperationResult> SaveSchoolSetupAsync(
        string? adminEmail,
        AdminSchoolSetupSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        var context = await TryCreateAdminOperationContextAsync(adminEmail, cancellationToken);
        if (context is null)
        {
            return new AdminOperationResult(false, "Admin toegang kon nie bevestig word nie.");
        }

        var normalizedSchoolName = NormalizeOptionalText(request.SchoolName, 180);
        if (string.IsNullOrWhiteSpace(normalizedSchoolName))
        {
            return new AdminOperationResult(false, "Skoolnaam is verpligtend.");
        }

        var normalizedSchoolAdminEmail = NormalizeEmail(request.AdminEmail);
        if (normalizedSchoolAdminEmail is null)
        {
            return new AdminOperationResult(false, "Gebruik asseblief 'n geldige skool admin e-posadres.");
        }

        var plan = PaymentPlanCatalog.FindByTierCode(request.PlanTierCode);
        if (plan is null || !plan.IsSchoolPlan || plan.SchoolSlotLimit is null)
        {
            return new AdminOperationResult(false, "Kies asseblief 'n geldige skoolplan.");
        }

        var normalizedStatus = NormalizeSchoolSetupStatus(request.Status);
        if (normalizedStatus is null)
        {
            return new AdminOperationResult(false, "Status moet active, paused of cancelled wees.");
        }

        var isActive = string.Equals(normalizedStatus, "active", StringComparison.OrdinalIgnoreCase);
        var accessExpiresAt = request.AccessExpiresAt?.ToUniversalTime();
        if (isActive)
        {
            accessExpiresAt ??= DateTimeOffset.UtcNow.AddMonths(plan.BillingPeriodMonths);
            if (accessExpiresAt.Value <= DateTimeOffset.UtcNow)
            {
                return new AdminOperationResult(false, "Aktiewe skooltoegang moet 'n toekomstige vervaldatum he.");
            }
        }

        if (isActive && await HasActiveSchoolAdminEmailConflictAsync(
                context,
                normalizedSchoolAdminEmail,
                request.SchoolAccountId,
                cancellationToken))
        {
            return new AdminOperationResult(false, "Daardie e-posadres het reeds 'n aktiewe skoolrekening.");
        }

        var existingAccount = request.SchoolAccountId.HasValue && request.SchoolAccountId.Value != Guid.Empty
            ? await FetchSchoolAccountByIdAsync(context, request.SchoolAccountId.Value, cancellationToken)
            : null;

        var schoolAccountId = await SaveSchoolAccountRowAsync(
            context,
            request.SchoolAccountId,
            normalizedSchoolName,
            normalizedSchoolAdminEmail,
            plan,
            request.AdminUsesSlot,
            normalizedStatus,
            cancellationToken);
        if (schoolAccountId is null)
        {
            return new AdminOperationResult(false, "Kon nie die skoolrekening stoor nie.");
        }

        var subscriberId = await UpsertSchoolSubscriberAsync(
            context,
            normalizedSchoolAdminEmail,
            cancellationToken);
        if (subscriberId is null)
        {
            return new AdminOperationResult(false, "Skool is gestoor, maar die admin gebruiker kon nie voorberei word nie.");
        }

        await CancelSchoolAdminEntitlementsForSubscriberAsync(
            context,
            subscriberId.Value,
            cancellationToken);

        if (!await SaveSchoolAdminEntitlementAsync(
                context,
                subscriberId.Value,
                plan,
                normalizedStatus,
                accessExpiresAt,
                cancellationToken))
        {
            return new AdminOperationResult(false, "Skool is gestoor, maar toegang kon nie geaktiveer word nie.");
        }

        var previousAdminEmail = NormalizeEmail(existingAccount?.AdminEmail);
        if (!string.IsNullOrWhiteSpace(previousAdminEmail) &&
            !string.Equals(previousAdminEmail, normalizedSchoolAdminEmail, StringComparison.OrdinalIgnoreCase))
        {
            await CancelSchoolAdminEntitlementsForEmailAsync(
                context,
                previousAdminEmail,
                cancellationToken);
            await RemoveSchoolAdminSeatAsync(
                context,
                schoolAccountId.Value,
                previousAdminEmail,
                cancellationToken);
        }

        if (isActive && request.AdminUsesSlot)
        {
            await UpsertSchoolAdminSeatAsync(
                context,
                schoolAccountId.Value,
                normalizedSchoolAdminEmail,
                plan,
                accessExpiresAt!.Value,
                cancellationToken);
        }
        else
        {
            await RemoveSchoolAdminSeatAsync(
                context,
                schoolAccountId.Value,
                normalizedSchoolAdminEmail,
                cancellationToken);
        }

        return new AdminOperationResult(true, EntityId: schoolAccountId);
    }

    private static AdminSchoolSetupSnapshot BuildEmptySchoolSetupSnapshot() =>
        new([], BuildSchoolPlanOptions());

    private static IReadOnlyList<AdminSchoolPlanOption> BuildSchoolPlanOptions() =>
        PaymentPlanCatalog.SchoolPlans
            .Select(plan => new AdminSchoolPlanOption(
                plan.TierCode,
                plan.Name,
                plan.Amount,
                plan.SchoolSlotLimit ?? 0))
            .ToArray();

    private static string BuildSchoolTierFilter() =>
        string.Join(',', PaymentPlanCatalog.SchoolPlans.Select(plan => Uri.EscapeDataString(plan.TierCode)));

    private static string ResolveSchoolPlanName(string? tierCode) =>
        PaymentPlanCatalog.FindByTierCode(tierCode)?.Name ?? tierCode ?? string.Empty;

    private static string? NormalizeSchoolSetupStatus(string? status)
    {
        var normalized = NormalizeOptionalText(status, 24)?.ToLowerInvariant() ?? "active";
        return SchoolSetupStatuses.Contains(normalized) ? normalized : null;
    }

    private async Task<bool> HasActiveSchoolAdminEmailConflictAsync(
        AdminOperationContext context,
        string adminEmail,
        Guid? currentSchoolAccountId,
        CancellationToken cancellationToken)
    {
        var escapedEmail = Uri.EscapeDataString(adminEmail);
        var rows = await FetchRowsAsync<AdminSchoolAccountRow>(
            new Uri(
                context.BaseUri,
                "rest/v1/school_accounts" +
                "?select=school_account_id,admin_email,status" +
                $"&admin_email=eq.{escapedEmail}" +
                "&status=eq.active" +
                "&limit=5"),
            context.ApiKey,
            cancellationToken);

        return rows.Any(row => !currentSchoolAccountId.HasValue || row.SchoolAccountId != currentSchoolAccountId.Value);
    }

    private async Task<AdminSchoolAccountRow?> FetchSchoolAccountByIdAsync(
        AdminOperationContext context,
        Guid schoolAccountId,
        CancellationToken cancellationToken)
    {
        var escapedSchoolAccountId = Uri.EscapeDataString(schoolAccountId.ToString("D"));
        var rows = await FetchRowsAsync<AdminSchoolAccountRow>(
            new Uri(
                context.BaseUri,
                "rest/v1/school_accounts" +
                "?select=school_account_id,school_name,admin_email,plan_tier_code,plan_name,slot_limit,admin_uses_slot,status,created_at,updated_at" +
                $"&school_account_id=eq.{escapedSchoolAccountId}" +
                "&limit=1"),
            context.ApiKey,
            cancellationToken);

        return rows.FirstOrDefault();
    }

    private async Task<Guid?> SaveSchoolAccountRowAsync(
        AdminOperationContext context,
        Guid? schoolAccountId,
        string schoolName,
        string adminEmail,
        PaymentPlan plan,
        bool adminUsesSlot,
        string status,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            school_name = schoolName,
            admin_email = adminEmail,
            plan_tier_code = plan.TierCode,
            plan_name = plan.Name,
            slot_limit = plan.SchoolSlotLimit ?? 0,
            admin_uses_slot = adminUsesSlot,
            status
        };

        Uri uri;
        HttpMethod method;
        string preferHeader;
        if (schoolAccountId.HasValue && schoolAccountId.Value != Guid.Empty)
        {
            uri = new Uri(
                context.BaseUri,
                $"rest/v1/school_accounts?school_account_id=eq.{Uri.EscapeDataString(schoolAccountId.Value.ToString("D"))}&select=school_account_id");
            method = new HttpMethod("PATCH");
            preferHeader = "return=representation";
        }
        else
        {
            uri = new Uri(context.BaseUri, "rest/v1/school_accounts?select=school_account_id");
            method = HttpMethod.Post;
            preferHeader = "return=representation";
        }

        using var saveRequest = CreateJsonRequest(method, uri, context.ApiKey, payload, preferHeader);
        using var saveResponse = await _httpClient.SendAsync(saveRequest, cancellationToken);
        var saveBody = await saveResponse.Content.ReadAsStringAsync(cancellationToken);
        if (!saveResponse.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "School account save failed. Status={StatusCode} Body={Body}",
                (int)saveResponse.StatusCode,
                saveBody);
            return null;
        }

        return TryReadFirstGuidProperty(saveBody, "school_account_id") ?? schoolAccountId;
    }

    private async Task<Guid?> UpsertSchoolSubscriberAsync(
        AdminOperationContext context,
        string email,
        CancellationToken cancellationToken)
    {
        var uri = new Uri(context.BaseUri, "rest/v1/subscribers?on_conflict=email&select=subscriber_id");
        using var request = CreateJsonRequest(
            HttpMethod.Post,
            uri,
            context.ApiKey,
            new { email },
            "resolution=merge-duplicates,return=representation");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "School admin subscriber upsert failed. email={Email} Status={StatusCode} Body={Body}",
                email,
                (int)response.StatusCode,
                responseText);
            return null;
        }

        return TryReadFirstGuidProperty(responseText, "subscriber_id");
    }

    private async Task CancelSchoolAdminEntitlementsForEmailAsync(
        AdminOperationContext context,
        string email,
        CancellationToken cancellationToken)
    {
        var subscriberRows = await FetchRowsAsync<AdminSchoolSubscriberRow>(
            new Uri(
                context.BaseUri,
                "rest/v1/subscribers" +
                "?select=subscriber_id,email" +
                $"&email=eq.{Uri.EscapeDataString(email)}" +
                "&limit=1"),
            context.ApiKey,
            cancellationToken);
        var subscriberId = subscriberRows.FirstOrDefault()?.SubscriberId;
        if (!subscriberId.HasValue || subscriberId.Value == Guid.Empty)
        {
            return;
        }

        await CancelSchoolAdminEntitlementsForSubscriberAsync(context, subscriberId.Value, cancellationToken);
    }

    private async Task CancelSchoolAdminEntitlementsForSubscriberAsync(
        AdminOperationContext context,
        Guid subscriberId,
        CancellationToken cancellationToken)
    {
        var uri = new Uri(
            context.BaseUri,
            "rest/v1/subscriptions" +
            $"?subscriber_id=eq.{Uri.EscapeDataString(subscriberId.ToString("D"))}" +
            "&source_system=eq.admin_override" +
            "&status=eq.active" +
            $"&tier_code=in.({BuildSchoolTierFilter()})");
        using var request = CreateJsonRequest(
            new HttpMethod("PATCH"),
            uri,
            context.ApiKey,
            new
            {
                status = "cancelled",
                cancelled_at = DateTimeOffset.UtcNow.UtcDateTime
            },
            "return=minimal");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "School admin entitlement cleanup failed. subscriberId={SubscriberId} Status={StatusCode} Body={Body}",
                subscriberId,
                (int)response.StatusCode,
                responseText);
        }
    }

    private async Task<bool> SaveSchoolAdminEntitlementAsync(
        AdminOperationContext context,
        Guid subscriberId,
        PaymentPlan plan,
        string schoolStatus,
        DateTimeOffset? accessExpiresAt,
        CancellationToken cancellationToken)
    {
        var nowUtc = DateTimeOffset.UtcNow;
        var isActive = string.Equals(schoolStatus, "active", StringComparison.OrdinalIgnoreCase);
        var payload = new
        {
            subscriber_id = subscriberId,
            tier_code = plan.TierCode,
            provider = "free",
            provider_payment_id = $"admin-override-{subscriberId:D}-{plan.TierCode}",
            provider_transaction_id = (string?)null,
            provider_token = (string?)null,
            status = isActive ? "active" : "cancelled",
            subscribed_at = nowUtc.UtcDateTime,
            next_renewal_at = isActive ? accessExpiresAt?.UtcDateTime : null,
            cancelled_at = isActive ? (DateTime?)null : nowUtc.UtcDateTime,
            source_system = "admin_override",
            billing_amount_zar = plan.Amount,
            billing_period_months = plan.BillingPeriodMonths,
            billing_amount_source = "manual"
        };

        var uri = new Uri(context.BaseUri, "rest/v1/subscriptions?on_conflict=provider,provider_payment_id&select=subscription_id");
        using var request = CreateJsonRequest(HttpMethod.Post, uri, context.ApiKey, payload, "resolution=merge-duplicates,return=representation");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return true;
        }

        _logger.LogWarning(
            "School admin entitlement save failed. subscriberId={SubscriberId} tier={TierCode} Status={StatusCode} Body={Body}",
            subscriberId,
            plan.TierCode,
            (int)response.StatusCode,
            responseText);
        return false;
    }

    private async Task UpsertSchoolAdminSeatAsync(
        AdminOperationContext context,
        Guid schoolAccountId,
        string email,
        PaymentPlan plan,
        DateTimeOffset accessExpiresAt,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            school_account_id = schoolAccountId,
            email,
            display_name = "Skool admin",
            role = "school_admin",
            status = "accepted",
            invited_by_email = context.AdminEmail,
            accepted_at = DateTimeOffset.UtcNow.UtcDateTime,
            removed_at = (DateTime?)null
        };

        var uri = new Uri(context.BaseUri, "rest/v1/school_seats?on_conflict=school_account_id,email&select=school_seat_id");
        using var request = CreateJsonRequest(HttpMethod.Post, uri, context.ApiKey, payload, "resolution=merge-duplicates,return=representation");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "School admin seat upsert failed. schoolAccountId={SchoolAccountId} email={Email} Status={StatusCode} Body={Body}",
                schoolAccountId,
                email,
                (int)response.StatusCode,
                responseText);
            return;
        }

        var seatId = TryReadFirstGuidProperty(responseText, "school_seat_id");
        if (!seatId.HasValue)
        {
            return;
        }

        var subscriberId = await UpsertSchoolSubscriberAsync(context, email, cancellationToken);
        if (!subscriberId.HasValue)
        {
            return;
        }

        var entitlementPayload = new
        {
            subscriber_id = subscriberId.Value,
            tier_code = plan.TierCode,
            provider = "free",
            provider_payment_id = $"school-seat-{seatId.Value:D}",
            provider_transaction_id = (string?)null,
            provider_token = (string?)null,
            status = "active",
            subscribed_at = DateTimeOffset.UtcNow.UtcDateTime,
            next_renewal_at = accessExpiresAt.UtcDateTime,
            cancelled_at = (DateTime?)null,
            source_system = "school_seat"
        };

        var subscriptionUri = new Uri(context.BaseUri, "rest/v1/subscriptions?on_conflict=provider,provider_payment_id&select=subscription_id");
        using var subscriptionRequest = CreateJsonRequest(
            HttpMethod.Post,
            subscriptionUri,
            context.ApiKey,
            entitlementPayload,
            "resolution=merge-duplicates,return=representation");
        using var subscriptionResponse = await _httpClient.SendAsync(subscriptionRequest, cancellationToken);
        if (!subscriptionResponse.IsSuccessStatusCode)
        {
            var subscriptionBody = await subscriptionResponse.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "School admin seat entitlement save failed. seatId={SeatId} Status={StatusCode} Body={Body}",
                seatId.Value,
                (int)subscriptionResponse.StatusCode,
                subscriptionBody);
        }
    }

    private async Task RemoveSchoolAdminSeatAsync(
        AdminOperationContext context,
        Guid schoolAccountId,
        string email,
        CancellationToken cancellationToken)
    {
        var escapedSchoolAccountId = Uri.EscapeDataString(schoolAccountId.ToString("D"));
        var escapedEmail = Uri.EscapeDataString(email);
        var seats = await FetchRowsAsync<AdminSchoolSeatIdentityRow>(
            new Uri(
                context.BaseUri,
                "rest/v1/school_seats" +
                "?select=school_seat_id" +
                $"&school_account_id=eq.{escapedSchoolAccountId}" +
                $"&email=eq.{escapedEmail}" +
                "&role=eq.school_admin" +
                "&status=neq.removed" +
                "&limit=5"),
            context.ApiKey,
            cancellationToken);

        foreach (var seat in seats.Where(row => row.SchoolSeatId != Guid.Empty))
        {
            var escapedSeatId = Uri.EscapeDataString(seat.SchoolSeatId.ToString("D"));
            using var seatRequest = CreateJsonRequest(
                new HttpMethod("PATCH"),
                new Uri(context.BaseUri, $"rest/v1/school_seats?school_seat_id=eq.{escapedSeatId}"),
                context.ApiKey,
                new
                {
                    status = "removed",
                    removed_at = DateTimeOffset.UtcNow.UtcDateTime
                },
                "return=minimal");
            using var seatResponse = await _httpClient.SendAsync(seatRequest, cancellationToken);
            if (!seatResponse.IsSuccessStatusCode)
            {
                var seatBody = await seatResponse.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "School admin seat remove failed. seatId={SeatId} Status={StatusCode} Body={Body}",
                    seat.SchoolSeatId,
                    (int)seatResponse.StatusCode,
                    seatBody);
            }

            using var subscriptionRequest = CreateJsonRequest(
                new HttpMethod("PATCH"),
                new Uri(
                    context.BaseUri,
                    $"rest/v1/subscriptions?provider=eq.free&provider_payment_id=eq.{Uri.EscapeDataString($"school-seat-{seat.SchoolSeatId:D}")}&source_system=eq.school_seat"),
                context.ApiKey,
                new
                {
                    status = "cancelled",
                    cancelled_at = DateTimeOffset.UtcNow.UtcDateTime
                },
                "return=minimal");
            using var subscriptionResponse = await _httpClient.SendAsync(subscriptionRequest, cancellationToken);
            if (!subscriptionResponse.IsSuccessStatusCode)
            {
                var subscriptionBody = await subscriptionResponse.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "School admin seat entitlement cancel failed. seatId={SeatId} Status={StatusCode} Body={Body}",
                    seat.SchoolSeatId,
                    (int)subscriptionResponse.StatusCode,
                    subscriptionBody);
            }
        }
    }

    private sealed class AdminSchoolAccountRow
    {
        [JsonPropertyName("school_account_id")]
        public Guid SchoolAccountId { get; set; }

        [JsonPropertyName("school_name")]
        public string SchoolName { get; set; } = string.Empty;

        [JsonPropertyName("admin_email")]
        public string AdminEmail { get; set; } = string.Empty;

        [JsonPropertyName("plan_tier_code")]
        public string PlanTierCode { get; set; } = string.Empty;

        [JsonPropertyName("plan_name")]
        public string PlanName { get; set; } = string.Empty;

        [JsonPropertyName("slot_limit")]
        public int SlotLimit { get; set; }

        [JsonPropertyName("admin_uses_slot")]
        public bool AdminUsesSlot { get; set; }

        [JsonPropertyName("status")]
        public string Status { get; set; } = "active";

        [JsonPropertyName("created_at")]
        public DateTimeOffset CreatedAt { get; set; }

        [JsonPropertyName("updated_at")]
        public DateTimeOffset UpdatedAt { get; set; }
    }

    private sealed class AdminSchoolSeatSummaryRow
    {
        [JsonPropertyName("school_account_id")]
        public Guid SchoolAccountId { get; set; }

        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("role")]
        public string Role { get; set; } = string.Empty;
    }

    private sealed class AdminSchoolSeatIdentityRow
    {
        [JsonPropertyName("school_seat_id")]
        public Guid SchoolSeatId { get; set; }
    }

    private sealed class AdminSchoolSubscriberRow
    {
        [JsonPropertyName("subscriber_id")]
        public Guid SubscriberId { get; set; }

        [JsonPropertyName("email")]
        public string Email { get; set; } = string.Empty;
    }

    private sealed class AdminSchoolSubscriptionRow
    {
        [JsonPropertyName("subscriber_id")]
        public Guid SubscriberId { get; set; }

        [JsonPropertyName("tier_code")]
        public string TierCode { get; set; } = string.Empty;

        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("next_renewal_at")]
        public DateTimeOffset? NextRenewalAt { get; set; }

        [JsonPropertyName("source_system")]
        public string SourceSystem { get; set; } = string.Empty;
    }
}
