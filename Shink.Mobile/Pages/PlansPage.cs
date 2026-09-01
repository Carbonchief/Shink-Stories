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
    private static readonly Color SoftGoldColor = Color.FromArgb("#FFF3CF");
    private static readonly Color BorderColor = Color.FromArgb("#E9E1D0");

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
                .Where(plan => plan.ProductId is "schink_stories_maandeliks" or "schink_stories_jaarliks")
                .OrderBy(plan => plan.BillingPeriodMonths)
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

            if (_sessionState.Current.HasFullStoryAccess)
            {
                _content.Children.Add(BuildActiveAccessCard());
                _content.Children.Add(BuildPurchaseDetails());
                _content.Children.Add(BuildLegalLinks());
                return;
            }

            if (plans.Length == 0)
            {
                _content.Children.Add(BuildNotice("Geen planne is tans beskikbaar nie. Probeer asseblief weer."));
                _content.Children.Add(BuildRestoreButton());
                _content.Children.Add(BuildPurchaseDetails());
                _content.Children.Add(BuildLegalLinks());
                return;
            }

            var monthlyPlan = plans.FirstOrDefault(plan => plan.BillingPeriodMonths == 1);
            var yearlyPlan = plans.FirstOrDefault(plan => plan.BillingPeriodMonths >= 12);
            var yearlySaving = monthlyPlan is not null && yearlyPlan is not null
                ? Math.Max(0, (monthlyPlan.Amount * 12) - yearlyPlan.Amount)
                : 0;
            foreach (var plan in plans)
            {
                _storeProducts.TryGetValue(plan.ProductId, out var product);
                _content.Children.Add(BuildPlanCard(plan, product, yearlySaving));
            }

            if (_storeProducts.Count < plans.Length)
            {
                _content.Children.Add(BuildNotice("Die winkelpryse is tans nie beskikbaar nie. Probeer asseblief weer voordat jy aankoop."));
            }

            _content.Children.Add(BuildRestoreButton());
            _content.Children.Add(BuildPurchaseDetails());
            _content.Children.Add(BuildLegalLinks());
        }
        catch (Exception)
        {
            _content.Children.Clear();
            _content.Children.Add(BuildHeader());
            _content.Children.Add(BuildIntro());
            _content.Children.Add(BuildNotice("Die winkelprodukte kon nie nou gelaai word nie. Probeer asseblief weer."));
            _content.Children.Add(BuildRestoreButton());
            _content.Children.Add(BuildPurchaseDetails());
            _content.Children.Add(BuildLegalLinks());
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
        backTap.Tapped += async (_, _) => await _navigationGate.RunAsync(ClosePaywallAsync);
        backButton.GestureRecognizers.Add(backTap);

        var title = new Label
        {
            Text = IsPaywall ? "Maak die storie oop" : "Schink Stories",
            FontSize = 24,
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

    private View BuildIntro()
    {
        var benefits = new VerticalStackLayout { Spacing = 10 };
        benefits.Children.Add(BuildBenefitRow("✓", "Onbeperkte toegang tot alle stories en reekse"));
        benefits.Children.Add(BuildBenefitRow("✓", "Veilige, advertensievrye luistertyd"));
        benefits.Children.Add(BuildBenefitRow("✓", "Gebruik dieselfde rekening op die app en webwerf"));
        benefits.Children.Add(BuildBenefitRow("✓", "Laai stories af om later te luister"));

        return new Border
        {
            BackgroundColor = AccentColor,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 28 },
            Padding = new Thickness(22, 24),
            Content = new VerticalStackLayout
            {
                Spacing = 16,
                Children =
                {
                    new Image
                    {
                        Source = "schink_stories_logo_white.png",
                        HeightRequest = 44,
                        Aspect = Aspect.AspectFit,
                        HorizontalOptions = LayoutOptions.Start
                    },
                    new Label
                    {
                        Text = IsPaywall ? "Jou volgende storie wag" : "Al die stories. Een eenvoudige plan.",
                        FontSize = 27,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = Colors.White
                    },
                    new Label
                    {
                        Text = IsPaywall
                            ? "Kies maandeliks of jaarliks en luister dadelik verder."
                            : "Kies die opsie wat by jou gesin pas.",
                        FontSize = 15,
                        TextColor = Color.FromArgb("#DDEDE8")
                    },
                    benefits
                }
            }
        };
    }

    private static View BuildBenefitRow(string marker, string text)
    {
        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star)
            },
            ColumnSpacing = 10
        };
        grid.Children.Add(new Label
        {
            Text = marker,
            FontSize = 15,
            FontAttributes = FontAttributes.Bold,
            TextColor = GoldColor
        });
        var label = new Label
        {
            Text = text,
            FontSize = 14,
            TextColor = Colors.White,
            LineBreakMode = LineBreakMode.WordWrap
        };
        grid.Children.Add(label);
        Grid.SetColumn(label, 1);
        return grid;
    }

    private View BuildPlanCard(MobilePlan plan, MobileStoreProduct? product, decimal yearlySaving)
    {
        var isYearly = plan.BillingPeriodMonths >= 12;
        var hasStoreProduct = product is not null;
        var displayPrice = product?.LocalizedPrice;
        if (string.IsNullOrWhiteSpace(displayPrice))
        {
            displayPrice = $"R{plan.Amount:0}";
        }

        var actionButton = new Button
        {
            Text = hasStoreProduct
                ? (_sessionState.Current.IsSignedIn
                    ? (isYearly ? "Kies jaarliks" : "Kies maandeliks")
                    : "Teken in om voort te gaan")
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
        actionButton.AutomationId = isYearly ? "paywall-yearly-button" : "paywall-monthly-button";
        SemanticProperties.SetDescription(
            actionButton,
            $"{plan.Name}, {displayPrice} {(isYearly ? "per jaar" : "per maand")}");
        if (product is not null)
        {
            actionButton.Clicked += async (_, _) => await OpenPlanAsync(plan, product);
        }

        var heading = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 10
        };
        heading.Children.Add(new Label
        {
            Text = isYearly ? "Jaarliks" : "Maandeliks",
            FontSize = 20,
            FontAttributes = FontAttributes.Bold,
            TextColor = TextColor
        });
        if (isYearly)
        {
            var badge = new Border
            {
                BackgroundColor = SoftGoldColor,
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = 999 },
                Padding = new Thickness(10, 5),
                Content = new Label
                {
                    Text = "BESTE WAARDE",
                    FontSize = 10,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Color.FromArgb("#765500")
                }
            };
            heading.Children.Add(badge);
            Grid.SetColumn(badge, 1);
        }

        var priceRow = new HorizontalStackLayout
        {
            Spacing = 6,
            Children =
            {
                new Label
                {
                    Text = displayPrice,
                    FontSize = 30,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = hasStoreProduct ? AccentColor : MutedTextColor
                },
                new Label
                {
                    Text = isYearly ? "/ jaar" : "/ maand",
                    FontSize = 14,
                    TextColor = MutedTextColor,
                    VerticalTextAlignment = TextAlignment.End,
                    Margin = new Thickness(0, 0, 0, 5)
                }
            }
        };

        var cardContent = new VerticalStackLayout
        {
            Spacing = 12,
            Children =
            {
                heading,
                priceRow,
                new Label
                {
                    Text = plan.Description,
                    FontSize = 14,
                    TextColor = MutedTextColor
                }
            }
        };
        if (isYearly && yearlySaving > 0)
        {
            cardContent.Children.Add(new Label
            {
                Text = $"Spaar R{yearlySaving:0} teenoor 12 maande se maandbetalings.",
                FontSize = 13,
                FontAttributes = FontAttributes.Bold,
                TextColor = Color.FromArgb("#765500")
            });
        }
        cardContent.Children.Add(actionButton);

        var card = new Border
        {
            BackgroundColor = Colors.White,
            Stroke = isYearly ? GoldColor : BorderColor,
            StrokeThickness = isYearly ? 2 : 1,
            StrokeShape = new RoundRectangle { CornerRadius = 26 },
            Padding = new Thickness(20),
            Content = cardContent
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

    private View BuildActiveAccessCard()
    {
        var continueButton = new Button
        {
            Text = IsPaywall ? "Luister verder" : "Gaan na stories",
            BackgroundColor = GoldColor,
            TextColor = TextColor,
            FontAttributes = FontAttributes.Bold,
            CornerRadius = 23,
            HeightRequest = 50,
            AutomationId = "paywall-active-access-button"
        };
        continueButton.Clicked += async (_, _) => await OpenReturnPathAsync();

        return new Border
        {
            BackgroundColor = Colors.White,
            Stroke = GoldColor,
            StrokeThickness = 2,
            StrokeShape = new RoundRectangle { CornerRadius = 26 },
            Padding = 20,
            Content = new VerticalStackLayout
            {
                Spacing = 12,
                Children =
                {
                    new Label
                    {
                        Text = "✓ Jou volle toegang is reeds aktief",
                        FontSize = 20,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = AccentColor
                    },
                    new Label
                    {
                        Text = "Hierdie rekening werk reeds op die Schink Stories-app en webwerf. Jy hoef nie weer te betaal nie.",
                        FontSize = 14,
                        TextColor = MutedTextColor
                    },
                    continueButton
                }
            }
        };
    }

    private static View BuildPurchaseDetails() =>
        new Border
        {
            BackgroundColor = Colors.White,
            Stroke = BorderColor,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 22 },
            Padding = 18,
            Content = new VerticalStackLayout
            {
                Spacing = 8,
                Children =
                {
                    new Label
                    {
                        Text = "Hoe dit werk",
                        FontSize = 15,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = TextColor
                    },
                    new Label
                    {
                        Text = "Betaling word veilig deur die App Store of Google Play voltooi. Jou intekening hernu outomaties tensy jy dit minstens 24 uur voor die einde van die huidige tydperk kanselleer. Bestuur of kanselleer dit in jou winkelrekening.",
                        FontSize = 12,
                        TextColor = MutedTextColor,
                        LineBreakMode = LineBreakMode.WordWrap
                    },
                    new Label
                    {
                        Text = "Jou Schink-rekening hou toegang op die app en www.schink.co.za in pas.",
                        FontSize = 12,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = AccentColor
                    }
                }
            }
        };

    private View BuildLegalLinks()
    {
        var termsButton = BuildTextLinkButton("Terme en voorwaardes");
        termsButton.Clicked += async (_, _) => await Launcher.Default.OpenAsync(
            new Uri(_apiClient.BuildAbsoluteUrl("/terme-en-voorwaardes")));

        var privacyButton = BuildTextLinkButton("Privaatheidsbeleid");
        privacyButton.Clicked += async (_, _) => await Launcher.Default.OpenAsync(
            new Uri(_apiClient.BuildAbsoluteUrl("/privaatheidsbeleid")));

        return new VerticalStackLayout
        {
            Spacing = 0,
            HorizontalOptions = LayoutOptions.Center,
            Children = { termsButton, privacyButton }
        };
    }

    private static Button BuildTextLinkButton(string text) =>
        new()
        {
            Text = text,
            BackgroundColor = Colors.Transparent,
            TextColor = AccentColor,
            FontSize = 13,
            FontAttributes = FontAttributes.Bold,
            HeightRequest = 38,
            Padding = new Thickness(10, 0)
        };

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

            var entitlement = await SyncPurchaseAsync(purchaseResult.Purchase);

            if (entitlement is null || !entitlement.IsActive)
            {
                await DisplayAlertAsync(
                    "Aankoop word bevestig",
                    "Die winkel het jou aankoop ontvang. Jou toegang sal oopmaak sodra die bevestiging voltooi is.",
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
            if (activePurchases > 0 && _sessionState.Current.HasFullStoryAccess)
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
            purchase.ProviderToken);
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
        if (PageHelpers.TryBuildStoryDetailRoute(ReturnUrl, out var storyRoute))
        {
            await Shell.Current.GoToAsync($"../{storyRoute}", animate: true);
            return;
        }

        await Shell.Current.GoToAsync("//Luister", animate: true);
    }

    private Task ClosePaywallAsync() =>
        IsPaywall
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
