using Shink.Mobile.Models;
using Shink.Mobile.Services;

namespace Shink.Mobile.Pages;

[QueryProperty(nameof(ReturnUrl), "returnUrl")]
public sealed partial class PlansPage : ContentPage
{
    private static readonly Color PageBackgroundColor = Color.FromArgb("#FFF7E8");
    private static readonly Color TextColor = Color.FromArgb("#102C32");
    private static readonly Color MutedTextColor = Color.FromArgb("#4F5A5D");
    private static readonly Color AccentColor = Color.FromArgb("#003F46");
    private static readonly Color BorderColor = Color.FromArgb("#E8DCC9");
    private readonly MobileApiClient _apiClient;
    private readonly SessionState _sessionState;
    private readonly MobileAnalyticsService _analytics;
    private readonly IMobileStoreBillingService _storeBilling;
    private readonly VerticalStackLayout _content;
    private readonly VerticalStackLayout _body;
    private readonly NavigationGate _navigationGate = new();
    private readonly Dictionary<string, MobileStoreProduct> _storeProducts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (Border Card, RadioButton Radio)> _planControls = new(StringComparer.Ordinal);
    private IReadOnlyList<MobilePlan> _plans = [];
    private string? _selectedProductId;
    private Button? _purchaseButton;
    private Label? _renewalDisclosure;
    private bool _hasParentAccess;
    private bool _isLoading;
    private bool _isOpeningPlan;

    public PlansPage(MobileApiClient apiClient, SessionState sessionState, MobileAnalyticsService analytics,
        IMobileStoreBillingService storeBilling, StoryPlaybackSession storyPlaybackSession)
    {
        _apiClient = apiClient;
        _sessionState = sessionState;
        _analytics = analytics;
        _storeBilling = storeBilling;
        Title = "Opsies";
        AutomationId = "paywall-page";
        BackgroundColor = PageBackgroundColor;
        SafeAreaEdges = new SafeAreaEdges(SafeAreaRegions.Container);
        Shell.SetNavBarIsVisible(this, false);
        Shell.SetTabBarIsVisible(this, false);

        _body = new VerticalStackLayout { Padding = new Thickness(16, 0, 16, 12), Spacing = 6, SafeAreaEdges = SafeAreaEdges.None };
        _content = new VerticalStackLayout
        {
            Spacing = 0, SafeAreaEdges = SafeAreaEdges.None,
            BackgroundColor = PageBackgroundColor, MaximumWidthRequest = 580,
            HorizontalOptions = LayoutOptions.Center,
            Children = { new PaywallHeroView(() => _navigationGate.RunAsync(ClosePaywallAsync)), _body }
        };
        var scrollView = new ScrollView
        {
            BackgroundColor = PageBackgroundColor, Content = _content,
            SafeAreaEdges = SafeAreaEdges.None, HorizontalScrollBarVisibility = ScrollBarVisibility.Never
        };
        Content = PersistentPlaybackHost.Wrap(scrollView, storyPlaybackSession);
        SizeChanged += (_, _) => _content.WidthRequest = Math.Min(580, Math.Max(0, Width));
        RenderIntro();
    }

    public string? ReturnUrl { get; set; }
    private bool IsPaywall => !string.IsNullOrWhiteSpace(ReturnUrl);

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_sessionState.Current.HasPaidSubscription)
        {
            await Shell.Current.GoToAsync("//Luister", animate: true);
            return;
        }
        if (_isLoading) return;
        _isLoading = true;
        try
        {
            if (!await EnsureParentAccessAsync()) return;
            _analytics.TrackEvent(IsPaywall ? "mobile_paywall_viewed" : "mobile_plans_viewed",
                new Dictionary<string, object>
                {
                    ["has_return_path"] = IsPaywall,
                    ["is_signed_in"] = _sessionState.Current.IsSignedIn
                });
            await LoadAsync();
        }
        finally { _isLoading = false; }
    }

    private async Task<bool> EnsureParentAccessAsync()
    {
        if (_hasParentAccess) return true;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var challenge = PaywallParentChallenge.Create();
            var response = await DisplayPromptAsync("Vir ouers", challenge.Instruction,
                "Maak oop", "Terug", maxLength: 4, keyboard: Keyboard.Numeric);
            if (response is null) break;
            if (challenge.IsCorrect(response)) return _hasParentAccess = true;
            await DisplayAlertAsync("Probeer weer", "Die syfers stem nie ooreen nie.", "Reg so");
        }
        await ClosePaywallAsync();
        return false;
    }

    private async Task LoadAsync()
    {
        RenderIntro();
        _body.Children.Add(new ActivityIndicator { IsRunning = true, Color = AccentColor, HeightRequest = 40 });
        _body.Children.Add(BuildFooter());
        try
        {
            var response = await _apiClient.GetPlansAsync();
            var plans = (response?.Plans ?? Array.Empty<MobilePlan>())
                .Where(plan => plan.ProductId is "schink_stories_maandeliks" or "schink_stories_jaarliks")
                .OrderBy(plan => plan.BillingPeriodMonths).ToArray();
            _plans = plans;
            var products = await _storeBilling.GetProductsAsync(plans.Select(plan => plan.ProductId).ToArray());
            _storeProducts.Clear();
            foreach (var product in products) _storeProducts[product.ProductId] = product;
            if (_sessionState.Current.HasPaidSubscription)
            {
                RenderIntro();
                _body.Children.Add(BuildNotice("Jou volle toegang is reeds aktief. Jy hoef nie weer te betaal nie."));
                var continueButton = BuildTextLinkButton("Luister verder");
                continueButton.Clicked += async (_, _) => await OpenReturnPathAsync();
                _body.Children.Add(continueButton);
                return;
            }
            RenderIntro();
            if (plans.Length == 0)
                _body.Children.Add(BuildNotice("Geen planne is tans beskikbaar nie. Probeer asseblief weer."));
            else
            {
                if (!_plans.Any(p => p.ProductId == _selectedProductId && HasPrice(p.ProductId)))
                    _selectedProductId = plans.OrderByDescending(p => p.BillingPeriodMonths).FirstOrDefault(p => HasPrice(p.ProductId))?.ProductId;
                foreach (var plan in plans)
                {
                    _storeProducts.TryGetValue(plan.ProductId, out var product);
                    _body.Children.Add(BuildPlanOption(plan, product));
                }
                _purchaseButton = BuildPurchaseButton();
                _renewalDisclosure = new Label
                {
                    FontSize = 14, TextColor = MutedTextColor, HorizontalTextAlignment = TextAlignment.Center,
                    LineBreakMode = LineBreakMode.WordWrap, AutomationId = "paywall-renewal-disclosure",
                    Margin = new Thickness(0, 2)
                };
                _body.Children.Add(_purchaseButton);
                _body.Children.Add(_renewalDisclosure);
                UpdateSelection();
                if (_storeProducts.Count < plans.Length || plans.Any(p => !HasPrice(p.ProductId)))
                    _body.Children.Add(BuildNotice("Die winkelpryse is tans nie beskikbaar nie. Probeer asseblief weer voordat jy aankoop."));
            }
            if (plans.Length == 0 || plans.Any(p => !HasPrice(p.ProductId))) _body.Children.Add(BuildRetryButton());
            _body.Children.Add(BuildFooter());
        }
        catch (Exception)
        {
            RenderIntro();
            _body.Children.Add(BuildNotice("Die winkelprodukte kon nie nou gelaai word nie. Probeer asseblief weer."));
            _body.Children.Add(BuildRetryButton());
            _body.Children.Add(BuildFooter());
        }
    }

    private bool HasPrice(string productId) => _storeProducts.TryGetValue(productId, out var product) &&
        !string.IsNullOrWhiteSpace(product.LocalizedPrice);

    private async Task OpenPlanAsync(MobilePlan plan, MobileStoreProduct product)
    {
        if (_isOpeningPlan) return;
        if (_sessionState.Current.HasPaidSubscription)
        {
            await OpenReturnPathAsync();
            return;
        }

        _isOpeningPlan = true;
        UpdateSelection();
        try
        {
            if (!_sessionState.Current.IsSignedIn)
            {
                await DisplayAlertAsync(
                    "Teken eers in",
                    "Skep of teken in op jou rekening voordat jy 'n plan kies.",
                    "Reg so");
                await Shell.Current.GoToAsync(nameof(AccountPage), animate: true);
                return;
            }

            _analytics.TrackEvent("mobile_plan_selected", new Dictionary<string, object>
            {
                ["plan_slug"] = plan.Slug,
                ["is_signed_in"] = _sessionState.Current.IsSignedIn,
                ["is_paywall"] = IsPaywall,
                ["store_product_id"] = product.ProductId
            });

            var purchaseEmail = _sessionState.Current.Email;
            var purchaseResult = await _storeBilling.PurchaseAsync(
                product.ProductId,
                _sessionState.Current.Email);
            if (purchaseResult.IsCancelled)
            {
                return;
            }

            if (purchaseResult.IsPending)
            {
                await DisplayAlertAsync("Betaling wag", purchaseResult.ErrorMessage ?? "Die betaling wag nog op bevestiging.", "Reg so");
                return;
            }

            if (!purchaseResult.IsSuccess || purchaseResult.Purchase is null)
            {
                await DisplayAlertAsync(
                    "Aankoop kon nie voltooi word nie",
                    purchaseResult.ErrorMessage ?? "Probeer asseblief weer.",
                    "Reg so");
                return;
            }

            if (!_sessionState.Current.IsSignedIn || !string.Equals(purchaseEmail, _sessionState.Current.Email, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Die aankooprekening het verander.");
            var entitlement = await SyncPurchaseAsync(purchaseResult.Purchase);

            if (entitlement is null || !entitlement.IsActive)
            {
                await DisplayAlertAsync(
                    "Aankoop word bevestig",
                    "Die winkel het jou aankoop ontvang. Ons sal die bevestiging weer probeer wanneer die app oop is. Jy kan ook Herstel aankoop kies.",
                    "Reg so");
                return;
            }

            await FinalizePurchaseAsync(purchaseResult.Purchase);

            await _apiClient.GetSessionAsync();
            await OpenReturnPathAsync();
        }
        catch (Exception ex)
        {
            _analytics.TrackException(ex, "mobile_plan_open_failed", new Dictionary<string, object>
            {
                ["plan_slug"] = plan.Slug,
                ["store_product_id"] = product.ProductId
            });
            await DisplayAlertAsync("Aankoop kon nie voltooi word nie", "Die winkelbetaling kon nie nou voltooi word nie. Probeer asseblief weer.", "Reg so");
        }
        finally
        {
            _isOpeningPlan = false;
            UpdateSelection();
        }
    }

    private async Task RestorePurchasesAsync()
    {
        if (_isOpeningPlan)
        {
            return;
        }

        if (!_sessionState.Current.IsSignedIn)
        {
            await DisplayAlertAsync(
                "Teken eers in",
                "Skep of teken in op jou rekening voordat jy 'n aankoop herstel.",
                "Reg so");
            await Shell.Current.GoToAsync(nameof(AccountPage), animate: true);
            return;
        }

        _isOpeningPlan = true;
        UpdateSelection();
        try
        {
            var purchases = await _storeBilling.RestoreAsync();
            var recovery = await StorePurchaseRecovery.RecoverAsync(
                purchases.Where(p => p.ProductId is "schink_stories_maandeliks" or "schink_stories_jaarliks"), async purchase =>
                {
                    var entitlement = await SyncPurchaseAsync(purchase);
                    var retry = entitlement is null || entitlement.IsRetryable;
                    if (!retry) retry = !await FinalizePurchaseAsync(purchase);
                    return new StoreDeliveryOutcome(entitlement?.IsActive == true, retry);
                });

            await _apiClient.GetSessionAsync();
            if (recovery.ActivePurchases > 0 && _sessionState.Current.HasPaidSubscription)
            {
                await OpenReturnPathAsync();
                return;
            }

            if (recovery.RetryPurchases > 0)
            {
                await DisplayAlertAsync("Herstel word probeer", "Sommige aankope kon nie nou bevestig word nie. Ons sal weer probeer wanneer die app oop is.", "Reg so");
                return;
            }
            await DisplayAlertAsync(
                "Geen aankoop gevind nie",
                "Daar is geen aktiewe winkelintekening vir hierdie rekening gevind nie.",
                "Reg so");
        }
        catch (Exception ex)
        {
            _analytics.TrackException(ex, "mobile_store_restore_failed");
            await DisplayAlertAsync("Aankoop kon nie herstel word nie", "Die winkel kon nie jou aankoop nou herstel nie. Probeer asseblief weer.", "Reg so");
        }
        finally
        {
            _isOpeningPlan = false;
            UpdateSelection();
        }
    }

    private async Task<MobileStoreEntitlementResponse?> SyncPurchaseAsync(MobileStorePurchase purchase)
    {
        var request = new MobileStorePurchaseRequest(
            purchase.Provider,
            purchase.ProductId,
            purchase.ProviderPaymentId,
            purchase.ProviderTransactionId,
            purchase.ProviderToken,
            _sessionState.Current.Email);
        var entitlement = await _apiClient.SyncStorePurchaseAsync(request);
        _analytics.TrackEvent("mobile_store_purchase_synced", new Dictionary<string, object>
        {
            ["provider"] = purchase.Provider,
            ["product_id"] = purchase.ProductId,
            ["is_active"] = entitlement?.IsActive ?? false
        });
        return entitlement;
    }

    private async Task<bool> FinalizePurchaseAsync(MobileStorePurchase purchase)
    {
        var finalized = await _storeBilling.FinalizeAsync(purchase);
        if (!finalized)
        {
            _analytics.TrackEvent("mobile_store_purchase_finalize_failed", new Dictionary<string, object>
            {
                ["provider"] = purchase.Provider,
                ["product_id"] = purchase.ProductId
            });
        }
        return finalized;
    }

    private async Task OpenReturnPathAsync()
    {
        if (PageHelpers.TryBuildStoryDetailRoute(ReturnUrl, out var storyRoute))
        {
            await Shell.Current.GoToAsync($"../{storyRoute}", animate: true);
            return;
        }

        await Shell.Current.GoToAsync("//Luister", animate: true);
    }

    private Task ClosePaywallAsync() =>
        _sessionState.Current.IsSignedIn
            ? Shell.Current.GoToAsync("//Luister", animate: true)
            : Shell.Current.GoToAsync("..", animate: true);

    protected override bool OnBackButtonPressed()
    {
        if (!IsPaywall)
        {
            return base.OnBackButtonPressed();
        }

        _ = _navigationGate.RunAsync(ClosePaywallAsync);
        return true;
    }
}
