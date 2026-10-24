using Microsoft.Maui.Controls.Shapes;
using Shink.Mobile.Models;
using Shink.Mobile.Services;

namespace Shink.Mobile.Pages;

public sealed partial class PlansPage
{
    private void RenderIntro()
    {
        _body.Children.Clear();
        _planControls.Clear();
        _purchaseButton = null;
        _renewalDisclosure = null;
        var title = new Label
        {
            Text = "Klein ore.\nGroot avonture.", FontFamily = "SchinkOnboardingRounded",
            FontSize = 36, FontAttributes = FontAttributes.Bold, LineHeight = 0.97,
            TextColor = AccentColor, HorizontalTextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 0, 0, 0), AutomationId = "paywall-headline"
        };
        SemanticProperties.SetHeadingLevel(title, SemanticHeadingLevel.Level1);
        _body.Children.Add(title);
        _body.Children.Add(new Label
        {
            Text = "Al die Schink Stories. Elke dag saam.", FontSize = 16,
            FontAttributes = FontAttributes.Bold, TextColor = TextColor,
            HorizontalTextAlignment = TextAlignment.Center
        });
        var benefits = new VerticalStackLayout { Spacing = 6, Margin = new Thickness(12, 4, 4, 5) };
        benefits.Children.Add(BuildBenefit("\uf518", "Onbeperkte stories en reekse"));
        benefits.Children.Add(BuildBenefit("\uf3ed", "Veilig en advertensievry"));
        benefits.Children.Add(BuildBenefit("\uf019", "Laai af en luister vanlyn"));
        _body.Children.Add(benefits);
    }

    private static View BuildBenefit(string icon, string text)
    {
        var badge = new Border
        {
            WidthRequest = 30, HeightRequest = 30, StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 15 },
            BackgroundColor = Color.FromArgb("#B8EBDF"),
            Content = new Label
            {
                Text = icon, FontFamily = "FontAwesomeSolid", FontSize = 17,
                TextColor = AccentColor, HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center
            }
        };
        var row = new Grid
        {
            ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star) }, ColumnSpacing = 12
        };
        row.Add(badge);
        row.Add(new Label { Text = text, FontSize = 15, TextColor = TextColor, VerticalTextAlignment = TextAlignment.Center }, 1);
        return row;
    }

    private View BuildPlanOption(MobilePlan plan, MobileStoreProduct? product)
    {
        var yearly = plan.BillingPeriodMonths >= 12;
        var available = !string.IsNullOrWhiteSpace(product?.LocalizedPrice);
        var radio = new RadioButton
        {
            GroupName = $"paywall-{GetHashCode()}", IsChecked = plan.ProductId == _selectedProductId,
            IsEnabled = available, WidthRequest = 28, MinimumHeightRequest = 44,
            VerticalOptions = LayoutOptions.Center, HorizontalOptions = LayoutOptions.Center,
            AutomationId = yearly ? "paywall-yearly-option" : "paywall-monthly-option"
        };
        SemanticProperties.SetDescription(radio, $"{(yearly ? "Jaarliks" : "Maandeliks")}, {product?.LocalizedPrice ?? "Prys nie beskikbaar nie"}, {PaywallPresentation.Period(plan.BillingPeriodMonths)}");
        radio.CheckedChanged += (_, args) => { if (args.Value) SelectPlan(plan.ProductId); };
        var heading = new FlexLayout { Direction = Microsoft.Maui.Layouts.FlexDirection.Row, Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap, AlignItems = Microsoft.Maui.Layouts.FlexAlignItems.Center };
        heading.Children.Add(new Label
        {
            Text = yearly ? "Jaarliks" : "Maandeliks", FontSize = 16,
            FontAttributes = FontAttributes.Bold, TextColor = TextColor, Margin = new Thickness(0, 0, 6, 0)
        });
        if (yearly && HasAnnualValue()) heading.Children.Add(new Border
        {
            StrokeThickness = 0, BackgroundColor = Color.FromArgb("#F3CC52"),
            StrokeShape = new RoundRectangle { CornerRadius = 10 }, Padding = new Thickness(7, 2),
            Content = new Label { Text = "Beste waarde", FontSize = 10, TextColor = TextColor, FontAttributes = FontAttributes.Bold }
        });
        var words = new VerticalStackLayout
        {
            Spacing = 1, VerticalOptions = LayoutOptions.Center,
            Children =
            {
                heading,
                new Label
                {
                    Text = yearly ? "Een betaling elke 12 maande" : "Een betaling elke maand",
                    FontSize = 12, TextColor = MutedTextColor
                }
            }
        };
        var price = new Label
        {
            FontSize = 16, TextColor = available ? TextColor : MutedTextColor,
            VerticalTextAlignment = TextAlignment.Center, HorizontalTextAlignment = TextAlignment.End,
            FormattedText = new FormattedString
            {
                Spans =
                {
                    new Span { Text = available ? product!.LocalizedPrice : "Prys nie beskikbaar nie", FontAttributes = FontAttributes.Bold },
                    new Span { Text = available ? (yearly ? " / jaar" : " / maand") : "" }
                }
            },
            MaximumWidthRequest = 160
        };
        var row = new Grid
        {
            ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) }, ColumnSpacing = 9,
            MinimumHeightRequest = 48
        };
        row.Add(radio); row.Add(words, 1); row.Add(price, 2);
        var card = new Border
        {
            StrokeShape = new RoundRectangle { CornerRadius = 12 }, StrokeThickness = 1,
            Stroke = BorderColor, Padding = new Thickness(10, 5), Content = row,
            BackgroundColor = Colors.White, Opacity = available ? 1 : 0.7
        };
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => SelectPlan(plan.ProductId);
        card.GestureRecognizers.Add(tap);
        _planControls[plan.ProductId] = (card, radio);
        return card;
    }

    private bool HasAnnualValue()
    {
        var monthly = _plans.FirstOrDefault(p => p.BillingPeriodMonths == 1);
        var yearly = _plans.FirstOrDefault(p => p.BillingPeriodMonths == 12);
        if (monthly is null || yearly is null || !HasPrice(monthly.ProductId) || !HasPrice(yearly.ProductId) ||
            !_storeProducts.TryGetValue(monthly.ProductId, out var m) ||
            !_storeProducts.TryGetValue(yearly.ProductId, out var y)) return false;
        return PaywallPresentation.IsAnnualBetterValue(m.MicrosPrice, m.CurrencyCode, y.MicrosPrice, y.CurrencyCode);
    }

    private void SelectPlan(string productId)
    {
        if (_isOpeningPlan || !HasPrice(productId) || _selectedProductId == productId) return;
        _selectedProductId = productId;
        UpdateSelection();
    }

    private void UpdateSelection()
    {
        foreach (var (id, controls) in _planControls)
        {
            var selected = id == _selectedProductId;
            controls.Radio.IsEnabled = !_isOpeningPlan && HasPrice(id);
            controls.Radio.IsChecked = selected;
            controls.Card.Stroke = selected ? AccentColor : BorderColor;
            controls.Card.StrokeThickness = selected ? 2 : 1;
            controls.Card.BackgroundColor = selected ? Color.FromArgb("#E1F5EF") : Colors.White;
        }
        var plan = _plans.FirstOrDefault(p => p.ProductId == _selectedProductId);
        var product = plan is not null && _storeProducts.TryGetValue(plan.ProductId, out var p) ? p : null;
        if (_purchaseButton is not null)
        {
            _purchaseButton.Text = _isOpeningPlan ? "Wag asseblief…" : PaywallPresentation.PurchaseLabel(product?.LocalizedPrice, plan?.BillingPeriodMonths ?? 1, _sessionState.Current.IsSignedIn);
            _purchaseButton.IsEnabled = !_isOpeningPlan && !string.IsNullOrWhiteSpace(product?.LocalizedPrice);
            _purchaseButton.Opacity = _purchaseButton.IsEnabled ? 1 : 0.6;
            SemanticProperties.SetDescription(_purchaseButton, _purchaseButton.Text);
        }
        if (_renewalDisclosure is not null)
            _renewalDisclosure.Text = PaywallPresentation.RenewalDisclosure(product?.LocalizedPrice,
                plan?.BillingPeriodMonths ?? 1, DeviceInfo.Platform == DevicePlatform.iOS);
    }

    private Button BuildPurchaseButton()
    {
        var button = new Button
        {
            BackgroundColor = AccentColor, TextColor = Colors.White, FontAttributes = FontAttributes.Bold,
            FontSize = 16, CornerRadius = 24, MinimumHeightRequest = 50, Padding = new Thickness(14, 10),
            AutomationId = "paywall-purchase-button"
        };
        button.Clicked += async (_, _) =>
        {
            var plan = _plans.FirstOrDefault(p => p.ProductId == _selectedProductId);
            if (plan is not null && _storeProducts.TryGetValue(plan.ProductId, out var product) && HasPrice(plan.ProductId))
                await OpenPlanAsync(plan, product);
        };
        return button;
    }

    private View BuildFooter()
    {
        var free = BuildTextLinkButton("Gaan voort met gratis stories");
        free.AutomationId = "paywall-continue-free";
        free.Clicked += async (_, _) => await _navigationGate.RunAsync(ClosePaywallAsync);
        var restore = BuildTextLinkButton("Herstel aankope");
        restore.AutomationId = "paywall-restore";
        restore.Clicked += async (_, _) => await RestorePurchasesAsync();
        return new VerticalStackLayout { Spacing = 0, Children = { free, restore, BuildLegalLinks() } };
    }

    private View BuildRetryButton()
    {
        var button = BuildTextLinkButton("Herlaai winkelpryse");
        button.AutomationId = "paywall-retry";
        button.Clicked += async (_, _) =>
        {
            if (_isLoading || _isOpeningPlan) return;
            _isLoading = true;
            try { await LoadAsync(); } finally { _isLoading = false; }
        };
        return button;
    }

    private View BuildLegalLinks()
    {
        var termsButton = BuildTextLinkButton("Gebruiksvoorwaardes");
        termsButton.FontSize = 13;
        termsButton.AutomationId = "plans-terms-link";
        termsButton.Clicked += async (_, _) => await OpenLegalLinkAsync("/terme-en-voorwaardes");
        var privacyButton = BuildTextLinkButton("Privaatheid");
        privacyButton.FontSize = 13;
        privacyButton.AutomationId = "plans-privacy-link";
        privacyButton.Clicked += async (_, _) => await OpenLegalLinkAsync("/privaatheidsbeleid");
        var links = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Star) } };
        links.Add(termsButton); links.Add(privacyButton, 1);
        return links;
    }

    private async Task OpenLegalLinkAsync(string path)
    {
        if (!_hasParentAccess) return;
        try { if (await Launcher.Default.OpenAsync(new Uri(_apiClient.BuildAbsoluteUrl(path)))) return; }
        catch (Exception) { /* The failed handoff is shown below. */ }
        await DisplayAlertAsync("Kon nie die blad oopmaak nie",
            "Probeer asseblief weer. Jy kan die beleid en voorwaardes ook by www.schink.co.za lees.", "Reg so");
    }

    private static Button BuildTextLinkButton(string text) => new()
    {
        Text = text, BackgroundColor = Colors.Transparent, TextColor = AccentColor,
        FontSize = 14, MinimumHeightRequest = 44, Padding = new Thickness(4, 2)
    };

    private static View BuildNotice(string message) => new Label
    {
        Text = message, FontSize = 14, TextColor = MutedTextColor,
        HorizontalTextAlignment = TextAlignment.Center, Margin = new Thickness(8)
    };
}
