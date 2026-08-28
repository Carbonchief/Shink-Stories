using Shink.Mobile.Models;
using Shink.Mobile.Services;

namespace Shink.Mobile.Pages;

[QueryProperty(nameof(ReturnUrl), "returnUrl")]
public sealed class PlansPage : ContentPage
{
    private static readonly Color PageBackgroundColor = Color.FromArgb("#FFF7E8");
    private static readonly Color TextColor = Color.FromArgb("#1B2231");
    private static readonly Color MutedTextColor = Color.FromArgb("#69716D");
    private static readonly Color AccentColor = Color.FromArgb("#123F3F");
    private static readonly Color GoldColor = Color.FromArgb("#E8B52F");

    private readonly MobileApiClient _apiClient;
    private readonly SessionState _sessionState;
    private readonly MobileAnalyticsService _analytics;
    private readonly IMobileStoreBillingService _storeBilling;
    private readonly VerticalStackLayout _content;
    private readonly NavigationGate _navigationGate = new();
    private readonly Dictionary<string, MobileStoreProduct> _storeProducts = new(StringComparer.Ordinal);
    private bool _hasLoaded;
    private bool _isOpeningPlan;

    public PlansPage(
        MobileApiClient apiClient,
        SessionState sessionState,
        MobileAnalyticsService analytics,
        IMobileStoreBillingService storeBilling,
        StoryPlaybackSession storyPlaybackSession)
    {
        _apiClient = apiClient;
        _sessionState = sessionState;
        _analytics = analytics;
        _storeBilling = storeBilling;

        Title = "Opsies";
        BackgroundColor = PageBackgroundColor;
        SafeAreaEdges = new SafeAreaEdges(SafeAreaRegions.Container);
        Shell.SetNavBarIsVisible(this, false);
        Shell.SetTabBarIsVisible(this, false);

        _content = new VerticalStackLayout
        {
            Padding = new Thickness(20, 18, 20, 30),
            Spacing = 18
        };
        MobileResponsiveLayout.ApplyCenteredContent(_content, Width, 820);
        SizeChanged += (_, _) => MobileResponsiveLayout.ApplyCenteredContent(_content, Width, 820);

        var scrollView = new ScrollView
        {
            BackgroundColor = PageBackgroundColor,
            Content = _content
        };
        Content = PersistentPlaybackHost.Wrap(scrollView, storyPlaybackSession);
    }

    public string? ReturnUrl { get; set; }

    private bool IsPaywall => !string.IsNullOrWhiteSpace(ReturnUrl);

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_hasLoaded)
        {
            return;
        }

        _hasLoaded = true;
        _analytics.TrackEvent(
            IsPaywall ? "mobile_paywall_viewed" : "mobile_plans_viewed",
            new Dictionary<string, object>
            {
                ["has_return_path"] = IsPaywall,
                ["is_signed_in"] = _sessionState.Current.IsSignedIn
            });
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        _content.Children.Clear();
        _content.Children.Add(BuildHeader());
        _content.Children.Add(BuildIntro());
        _content.Children.Add(new ActivityIndicator
        {
            IsRunning = true,
            Color = AccentColor,
            HorizontalOptions = LayoutOptions.Center
        });

        try
        {
            var response = await _apiClient.GetPlansAsync();
            var plans = (response?.Plans ?? Array.Empty<MobilePlan>())
                .Where(plan => !plan.Slug.StartsWith("skool-", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var storeProducts = await _storeBilling.GetProductsAsync(
                plans.Select(plan => plan.ProductId).ToArray());
            _storeProducts.Clear();
            foreach (var product in storeProducts)
            {
                _storeProducts[product.ProductId] = product;
            }

            _content.Children.Clear();
            _content.Children.Add(BuildHeader());
            _content.Children.Add(BuildIntro());

            if (plans.Length == 0)
            {
                _content.Children.Add(BuildNotice("Geen planne is tans beskikbaar nie. Probeer asseblief weer."));
                _content.Children.Add(BuildRestoreButton());
                return;
            }

            foreach (var plan in plans)
            {
                _storeProducts.TryGetValue(plan.ProductId, out var product);
                _content.Children.Add(BuildPlanCard(plan, product));
            }

            if (_storeProducts.Count < plans.Length)
            {
                _content.Children.Add(BuildNotice("Die winkelpryse is tans nie beskikbaar nie. Probeer asseblief weer voordat jy aankoop."));
            }

            _content.Children.Add(new Label
            {
                Text = "Betaling word veilig deur die App Store of Google Play voltooi.",
                FontSize = 13,
                TextColor = MutedTextColor,
                HorizontalTextAlignment = TextAlignment.Center,
                Margin = new Thickness(8, 0)
            });
            _content.Children.Add(BuildRestoreButton());
        }
        catch (Exception)
        {
            _content.Children.Clear();
            _content.Children.Add(BuildHeader());
            _content.Children.Add(BuildIntro());
            _content.Children.Add(BuildNotice("Die winkelprodukte kon nie nou gelaai word nie. Probeer asseblief weer."));
            _content.Children.Add(BuildRestoreButton());
        }
    }

    private View BuildHeader()
    {
        var backButton = new Border
        {
            BackgroundColor = Colors.White,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 23 },
            WidthRequest = 46,
            HeightRequest = 46,
            Content = new Label
            {
                Text = "‹",
                FontSize = 34,
                TextColor = AccentColor,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, -4, 0, 0)
            }
        };
        var backTap = new TapGestureRecognizer();
        backTap.Tapped += async (_, _) => await _navigationGate.RunAsync(() => Shell.Current.GoToAsync("..", animate: true));
        backButton.GestureRecognizers.Add(backTap);

        var title = new Label
        {
            Text = IsPaywall ? "Maak stories oop" : "Kies 'n plan",
            FontSize = 26,
            FontAttributes = FontAttributes.Bold,
            TextColor = TextColor,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center
        };

        var spacer = new BoxView
        {
            WidthRequest = 46,
            HeightRequest = 46,
            Opacity = 0
        };

        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            Children =
            {
                backButton,
                title,
                spacer
            }
        };
        Grid.SetColumn(title, 1);
        Grid.SetColumn(spacer, 2);
        return grid;
    }

    private View BuildIntro() =>
        new Border
        {
            BackgroundColor = AccentColor,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 28 },
            Padding = new Thickness(22, 24),
            Content = new VerticalStackLayout
            {
                Spacing = 8,
                Children =
                {
                    new Label
                    {
                        Text = IsPaywall ? "Jou storietyd wag." : "Rustige storietyd vir jou gesin.",
                        FontSize = 24,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = Colors.White
                    },
                    new Label
                    {
                        Text = IsPaywall
                            ? "Kies 'n opsie om hierdie storie en nog baie meer oop te maak."
                            : "Kies die opsie wat by jou gesin pas. Alle opsies hieronder is vir huishoudings.",
                        FontSize = 15,
                        TextColor = Color.FromArgb("#DDEDE8")
                    }
                }
            }
        };

    private View BuildPlanCard(MobilePlan plan, MobileStoreProduct? product)
    {
        var isYearly = plan.BillingPeriodMonths >= 12;
        var hasStoreProduct = product is not null;
        var actionButton = new Button
        {
            Text = hasStoreProduct
                ? (_sessionState.Current.IsSignedIn ? "Kies hierdie plan" : "Skep rekening")
                : "Tans nie beskikbaar nie",
            BackgroundColor = hasStoreProduct
                ? (isYearly ? GoldColor : AccentColor)
                : Color.FromArgb("#E7E1D7"),
            TextColor = hasStoreProduct
                ? (isYearly ? TextColor : Colors.White)
                : MutedTextColor,
            FontAttributes = FontAttributes.Bold,
            CornerRadius = 22,
            HeightRequest = 50,
            IsEnabled = hasStoreProduct,
            Opacity = hasStoreProduct ? 1 : 0.78
        };
        if (product is not null)
        {
            actionButton.Clicked += async (_, _) => await OpenPlanAsync(plan, product);
        }

        var card = new Border
        {
            BackgroundColor = Colors.White,
            Stroke = isYearly ? GoldColor : Color.FromArgb("#E9E1D0"),
            StrokeThickness = isYearly ? 2 : 1,
            StrokeShape = new RoundRectangle { CornerRadius = 26 },
            Padding = new Thickness(20),
            Content = new VerticalStackLayout
            {
                Spacing = 10,
                Children =
                {
                    isYearly
                        ? new Label
                        {
                            Text = "Beste waarde",
                            FontSize = 12,
                            FontAttributes = FontAttributes.Bold,
                            TextColor = Color.FromArgb("#8A6400")
                        }
                        : new BoxView { HeightRequest = 0 },
                    new Label
                    {
                        Text = plan.Name,
                        FontSize = 22,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = TextColor
                    },
                    new HorizontalStackLayout
                    {
                        Spacing = 5,
                        Children =
                        {
                            new Label
                            {
                                Text = product?.LocalizedPrice ?? "Prys word gelaai",
                                FontSize = 24,
                                FontAttributes = FontAttributes.Bold,
                                TextColor = hasStoreProduct ? AccentColor : MutedTextColor
                            },
                            new Label
                            {
                                Text = isYearly ? "per jaar" : "per maand",
                                FontSize = 14,
                                TextColor = MutedTextColor,
                                VerticalTextAlignment = TextAlignment.End,
                                Margin = new Thickness(0, 0, 0, 3)
                            }
                        }
                    },
                    new Label
                    {
                        Text = plan.Description,
                        FontSize = 14,
                        TextColor = MutedTextColor
                    },
                    actionButton
                }
            }
        };
        MobileResponsiveLayout.ApplyCenteredContent(card, Width, 720);
        return card;
    }

    private View BuildRestoreButton()
    {
        var restoreButton = new Button
        {
            Text = "Herstel aankoop",
            BackgroundColor = Colors.Transparent,
            TextColor = AccentColor,
            FontAttributes = FontAttributes.Bold,
            HeightRequest = 46
        };
        restoreButton.Clicked += async (_, _) => await RestorePurchasesAsync();
        return restoreButton;
    }

    private static View BuildNotice(string message) =>
        new Border
        {
            BackgroundColor = Colors.White,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 22 },
            Padding = 20,
            Content = new Label
            {
                Text = message,
                FontSize = 15,
                TextColor = MutedTextColor,
                HorizontalTextAlignment = TextAlignment.Center
            }
        };

    private async Task OpenPlanAsync(MobilePlan plan, MobileStoreProduct product)
    {
        if (_isOpeningPlan)
        {
            return;
        }

        _isOpeningPlan = true;
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

            MobileStoreEntitlementResponse? entitlement;
            try
            {
                entitlement = await SyncPurchaseAsync(purchaseResult.Purchase);
            }
            finally
            {
                await FinalizePurchaseAsync(purchaseResult.Purchase);
            }

            if (entitlement is null || !entitlement.IsActive)
            {
                await DisplayAlertAsync(
                    "Aankoop word bevestig",
                    "Die winkel het jou aankoop ontvang. Jou toegang sal oopmaak sodra die bevestiging voltooi is.",
                    "Reg so");
                return;
            }

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
        try
        {
            var purchases = await _storeBilling.RestoreAsync();
            var activePurchases = 0;
            foreach (var purchase in purchases)
            {
                var entitlement = await SyncPurchaseAsync(purchase);
                if (entitlement?.IsActive == true)
                {
                    activePurchases++;
                    await FinalizePurchaseAsync(purchase);
                }
            }

            await _apiClient.GetSessionAsync();
            if (activePurchases > 0 && _sessionState.Current.HasPaidSubscription)
            {
                await OpenReturnPathAsync();
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
            purchase.ReceiptData);
        var entitlement = await _apiClient.SyncStorePurchaseAsync(request);
        _analytics.TrackEvent("mobile_store_purchase_synced", new Dictionary<string, object>
        {
            ["provider"] = purchase.Provider,
            ["product_id"] = purchase.ProductId,
            ["is_active"] = entitlement?.IsActive ?? false
        });
        return entitlement;
    }

    private async Task FinalizePurchaseAsync(MobileStorePurchase purchase)
    {
        if (!await _storeBilling.FinalizeAsync(purchase))
        {
            _analytics.TrackEvent("mobile_store_purchase_finalize_failed", new Dictionary<string, object>
            {
                ["provider"] = purchase.Provider,
                ["product_id"] = purchase.ProductId
            });
        }
    }

    private async Task OpenReturnPathAsync()
    {
        if (!string.IsNullOrWhiteSpace(ReturnUrl))
        {
            await Shell.Current.GoToAsync(ReturnUrl, animate: true);
            return;
        }

        await Shell.Current.GoToAsync("//Luister", animate: true);
    }
}
