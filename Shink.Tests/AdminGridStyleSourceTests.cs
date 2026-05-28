using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Shink.Tests;

[TestClass]
public class AdminGridStyleSourceTests
{
    [TestMethod]
    public void AdminMudTablesUseReadableRowAndColumnLines()
    {
        var css = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "Admin.razor.css"));
        var settingsCss = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "AdminSettingsPanel.razor.css"));
        var globalCss = File.ReadAllText(GetRepoPath("Shink", "wwwroot", "app.css"));

        StringAssert.Contains(css, "--admin-grid-line:");
        StringAssert.Contains(css, ".admin-page ::deep .mud-table-root");
        StringAssert.Contains(css, ".admin-page ::deep .mud-table-cell:not(:last-child)");
        StringAssert.Contains(css, ".admin-page ::deep tbody .mud-table-row:not(:last-child) > .mud-table-cell");
        StringAssert.Contains(css, ".admin-grid ::deep .mud-table-root");
        StringAssert.Contains(css, "border-collapse: separate;");
        StringAssert.Contains(css, ".admin-grid ::deep .mud-table-cell:not(:last-child)");
        StringAssert.Contains(css, "border-right: 1px solid var(--admin-grid-line);");
        StringAssert.Contains(css, ".admin-grid ::deep tbody .mud-table-row:not(:last-child) > .mud-table-cell");
        StringAssert.Contains(css, "border-bottom: 1px solid var(--admin-grid-line);");
        StringAssert.Contains(css, ".admin-grid ::deep .mud-sm-table .mud-table-cell");
        StringAssert.Contains(settingsCss, ".admin-settings-table ::deep .mud-table-cell:not(:last-child)");
        StringAssert.Contains(settingsCss, ".admin-settings-table ::deep tbody .mud-table-row:not(:last-child) > .mud-table-cell");
        StringAssert.Contains(globalCss, ".mud-dialog.admin-solid-dialog .mud-table-cell:not(:last-child)");
        StringAssert.Contains(globalCss, ".mud-dialog.admin-solid-dialog tbody .mud-table-row:not(:last-child) > .mud-table-cell");
    }

    [TestMethod]
    public void AdminGridSwitchesUseClearOnAndOffColors()
    {
        var css = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "Admin.razor.css"));
        var settingsCss = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "AdminSettingsPanel.razor.css"));
        var globalCss = File.ReadAllText(GetRepoPath("Shink", "wwwroot", "app.css"));

        StringAssert.Contains(css, "--admin-toggle-on:");
        StringAssert.Contains(css, "--admin-toggle-off:");
        StringAssert.Contains(css, ".admin-page ::deep .mud-switch-base:not(.mud-checked)");
        StringAssert.Contains(css, ".admin-page ::deep .mud-switch-base.mud-checked");
        StringAssert.Contains(css, ".admin-page ::deep .mud-switch-base:not(.mud-checked) + .mud-switch-track");
        StringAssert.Contains(css, ".admin-page ::deep .mud-switch-base.mud-checked + .mud-switch-track");
        StringAssert.Contains(css, "color: var(--admin-toggle-off) !important;");
        StringAssert.Contains(css, "color: var(--admin-toggle-on) !important;");
        StringAssert.Contains(settingsCss, ".admin-section ::deep .mud-switch-base:not(.mud-checked)");
        StringAssert.Contains(settingsCss, ".admin-section ::deep .mud-switch-base.mud-checked");
        StringAssert.Contains(globalCss, ".mud-dialog.admin-solid-dialog .mud-switch-base:not(.mud-checked)");
        StringAssert.Contains(globalCss, ".mud-dialog.admin-solid-dialog .mud-switch-base.mud-checked");
    }

    [TestMethod]
    public void AdminTablePagersUseHighContrastWhiteControls()
    {
        var css = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "Admin.razor.css"));

        StringAssert.Contains(css, "--admin-pager-text: #ffffff;");
        StringAssert.Contains(css, ".admin-page ::deep .mud-table-pagination");
        StringAssert.Contains(css, "color: var(--admin-pager-text) !important;");
        StringAssert.Contains(css, ".admin-page ::deep .mud-table-pagination .mud-icon-root");
        StringAssert.Contains(css, ".admin-page ::deep .mud-table-pagination .mud-input-adornment");
        StringAssert.Contains(css, ".admin-page ::deep .mud-table-pagination button:not(:disabled)");
        StringAssert.Contains(css, ".admin-page ::deep .mud-table-pagination .mud-icon-button");
        StringAssert.Contains(css, ".admin-page ::deep .mud-table-pagination svg");
        StringAssert.Contains(css, ".admin-page ::deep .mud-table-pagination svg path");
        StringAssert.Contains(css, "fill: var(--admin-pager-text) !important;");
        StringAssert.Contains(css, "color: var(--admin-pager-disabled) !important;");
        Assert.IsFalse(css.Contains("color: #1b2836 !important;", StringComparison.Ordinal));
    }

    [TestMethod]
    public void AdminGridTogglesExposeStateTooltips()
    {
        var markup = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "Admin.razor"));
        var settingsMarkup = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "AdminSettingsPanel.razor"));

        StringAssert.Contains(markup, "<MudTooltip Text=\"@BuildPlaylistEnabledToggleTooltip(context)\"");
        StringAssert.Contains(markup, "<MudTooltip Text=\"@BuildPlaylistHomeToggleTooltip(context)\"");
        StringAssert.Contains(markup, "<MudTooltip Text=\"@BuildPlaylistShowcaseToggleTooltip(context)\"");
        StringAssert.Contains(markup, "<MudTooltip Text=\"@BuildPlaylistStoryShowcaseToggleTooltip(context.IsShowcase)\"");
        StringAssert.Contains(markup, "private string BuildPlaylistEnabledToggleTooltip(AdminPlaylistRecord playlist)");
        StringAssert.Contains(markup, "private string BuildPlaylistHomeToggleTooltip(AdminPlaylistRecord playlist)");
        StringAssert.Contains(markup, "private string BuildPlaylistShowcaseToggleTooltip(AdminPlaylistRecord playlist)");
        StringAssert.Contains(markup, "private string BuildPlaylistStoryShowcaseToggleTooltip(bool isShowcase)");
        StringAssert.Contains(markup, "Aktief - klik om af te skakel");
        StringAssert.Contains(markup, "Active - click to turn off");

        StringAssert.Contains(settingsMarkup, "<MudTooltip Text=\"@BuildSignupCodeBypassTooltip()\"");
        StringAssert.Contains(settingsMarkup, "<MudTooltip Text=\"@BuildTierActiveTooltip(context.IsActive)\"");
        StringAssert.Contains(settingsMarkup, "<MudTooltip Text=\"@BuildOneUsePerUserTooltip()\"");
        StringAssert.Contains(settingsMarkup, "<MudTooltip Text=\"@BuildBypassPaymentTooltip()\"");
        StringAssert.Contains(settingsMarkup, "<MudTooltip Text=\"@BuildCodeActiveTooltip()\"");
        StringAssert.Contains(settingsMarkup, "private string BuildSignupCodeBypassTooltip()");
        StringAssert.Contains(settingsMarkup, "private string BuildTierActiveTooltip(bool isActive)");
        StringAssert.Contains(settingsMarkup, "private string BuildCodeActiveTooltip()");
        StringAssert.Contains(settingsMarkup, "Aan - klik om af te skakel");
        StringAssert.Contains(settingsMarkup, "On - click to turn off");
    }

    [TestMethod]
    public void SubscriberSearchRunsWhenEnterIsPressed()
    {
        var markup = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "Admin.razor"));

        StringAssert.Contains(markup, "@bind-Value=\"SubscriberSearch\"");
        StringAssert.Contains(markup, "OnKeyDown=\"HandleSubscriberSearchKeyDownAsync\"");
        StringAssert.Contains(markup, "private async Task HandleSubscriberSearchKeyDownAsync(KeyboardEventArgs args)");
        StringAssert.Contains(markup, "string.Equals(args.Key, \"Enter\", StringComparison.Ordinal)");
        StringAssert.Contains(markup, "await SearchSubscribersAsync();");
    }

    [TestMethod]
    public void SubscriberSearchRefreshesAsUserTypesWithDebounce()
    {
        var markup = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "Admin.razor"));

        StringAssert.Contains(markup, "@bind-Value=\"SubscriberSearch\"");
        StringAssert.Contains(markup, "DebounceInterval=\"300\"");
        StringAssert.Contains(markup, "OnDebounceIntervalElapsed=\"HandleSubscriberSearchDebouncedAsync\"");
        StringAssert.Contains(markup, "private async Task HandleSubscriberSearchDebouncedAsync(string value)");
        StringAssert.Contains(markup, "SubscriberSearch = value;");
        StringAssert.Contains(markup, "await RefreshSubscribersAsync(resetToFirstPage: true);");
    }

    [TestMethod]
    public void SubscriberGridShowsServerTotalCountInToolbar()
    {
        var markup = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "Admin.razor"));
        var css = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "Admin.razor.css"));

        StringAssert.Contains(markup, "class=\"admin-subscriber-count\"");
        StringAssert.Contains(markup, "@BuildSubscriberCountLabel()");
        StringAssert.Contains(markup, "private int? SubscriberTotalCount { get; set; }");
        StringAssert.Contains(markup, "SubscriberTotalCount = page.TotalCount;");
        StringAssert.Contains(markup, "private string BuildSubscriberCountLabel()");
        StringAssert.Contains(markup, "T(\"intekenaar\", \"subscriber\")");
        StringAssert.Contains(markup, "T(\"intekenaars\", \"subscribers\")");
        StringAssert.Contains(css, ".admin-subscriber-count");
    }

    [TestMethod]
    public void SubscriberGridDateColumnsExposeServerSideFilters()
    {
        var markup = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "Admin.razor"));
        var serviceContract = File.ReadAllText(GetRepoPath("Shink", "Services", "IAdminManagementService.cs"));
        var supabaseService = File.ReadAllText(GetRepoPath("Shink", "Services", "SupabaseAdminManagementService.cs"));
        var migrationPath = Directory.GetFiles(
                GetRepoPath("Shink", "Database", "migrations"),
                "*admin_subscribers_page_date_filters.sql")
            .Single();
        var migration = File.ReadAllText(migrationPath);

        StringAssert.Contains(markup, "id=\"subscriber-filter-subscribed-from\"");
        StringAssert.Contains(markup, "id=\"subscriber-filter-subscribed-to\"");
        StringAssert.Contains(markup, "id=\"subscriber-filter-next-payment-from\"");
        StringAssert.Contains(markup, "id=\"subscriber-filter-next-payment-to\"");
        StringAssert.Contains(markup, "ApplySubscriberDateFilterAsync(\"subscribed\")");
        StringAssert.Contains(markup, "ApplySubscriberDateFilterAsync(\"next-payment\")");
        StringAssert.Contains(markup, "SubscribedFrom: SubscriberColumnFilters.SubscribedFrom");
        StringAssert.Contains(markup, "NextPaymentTo: SubscriberColumnFilters.NextPaymentTo");

        StringAssert.Contains(serviceContract, "DateOnly? SubscribedFrom = null");
        StringAssert.Contains(serviceContract, "DateOnly? NextPaymentTo = null");
        StringAssert.Contains(supabaseService, "p_subscribed_from = request.SubscribedFrom");
        StringAssert.Contains(supabaseService, "p_next_payment_to = request.NextPaymentTo");
        StringAssert.Contains(migration, "p_subscribed_from date default null");
        StringAssert.Contains(migration, "p_next_payment_to date default null");
        StringAssert.Contains(migration, "summary.subscribed_at");
        StringAssert.Contains(migration, "summary.next_payment_due_at");
    }

    [TestMethod]
    public void AdminHeaderFiltersCloseAfterApplyOrSelection()
    {
        var markup = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "Admin.razor"));
        var gridTools = File.ReadAllText(GetRepoPath("Shink", "wwwroot", "js", "admin-grid-tools.js"));

        StringAssert.Contains(gridTools, "export function closeAdminHeaderFilters()");
        StringAssert.Contains(gridTools, "document.querySelectorAll(\".admin-header-filter[open]\")");
        StringAssert.Contains(gridTools, "filter.removeAttribute(\"open\")");
        StringAssert.Contains(markup, "private async Task CloseAdminHeaderFiltersAsync()");
        StringAssert.Contains(markup, "await gridModule.InvokeVoidAsync(\"closeAdminHeaderFilters\")");
        StringAssert.Contains(markup, "await CloseAdminHeaderFiltersAsync();");
        StringAssert.Contains(markup, "private async Task ApplySubscriberTextFilterAsync(string filterKey)");
        StringAssert.Contains(markup, "private async Task OnSubscriberSourceFilterChanged(ChangeEventArgs args)");
        StringAssert.Contains(markup, "private async Task ApplyStoryTitleFilter()");
        StringAssert.Contains(markup, "private async Task OnStoryStatusFilterChanged(ChangeEventArgs args)");
        StringAssert.Contains(markup, "private async Task ApplyPlaylistTitleFilter()");
        StringAssert.Contains(markup, "private async Task OnPlaylistEnabledFilterChanged(ChangeEventArgs args)");
    }

    [TestMethod]
    public void SubscriberGridTiersColumnIncludesBillingAmounts()
    {
        var markup = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "Admin.razor"));
        var serviceContract = File.ReadAllText(GetRepoPath("Shink", "Services", "IAdminManagementService.cs"));
        var supabaseService = File.ReadAllText(GetRepoPath("Shink", "Services", "SupabaseAdminManagementService.cs"));
        var migrationPath = Directory.GetFiles(
                GetRepoPath("Shink", "Database", "migrations"),
                "*admin_subscribers_page_tier_amounts.sql")
            .Single();
        var migration = File.ReadAllText(migrationPath);

        StringAssert.Contains(markup, "@BuildTierSummary(context)");
        StringAssert.Contains(markup, "AdminSubscriberManagementLogic.BuildSubscriberTierSummary(");
        StringAssert.Contains(serviceContract, "IReadOnlyList<AdminSubscriberTierSummary> ActiveTierSummaries");
        StringAssert.Contains(serviceContract, "public sealed record AdminSubscriberTierSummary(");
        StringAssert.Contains(supabaseService, "ActiveTierSummaries: disabledStates.GetValueOrDefault(item.SubscriberId)?.DisabledAt is not null");
        StringAssert.Contains(supabaseService, "item.ActiveTierSummaries?");
        StringAssert.Contains(supabaseService, "[JsonPropertyName(\"active_tier_summaries\")]");
        StringAssert.Contains(migration, "active_tier_summaries");
        StringAssert.Contains(migration, "billing_amount_zar");
        StringAssert.Contains(migration, "subscription_tiers");
    }

    [TestMethod]
    public void SubscriberDialogLoadsTierOptionsForNewSubscriberAndSavesManualAccess()
    {
        var markup = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "Admin.razor"));
        var serviceContract = File.ReadAllText(GetRepoPath("Shink", "Services", "IAdminManagementService.cs"));
        var supabaseService = File.ReadAllText(GetRepoPath("Shink", "Services", "SupabaseAdminManagementService.cs"));

        StringAssert.Contains(serviceContract, "GetSubscriberAccessTierOptionsAsync");
        StringAssert.Contains(supabaseService, "public async Task<IReadOnlyList<AdminSubscriptionTierOption>> GetSubscriberAccessTierOptionsAsync");
        StringAssert.Contains(markup, "private IReadOnlyList<AdminSubscriptionTierOption> SubscriberTierOptions");
        StringAssert.Contains(markup, "await LoadSubscriberTierOptionsAsync();");
        StringAssert.Contains(markup, "GetAvailableManualAccessTierOptions()");
        StringAssert.Contains(markup, "await SaveManualAccessFromSubscriberSaveAsync(savedSubscriberId)");
        StringAssert.Contains(markup, "Skep die intekenaar om hierdie handmatige toegang saam te stoor.");
        StringAssert.Contains(markup, "Create the subscriber to save this manual access with it.");
        StringAssert.Contains(markup, "Intekenaar is geskep en handmatige toegang is gestoor.");
        StringAssert.Contains(markup, "Subscriber created and manual access saved.");
    }

    private static string GetRepoPath(params string[] segments)
    {
        var parts = new[]
        {
            Path.GetDirectoryName(GetSourceFilePath())!,
            ".."
        }.Concat(segments).ToArray();

        return Path.GetFullPath(Path.Combine(parts));
    }

    private static string GetSourceFilePath([CallerFilePath] string path = "") => path;
}
