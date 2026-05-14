using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Shink.Components.Content;

namespace Shink.Services;

public sealed class SupabaseSchoolManagementService(
    HttpClient httpClient,
    IOptions<SupabaseOptions> supabaseOptions,
    ILogger<SupabaseSchoolManagementService> logger) : ISchoolManagementService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient = httpClient;
    private readonly SupabaseOptions _options = supabaseOptions.Value;
    private readonly ILogger<SupabaseSchoolManagementService> _logger = logger;

    public async Task<bool> HasSchoolAdminAccessAsync(string? adminEmail, CancellationToken cancellationToken = default)
    {
        var normalizedAdminEmail = NormalizeEmail(adminEmail);
        if (string.IsNullOrWhiteSpace(normalizedAdminEmail) ||
            !TryBuildSupabaseBaseUri(out var baseUri, out var apiKey))
        {
            return false;
        }

        var account = await FetchSchoolAccountAsync(baseUri, apiKey, normalizedAdminEmail, cancellationToken);
        if (account is not null)
        {
            return true;
        }

        return await ResolveActiveSchoolPlanAsync(baseUri, apiKey, normalizedAdminEmail, cancellationToken) is not null;
    }

    public async Task<SchoolDashboardSnapshot> GetDashboardAsync(string? adminEmail, CancellationToken cancellationToken = default)
    {
        var normalizedAdminEmail = NormalizeEmail(adminEmail);
        var availablePlans = BuildSchoolPlanRecords();
        if (string.IsNullOrWhiteSpace(normalizedAdminEmail) ||
            !TryBuildSupabaseBaseUri(out var baseUri, out var apiKey))
        {
            return new SchoolDashboardSnapshot(false, null, [], availablePlans);
        }

        var activeSchoolPlan = await ResolveActiveSchoolPlanAsync(baseUri, apiKey, normalizedAdminEmail, cancellationToken);
        var account = await FetchSchoolAccountAsync(baseUri, apiKey, normalizedAdminEmail, cancellationToken);
        if (activeSchoolPlan is not null)
        {
            account = account is null
                ? await CreateSchoolAccountAsync(baseUri, apiKey, normalizedAdminEmail, activeSchoolPlan, cancellationToken)
                : await SyncSchoolAccountPlanAsync(baseUri, apiKey, account, activeSchoolPlan, cancellationToken);
        }

        if (account is null)
        {
            return new SchoolDashboardSnapshot(false, null, [], availablePlans);
        }

        var seats = await FetchSchoolSeatsAsync(baseUri, apiKey, account, cancellationToken);
        return new SchoolDashboardSnapshot(activeSchoolPlan is not null, account, seats, availablePlans);
    }

    public async Task<SchoolOperationResult> UpdateSchoolNameAsync(string? adminEmail, string? schoolName, CancellationToken cancellationToken = default)
    {
        var context = await TryCreateSchoolContextAsync(adminEmail, cancellationToken);
        if (context is null)
        {
            return new SchoolOperationResult(false, "Jy het nie 'n aktiewe skoolopsie nie.");
        }

        var normalizedSchoolName = NormalizeOptionalText(schoolName, 120);
        if (string.IsNullOrWhiteSpace(normalizedSchoolName))
        {
            return new SchoolOperationResult(false, "Gee asseblief 'n skoolnaam.");
        }

        var escapedAccountId = Uri.EscapeDataString(context.Account.SchoolAccountId.ToString("D"));
        var uri = new Uri(context.BaseUri, $"rest/v1/school_accounts?school_account_id=eq.{escapedAccountId}");
        using var request = CreateJsonRequest(new HttpMethod("PATCH"), uri, context.ApiKey, new { school_name = normalizedSchoolName }, "return=minimal");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return new SchoolOperationResult(true, EntityId: context.Account.SchoolAccountId);
        }

        await LogFailedResponseAsync(response, "School name update failed.", cancellationToken);
        return new SchoolOperationResult(false, "Kon nie die skoolnaam nou stoor nie.");
    }

    public async Task<SchoolOperationResult> UpdateAdminSeatUsageAsync(string? adminEmail, bool adminUsesSlot, CancellationToken cancellationToken = default)
    {
        var context = await TryCreateSchoolContextAsync(adminEmail, cancellationToken);
        if (context is null)
        {
            return new SchoolOperationResult(false, "Jy het nie 'n aktiewe skoolopsie nie.");
        }

        var escapedAccountId = Uri.EscapeDataString(context.Account.SchoolAccountId.ToString("D"));
        var uri = new Uri(context.BaseUri, $"rest/v1/school_accounts?school_account_id=eq.{escapedAccountId}");
        using var request = CreateJsonRequest(new HttpMethod("PATCH"), uri, context.ApiKey, new { admin_uses_slot = adminUsesSlot }, "return=minimal");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            await LogFailedResponseAsync(response, "School admin slot preference update failed.", cancellationToken);
            return new SchoolOperationResult(false, "Kon nie jou plek-keuse nou stoor nie.");
        }

        if (adminUsesSlot)
        {
            var seatResult = await UpsertSeatAsync(
                context,
                context.AdminEmail,
                "Skool admin",
                "school_admin",
                "accepted",
                countsTowardSlot: true,
                cancellationToken);
            if (!seatResult.IsSuccess)
            {
                return seatResult;
            }

            await GrantSeatAccessAsync(context, seatResult.EntityId!.Value, context.AdminEmail, cancellationToken);
        }
        else
        {
            var adminSeat = (await FetchSchoolSeatsAsync(context.BaseUri, context.ApiKey, context.Account, cancellationToken))
                .FirstOrDefault(seat => string.Equals(seat.Email, context.AdminEmail, StringComparison.OrdinalIgnoreCase) &&
                                        string.Equals(seat.Role, "school_admin", StringComparison.OrdinalIgnoreCase));
            if (adminSeat is not null)
            {
                await RemoveSeatCoreAsync(context, adminSeat.SchoolSeatId, cancellationToken);
            }
        }

        return new SchoolOperationResult(true, EntityId: context.Account.SchoolAccountId);
    }

    public async Task<SchoolOperationResult> InviteTeacherAsync(string? adminEmail, SchoolInviteTeacherRequest request, CancellationToken cancellationToken = default)
    {
        var context = await TryCreateSchoolContextAsync(adminEmail, cancellationToken);
        if (context is null)
        {
            return new SchoolOperationResult(false, "Jy het nie 'n aktiewe skoolopsie nie.");
        }

        var normalizedEmail = NormalizeEmail(request.Email);
        if (string.IsNullOrWhiteSpace(normalizedEmail))
        {
            return new SchoolOperationResult(false, "Gee asseblief 'n geldige e-posadres.");
        }

        if (string.Equals(normalizedEmail, context.AdminEmail, StringComparison.OrdinalIgnoreCase))
        {
            return new SchoolOperationResult(false, "Gebruik die skool admin plek-keuse vir jou eie e-posadres.");
        }

        var seats = await FetchSchoolSeatsAsync(context.BaseUri, context.ApiKey, context.Account, cancellationToken);
        var existingSeat = seats.FirstOrDefault(seat =>
            string.Equals(seat.Email, normalizedEmail, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(seat.Status, "removed", StringComparison.OrdinalIgnoreCase));
        if (existingSeat is not null)
        {
            return new SchoolOperationResult(false, "Hierdie e-pos is reeds in 'n skoolplek.");
        }

        if (seats.Count(seat => seat.CountsTowardSlot) >= context.Account.SlotLimit)
        {
            return new SchoolOperationResult(false, "Al die beskikbare skoolplekke is reeds gebruik.");
        }

        var seatResult = await UpsertSeatAsync(
            context,
            normalizedEmail,
            NormalizeOptionalText(request.DisplayName, 120),
            "teacher",
            "invited",
            countsTowardSlot: true,
            cancellationToken);
        if (!seatResult.IsSuccess || seatResult.EntityId is null)
        {
            return seatResult;
        }

        await UpsertSubscriberAsync(context.BaseUri, context.ApiKey, normalizedEmail, request.DisplayName, cancellationToken);
        await GrantSeatAccessAsync(context, seatResult.EntityId.Value, normalizedEmail, cancellationToken);
        return seatResult;
    }

    public async Task<SchoolOperationResult> RemoveSeatAsync(string? adminEmail, Guid seatId, CancellationToken cancellationToken = default)
    {
        var context = await TryCreateSchoolContextAsync(adminEmail, cancellationToken);
        if (context is null)
        {
            return new SchoolOperationResult(false, "Jy het nie 'n aktiewe skoolopsie nie.");
        }

        return await RemoveSeatCoreAsync(context, seatId, cancellationToken);
    }

    public async Task<SchoolSeatStatsSnapshot?> GetSeatStatsAsync(string? adminEmail, Guid seatId, CancellationToken cancellationToken = default)
    {
        if (seatId == Guid.Empty)
        {
            return null;
        }

        var context = await TryCreateSchoolContextAsync(adminEmail, cancellationToken);
        if (context is null)
        {
            return null;
        }

        var seat = (await FetchSchoolSeatsAsync(context.BaseUri, context.ApiKey, context.Account, cancellationToken))
            .FirstOrDefault(candidate => candidate.SchoolSeatId == seatId);
        if (seat is null)
        {
            return null;
        }

        var subscriberId = await FetchSubscriberIdAsync(context.BaseUri, context.ApiKey, seat.Email, cancellationToken);
        if (subscriberId is null)
        {
            return new SchoolSeatStatsSnapshot(seat, false, null, 0, 0, 0, 0, 0, null, []);
        }

        var accessExpiresAt = await FetchSeatAccessExpiryAsync(context.BaseUri, context.ApiKey, subscriberId.Value, seatId, cancellationToken);
        var viewRowsTask = FetchSeatStoryViewsAsync(context.BaseUri, context.ApiKey, subscriberId.Value, cancellationToken);
        var listenRowsTask = FetchSeatStoryListensAsync(context.BaseUri, context.ApiKey, subscriberId.Value, cancellationToken);
        await Task.WhenAll(viewRowsTask, listenRowsTask);

        var viewRows = viewRowsTask.Result;
        var listenRows = listenRowsTask.Result;
        var lastViewAt = viewRows.Select(row => (DateTimeOffset?)row.ViewedAt).DefaultIfEmpty().Max();
        var lastListenAt = listenRows.Select(row => (DateTimeOffset?)row.OccurredAt).DefaultIfEmpty().Max();
        var lastActivityAt = MaxNullable(lastViewAt, lastListenAt);
        var recentStories = BuildRecentStoryActivity(viewRows, listenRows);
        var storyTitlesBySlug = await FetchStoryTitlesBySlugAsync(
            context.BaseUri,
            context.ApiKey,
            recentStories.Select(story => story.StorySlug),
            cancellationToken);
        recentStories = recentStories
            .Select(story => story with { StoryTitle = ResolveStoryTitle(story.StorySlug, storyTitlesBySlug) })
            .ToArray();

        return new SchoolSeatStatsSnapshot(
            seat,
            true,
            accessExpiresAt,
            viewRows.Count,
            viewRows.Select(row => row.StorySlug).Where(slug => !string.IsNullOrWhiteSpace(slug)).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            listenRows.Select(row => row.SessionId).Distinct().Count(),
            listenRows.Sum(row => row.ListenedSeconds),
            listenRows.Select(row => row.StorySlug).Where(slug => !string.IsNullOrWhiteSpace(slug)).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            lastActivityAt,
            recentStories);
    }

    private async Task<SchoolContext?> TryCreateSchoolContextAsync(string? adminEmail, CancellationToken cancellationToken)
    {
        var snapshot = await GetDashboardAsync(adminEmail, cancellationToken);
        var normalizedAdminEmail = NormalizeEmail(adminEmail);
        if (!snapshot.HasSchoolAccess ||
            snapshot.Account is null ||
            string.IsNullOrWhiteSpace(normalizedAdminEmail) ||
            !TryBuildSupabaseBaseUri(out var baseUri, out var apiKey))
        {
            return null;
        }

        return new SchoolContext(baseUri, apiKey, normalizedAdminEmail, snapshot.Account);
    }

    private async Task<PaymentPlan?> ResolveActiveSchoolPlanAsync(Uri baseUri, string apiKey, string email, CancellationToken cancellationToken)
    {
        var activeTierCodes = await FetchActiveSchoolOptionTierCodesAsync(baseUri, apiKey, email, cancellationToken);
        return PaymentPlanCatalog.SchoolPlans
            .OrderByDescending(plan => plan.SchoolSlotLimit ?? 0)
            .FirstOrDefault(plan => activeTierCodes.Any(tierCode =>
                string.Equals(tierCode, plan.TierCode, StringComparison.OrdinalIgnoreCase)));
    }

    private async Task<IReadOnlyList<string>> FetchActiveSchoolOptionTierCodesAsync(Uri baseUri, string apiKey, string email, CancellationToken cancellationToken)
    {
        var subscriberId = await FetchSubscriberIdAsync(baseUri, apiKey, email, cancellationToken);
        if (subscriberId is null)
        {
            return [];
        }

        var tierFilter = string.Join(',', PaymentPlanCatalog.SchoolPlans.Select(plan => Uri.EscapeDataString(plan.TierCode)));
        var escapedSubscriberId = Uri.EscapeDataString(subscriberId.Value.ToString("D"));
        var uri = new Uri(baseUri, "rest/v1/subscriptions" +
            "?select=status,next_renewal_at,cancelled_at,tier_code,source_system" +
            $"&subscriber_id=eq.{escapedSubscriberId}&status=eq.active&source_system=neq.school_seat&tier_code=in.({tierFilter})" +
            "&order=subscribed_at.desc&limit=25");

        var rows = await FetchRowsAsync<SchoolSubscriptionRow>(uri, apiKey, cancellationToken);
        var nowUtc = DateTimeOffset.UtcNow;
        return rows
            .Where(row => IsCurrentlyActiveSchoolOptionSubscription(row, nowUtc))
            .Select(row => row.TierCode)
            .Where(tierCode => !string.IsNullOrWhiteSpace(tierCode))
            .Select(tierCode => tierCode!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task<Guid?> FetchSubscriberIdAsync(Uri baseUri, string apiKey, string email, CancellationToken cancellationToken)
    {
        var escapedEmail = Uri.EscapeDataString(email);
        var uri = new Uri(baseUri, $"rest/v1/subscribers?select=subscriber_id&email=eq.{escapedEmail}&disabled_at=is.null&limit=1");
        var rows = await FetchRowsAsync<SchoolSubscriberRow>(uri, apiKey, cancellationToken);
        return rows.FirstOrDefault()?.SubscriberId;
    }

    private async Task<DateTimeOffset?> FetchSeatAccessExpiryAsync(Uri baseUri, string apiKey, Guid subscriberId, Guid seatId, CancellationToken cancellationToken)
    {
        var escapedSubscriberId = Uri.EscapeDataString(subscriberId.ToString("D"));
        var escapedPaymentId = Uri.EscapeDataString($"school-seat-{seatId:D}");
        var uri = new Uri(baseUri, "rest/v1/subscriptions" +
            "?select=next_renewal_at" +
            $"&subscriber_id=eq.{escapedSubscriberId}&provider=eq.free&provider_payment_id=eq.{escapedPaymentId}&source_system=eq.school_seat&status=eq.active" +
            "&order=subscribed_at.desc&limit=1");
        var rows = await FetchRowsAsync<SchoolSeatAccessRow>(uri, apiKey, cancellationToken);
        return rows.FirstOrDefault()?.NextRenewalAt;
    }

    private async Task<IReadOnlyList<SchoolStoryViewRow>> FetchSeatStoryViewsAsync(Uri baseUri, string apiKey, Guid subscriberId, CancellationToken cancellationToken)
    {
        var escapedSubscriberId = Uri.EscapeDataString(subscriberId.ToString("D"));
        var uri = new Uri(baseUri, "rest/v1/story_views" +
            "?select=story_slug,viewed_at" +
            $"&subscriber_id=eq.{escapedSubscriberId}&order=viewed_at.desc&limit=500");
        return await FetchRowsAsync<SchoolStoryViewRow>(uri, apiKey, cancellationToken);
    }

    private async Task<IReadOnlyList<SchoolStoryListenRow>> FetchSeatStoryListensAsync(Uri baseUri, string apiKey, Guid subscriberId, CancellationToken cancellationToken)
    {
        var escapedSubscriberId = Uri.EscapeDataString(subscriberId.ToString("D"));
        var uri = new Uri(baseUri, "rest/v1/story_listen_events" +
            "?select=story_slug,session_id,listened_seconds,occurred_at" +
            $"&subscriber_id=eq.{escapedSubscriberId}&order=occurred_at.desc&limit=1000");
        return await FetchRowsAsync<SchoolStoryListenRow>(uri, apiKey, cancellationToken);
    }

    private async Task<IReadOnlyDictionary<string, string>> FetchStoryTitlesBySlugAsync(
        Uri baseUri,
        string apiKey,
        IEnumerable<string> storySlugs,
        CancellationToken cancellationToken)
    {
        var normalizedSlugs = storySlugs
            .Select(slug => NormalizeOptionalText(slug, 160))
            .Where(slug => !string.IsNullOrWhiteSpace(slug))
            .Select(slug => slug!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .ToArray();
        if (normalizedSlugs.Length == 0)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var slugFilter = string.Join(",", normalizedSlugs.Select(Uri.EscapeDataString));
        var uri = new Uri(baseUri, "rest/v1/stories" +
            "?select=slug,title" +
            $"&slug=in.({slugFilter})&limit={normalizedSlugs.Length}");
        var rows = await FetchRowsAsync<SchoolStoryTitleRow>(uri, apiKey, cancellationToken);
        return rows
            .Where(row => !string.IsNullOrWhiteSpace(row.Slug) && !string.IsNullOrWhiteSpace(row.Title))
            .GroupBy(row => row.Slug!.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First().Title!.Trim(),
                StringComparer.OrdinalIgnoreCase);
    }

    private async Task<SchoolAccountRecord?> FetchSchoolAccountAsync(Uri baseUri, string apiKey, string adminEmail, CancellationToken cancellationToken)
    {
        var escapedEmail = Uri.EscapeDataString(adminEmail);
        var uri = new Uri(baseUri, "rest/v1/school_accounts" +
            "?select=school_account_id,school_name,admin_email,plan_tier_code,plan_name,slot_limit,admin_uses_slot,status,created_at,updated_at" +
            $"&admin_email=eq.{escapedEmail}&status=eq.active&order=created_at.desc&limit=1");
        var rows = await FetchRowsAsync<SchoolAccountRow>(uri, apiKey, cancellationToken);
        return rows.Select(MapSchoolAccount).FirstOrDefault();
    }

    private async Task<SchoolAccountRecord?> CreateSchoolAccountAsync(Uri baseUri, string apiKey, string adminEmail, PaymentPlan plan, CancellationToken cancellationToken)
    {
        var payload = new
        {
            school_name = "My skool",
            admin_email = adminEmail,
            plan_tier_code = plan.TierCode,
            plan_name = plan.Name,
            slot_limit = plan.SchoolSlotLimit ?? 1,
            admin_uses_slot = true,
            status = "active"
        };

        var uri = new Uri(baseUri, "rest/v1/school_accounts?select=school_account_id,school_name,admin_email,plan_tier_code,plan_name,slot_limit,admin_uses_slot,status,created_at,updated_at");
        using var request = CreateJsonRequest(HttpMethod.Post, uri, apiKey, payload, "return=representation");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            await LogFailedResponseAsync(response, "School account create failed.", cancellationToken);
            return null;
        }

        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        var rows = JsonSerializer.Deserialize<List<SchoolAccountRow>>(responseText, JsonOptions) ?? [];
        var account = rows.Select(MapSchoolAccount).FirstOrDefault();
        if (account is not null)
        {
            var context = new SchoolContext(baseUri, apiKey, adminEmail, account);
            var seatResult = await UpsertSeatAsync(context, adminEmail, "Skool admin", "school_admin", "accepted", true, cancellationToken);
            if (seatResult.IsSuccess && seatResult.EntityId is not null)
            {
                await GrantSeatAccessAsync(context, seatResult.EntityId.Value, adminEmail, cancellationToken);
            }
        }

        return account;
    }

    private async Task<SchoolAccountRecord> SyncSchoolAccountPlanAsync(Uri baseUri, string apiKey, SchoolAccountRecord account, PaymentPlan plan, CancellationToken cancellationToken)
    {
        if (string.Equals(account.PlanTierCode, plan.TierCode, StringComparison.OrdinalIgnoreCase) &&
            account.SlotLimit == (plan.SchoolSlotLimit ?? account.SlotLimit) &&
            string.Equals(account.PlanName, plan.Name, StringComparison.Ordinal))
        {
            return account;
        }

        var escapedAccountId = Uri.EscapeDataString(account.SchoolAccountId.ToString("D"));
        var uri = new Uri(baseUri, $"rest/v1/school_accounts?school_account_id=eq.{escapedAccountId}");
        var payload = new
        {
            plan_tier_code = plan.TierCode,
            plan_name = plan.Name,
            slot_limit = plan.SchoolSlotLimit ?? account.SlotLimit
        };
        using var request = CreateJsonRequest(new HttpMethod("PATCH"), uri, apiKey, payload, "return=minimal");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            await LogFailedResponseAsync(response, "School account plan sync failed.", cancellationToken);
            return account;
        }

        return account with
        {
            PlanTierCode = plan.TierCode,
            PlanName = plan.Name,
            SlotLimit = plan.SchoolSlotLimit ?? account.SlotLimit
        };
    }

    private async Task<IReadOnlyList<SchoolSeatRecord>> FetchSchoolSeatsAsync(Uri baseUri, string apiKey, SchoolAccountRecord account, CancellationToken cancellationToken)
    {
        var escapedAccountId = Uri.EscapeDataString(account.SchoolAccountId.ToString("D"));
        var uri = new Uri(baseUri, "rest/v1/school_seats" +
            "?select=school_seat_id,school_account_id,email,display_name,role,status,invited_at,accepted_at,removed_at" +
            $"&school_account_id=eq.{escapedAccountId}&status=neq.removed&order=invited_at.asc");
        var rows = await FetchRowsAsync<SchoolSeatRow>(uri, apiKey, cancellationToken);
        return rows.Select(row => MapSchoolSeat(row, account)).ToArray();
    }

    private async Task<SchoolOperationResult> UpsertSeatAsync(
        SchoolContext context,
        string email,
        string? displayName,
        string role,
        string status,
        bool countsTowardSlot,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = NormalizeEmail(email);
        if (string.IsNullOrWhiteSpace(normalizedEmail))
        {
            return new SchoolOperationResult(false, "Gee asseblief 'n geldige e-posadres.");
        }

        var payload = new
        {
            school_account_id = context.Account.SchoolAccountId,
            email = normalizedEmail,
            display_name = displayName,
            role,
            status,
            invited_by_email = context.AdminEmail,
            accepted_at = string.Equals(status, "accepted", StringComparison.OrdinalIgnoreCase) ? DateTimeOffset.UtcNow.UtcDateTime : (DateTime?)null,
            removed_at = (DateTime?)null
        };

        var uri = new Uri(context.BaseUri, "rest/v1/school_seats?on_conflict=school_account_id,email&select=school_seat_id");
        using var request = CreateJsonRequest(HttpMethod.Post, uri, context.ApiKey, payload, "resolution=merge-duplicates,return=representation");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            await LogFailedResponseAsync(response, "School seat upsert failed.", cancellationToken);
            return new SchoolOperationResult(false, "Kon nie die skoolplek nou stoor nie.");
        }

        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        var seatId = TryReadFirstGuidProperty(responseText, "school_seat_id");
        return new SchoolOperationResult(true, EntityId: seatId);
    }

    private async Task GrantSeatAccessAsync(SchoolContext context, Guid seatId, string email, CancellationToken cancellationToken)
    {
        var subscriberId = await UpsertSubscriberAsync(context.BaseUri, context.ApiKey, email, null, cancellationToken);
        if (subscriberId is null)
        {
            return;
        }

        var plan = PaymentPlanCatalog.FindByTierCode(context.Account.PlanTierCode);
        var nowUtc = DateTimeOffset.UtcNow;
        var payload = new
        {
            subscriber_id = subscriberId.Value,
            tier_code = context.Account.PlanTierCode,
            provider = "free",
            provider_payment_id = $"school-seat-{seatId:D}",
            provider_transaction_id = (string?)null,
            provider_token = (string?)null,
            status = "active",
            subscribed_at = nowUtc.UtcDateTime,
            next_renewal_at = nowUtc.AddMonths(plan?.BillingPeriodMonths ?? 12).UtcDateTime,
            cancelled_at = (DateTime?)null,
            source_system = "school_seat"
        };

        var uri = new Uri(context.BaseUri, "rest/v1/subscriptions?on_conflict=provider,provider_payment_id&select=subscription_id");
        using var request = CreateJsonRequest(HttpMethod.Post, uri, context.ApiKey, payload, "resolution=merge-duplicates,return=representation");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            await LogFailedResponseAsync(response, "School seat access grant failed.", cancellationToken);
        }
    }

    private async Task<SchoolOperationResult> RemoveSeatCoreAsync(SchoolContext context, Guid seatId, CancellationToken cancellationToken)
    {
        if (seatId == Guid.Empty)
        {
            return new SchoolOperationResult(false, "Kies asseblief 'n geldige plek.");
        }

        var escapedSeatId = Uri.EscapeDataString(seatId.ToString("D"));
        var escapedAccountId = Uri.EscapeDataString(context.Account.SchoolAccountId.ToString("D"));
        var seatUri = new Uri(context.BaseUri, $"rest/v1/school_seats?school_seat_id=eq.{escapedSeatId}&school_account_id=eq.{escapedAccountId}");
        var payload = new
        {
            status = "removed",
            removed_at = DateTimeOffset.UtcNow.UtcDateTime
        };
        using var seatRequest = CreateJsonRequest(new HttpMethod("PATCH"), seatUri, context.ApiKey, payload, "return=minimal");
        using var seatResponse = await _httpClient.SendAsync(seatRequest, cancellationToken);
        if (!seatResponse.IsSuccessStatusCode)
        {
            await LogFailedResponseAsync(seatResponse, "School seat remove failed.", cancellationToken);
            return new SchoolOperationResult(false, "Kon nie die plek nou verwyder nie.");
        }

        var paymentId = Uri.EscapeDataString($"school-seat-{seatId:D}");
        var subscriptionUri = new Uri(context.BaseUri, $"rest/v1/subscriptions?provider=eq.free&provider_payment_id=eq.{paymentId}&source_system=eq.school_seat");
        using var subscriptionRequest = CreateJsonRequest(
            new HttpMethod("PATCH"),
            subscriptionUri,
            context.ApiKey,
            new { status = "cancelled", cancelled_at = DateTimeOffset.UtcNow.UtcDateTime },
            "return=minimal");
        using var subscriptionResponse = await _httpClient.SendAsync(subscriptionRequest, cancellationToken);
        if (!subscriptionResponse.IsSuccessStatusCode)
        {
            await LogFailedResponseAsync(subscriptionResponse, "School seat access cancel failed.", cancellationToken);
        }

        return new SchoolOperationResult(true, EntityId: seatId);
    }

    private async Task<Guid?> UpsertSubscriberAsync(Uri baseUri, string apiKey, string email, string? displayName, CancellationToken cancellationToken)
    {
        var payload = new Dictionary<string, object?>
        {
            ["email"] = NormalizeEmail(email),
            ["display_name"] = NormalizeOptionalText(displayName, 120)
        };

        var uri = new Uri(baseUri, "rest/v1/subscribers?on_conflict=email&select=subscriber_id");
        using var request = CreateJsonRequest(HttpMethod.Post, uri, apiKey, payload, "resolution=merge-duplicates,return=representation");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            await LogFailedResponseAsync(response, "School subscriber upsert failed.", cancellationToken);
            return null;
        }

        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        return TryReadFirstGuidProperty(responseText, "subscriber_id");
    }

    private async Task<IReadOnlyList<T>> FetchRowsAsync<T>(Uri uri, string apiKey, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Get, uri, apiKey);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            await LogFailedResponseAsync(response, $"School fetch failed for {typeof(T).Name}.", cancellationToken);
            return [];
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<List<T>>(stream, JsonOptions, cancellationToken) ?? [];
    }

    private bool TryBuildSupabaseBaseUri(out Uri baseUri, out string apiKey)
    {
        baseUri = null!;
        apiKey = _options.SecretKey;
        if (string.IsNullOrWhiteSpace(apiKey) ||
            !Uri.TryCreate(_options.Url, UriKind.Absolute, out var parsedBaseUri))
        {
            return false;
        }

        baseUri = parsedBaseUri;
        return true;
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, Uri uri, string apiKey)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Headers.TryAddWithoutValidation("apikey", apiKey);
        return request;
    }

    private static HttpRequestMessage CreateJsonRequest(HttpMethod method, Uri uri, string apiKey, object payload, string? prefer = null)
    {
        var request = CreateRequest(method, uri, apiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");
        if (!string.IsNullOrWhiteSpace(prefer))
        {
            request.Headers.TryAddWithoutValidation("Prefer", prefer);
        }

        return request;
    }

    private async Task LogFailedResponseAsync(HttpResponseMessage response, string message, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        _logger.LogWarning("{Message} Status={StatusCode} Body={Body}", message, (int)response.StatusCode, body);
    }

    private static IReadOnlyList<SchoolPlanRecord> BuildSchoolPlanRecords() =>
        PaymentPlanCatalog.SchoolPlans
            .Select(plan => new SchoolPlanRecord(
                plan.Slug,
                plan.Name,
                plan.TierCode,
                plan.Amount,
                plan.SchoolSlotLimit ?? 0))
            .ToArray();

    private static SchoolAccountRecord MapSchoolAccount(SchoolAccountRow row) =>
        new(
            row.SchoolAccountId,
            row.SchoolName ?? "My skool",
            row.AdminEmail ?? string.Empty,
            row.PlanTierCode ?? string.Empty,
            row.PlanName ?? string.Empty,
            row.SlotLimit,
            row.AdminUsesSlot,
            row.Status ?? "active",
            row.CreatedAt,
            row.UpdatedAt);

    private static SchoolSeatRecord MapSchoolSeat(SchoolSeatRow row, SchoolAccountRecord account)
    {
        var countsTowardSlot = !string.Equals(row.Status, "removed", StringComparison.OrdinalIgnoreCase) &&
                               (!string.Equals(row.Role, "school_admin", StringComparison.OrdinalIgnoreCase) || account.AdminUsesSlot);
        return new SchoolSeatRecord(
            row.SchoolSeatId,
            row.SchoolAccountId,
            row.Email ?? string.Empty,
            row.DisplayName,
            row.Role ?? "teacher",
            row.Status ?? "invited",
            countsTowardSlot,
            row.InvitedAt,
            row.AcceptedAt,
            row.RemovedAt);
    }

    private static bool IsCurrentlyActiveSchoolOptionSubscription(SchoolSubscriptionRow row, DateTimeOffset nowUtc)
    {
        if (!string.Equals(row.Status, "active", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (row.CancelledAt is not null && row.CancelledAt <= nowUtc)
        {
            return false;
        }

        if (row.NextRenewalAt is null || row.NextRenewalAt < nowUtc)
        {
            return false;
        }

        return !string.Equals(row.SourceSystem, "school_seat", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<SchoolSeatStoryActivityRecord> BuildRecentStoryActivity(
        IReadOnlyList<SchoolStoryViewRow> viewRows,
        IReadOnlyList<SchoolStoryListenRow> listenRows)
    {
        var activityBySlug = new Dictionary<string, StoryActivityAccumulator>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in viewRows)
        {
            var slug = NormalizeOptionalText(row.StorySlug, 160);
            if (string.IsNullOrWhiteSpace(slug))
            {
                continue;
            }

            var activity = GetOrCreateStoryActivity(activityBySlug, slug);
            activity.Views++;
            activity.LastActivityAt = Max(activity.LastActivityAt, row.ViewedAt);
        }

        foreach (var row in listenRows)
        {
            var slug = NormalizeOptionalText(row.StorySlug, 160);
            if (string.IsNullOrWhiteSpace(slug))
            {
                continue;
            }

            var activity = GetOrCreateStoryActivity(activityBySlug, slug);
            activity.ListenedSeconds += row.ListenedSeconds;
            activity.LastActivityAt = Max(activity.LastActivityAt, row.OccurredAt);
        }

        return activityBySlug
            .Select(pair => new SchoolSeatStoryActivityRecord(pair.Key, pair.Key, pair.Value.Views, pair.Value.ListenedSeconds, pair.Value.LastActivityAt))
            .OrderByDescending(activity => activity.LastActivityAt)
            .Take(5)
            .ToArray();
    }

    private static string ResolveStoryTitle(string storySlug, IReadOnlyDictionary<string, string> storyTitlesBySlug) =>
        storyTitlesBySlug.TryGetValue(storySlug, out var title) && !string.IsNullOrWhiteSpace(title)
            ? title
            : storySlug;

    private static StoryActivityAccumulator GetOrCreateStoryActivity(Dictionary<string, StoryActivityAccumulator> activityBySlug, string slug)
    {
        if (!activityBySlug.TryGetValue(slug, out var activity))
        {
            activity = new StoryActivityAccumulator();
            activityBySlug[slug] = activity;
        }

        return activity;
    }

    private static DateTimeOffset Max(DateTimeOffset left, DateTimeOffset right) =>
        left >= right ? left : right;

    private static DateTimeOffset? MaxNullable(DateTimeOffset? left, DateTimeOffset? right) =>
        (left, right) switch
        {
            ({ } leftValue, { } rightValue) => Max(leftValue, rightValue),
            ({ } leftValue, null) => leftValue,
            (null, { } rightValue) => rightValue,
            _ => null
        };

    private static string? NormalizeEmail(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim().ToLowerInvariant();
        return normalized.Contains('@', StringComparison.Ordinal) ? normalized : null;
    }

    private static string? NormalizeOptionalText(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private static Guid? TryReadFirstGuidProperty(string? json, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
            {
                root = root[0];
            }

            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty(propertyName, out var value) &&
                value.ValueKind == JsonValueKind.String &&
                Guid.TryParse(value.GetString(), out var id))
            {
                return id;
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private sealed record SchoolContext(Uri BaseUri, string ApiKey, string AdminEmail, SchoolAccountRecord Account);

    private sealed class SchoolSubscriberRow
    {
        [JsonPropertyName("subscriber_id")]
        public Guid SubscriberId { get; set; }
    }

    private sealed class SchoolAccountRow
    {
        [JsonPropertyName("school_account_id")]
        public Guid SchoolAccountId { get; set; }

        [JsonPropertyName("school_name")]
        public string? SchoolName { get; set; }

        [JsonPropertyName("admin_email")]
        public string? AdminEmail { get; set; }

        [JsonPropertyName("plan_tier_code")]
        public string? PlanTierCode { get; set; }

        [JsonPropertyName("plan_name")]
        public string? PlanName { get; set; }

        [JsonPropertyName("slot_limit")]
        public int SlotLimit { get; set; }

        [JsonPropertyName("admin_uses_slot")]
        public bool AdminUsesSlot { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("created_at")]
        public DateTimeOffset CreatedAt { get; set; }

        [JsonPropertyName("updated_at")]
        public DateTimeOffset UpdatedAt { get; set; }
    }

    private sealed class SchoolSeatRow
    {
        [JsonPropertyName("school_seat_id")]
        public Guid SchoolSeatId { get; set; }

        [JsonPropertyName("school_account_id")]
        public Guid SchoolAccountId { get; set; }

        [JsonPropertyName("email")]
        public string? Email { get; set; }

        [JsonPropertyName("display_name")]
        public string? DisplayName { get; set; }

        [JsonPropertyName("role")]
        public string? Role { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("invited_at")]
        public DateTimeOffset InvitedAt { get; set; }

        [JsonPropertyName("accepted_at")]
        public DateTimeOffset? AcceptedAt { get; set; }

        [JsonPropertyName("removed_at")]
        public DateTimeOffset? RemovedAt { get; set; }
    }

    private sealed class SchoolSubscriptionRow
    {
        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("next_renewal_at")]
        public DateTimeOffset? NextRenewalAt { get; set; }

        [JsonPropertyName("cancelled_at")]
        public DateTimeOffset? CancelledAt { get; set; }

        [JsonPropertyName("tier_code")]
        public string? TierCode { get; set; }

        [JsonPropertyName("source_system")]
        public string? SourceSystem { get; set; }
    }

    private sealed class SchoolSeatAccessRow
    {
        [JsonPropertyName("next_renewal_at")]
        public DateTimeOffset? NextRenewalAt { get; set; }
    }

    private sealed class SchoolStoryViewRow
    {
        [JsonPropertyName("story_slug")]
        public string? StorySlug { get; set; }

        [JsonPropertyName("viewed_at")]
        public DateTimeOffset ViewedAt { get; set; }
    }

    private sealed class SchoolStoryListenRow
    {
        [JsonPropertyName("story_slug")]
        public string? StorySlug { get; set; }

        [JsonPropertyName("session_id")]
        public Guid SessionId { get; set; }

        [JsonPropertyName("listened_seconds")]
        public decimal ListenedSeconds { get; set; }

        [JsonPropertyName("occurred_at")]
        public DateTimeOffset OccurredAt { get; set; }
    }

    private sealed class SchoolStoryTitleRow
    {
        [JsonPropertyName("slug")]
        public string? Slug { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }
    }

    private sealed class StoryActivityAccumulator
    {
        public int Views { get; set; }
        public decimal ListenedSeconds { get; set; }
        public DateTimeOffset LastActivityAt { get; set; }
    }
}
