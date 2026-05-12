using Shink.Mobile.Services;

namespace Shink.Mobile.Pages;

public sealed class AccountPage : ContentPage
{
    private readonly MobileApiClient _apiClient;
    private readonly SessionState _sessionState;
    private readonly Entry _baseUrlEntry;
    private readonly Label _statusLabel;
    private readonly VerticalStackLayout _signedInState;
    private readonly VerticalStackLayout _signedOutState;

    private readonly Entry _loginEmailEntry;
    private readonly Entry _loginPasswordEntry;
    private readonly Entry _signupFirstNameEntry;
    private readonly Entry _signupLastNameEntry;
    private readonly Entry _signupDisplayNameEntry;
    private readonly Entry _signupEmailEntry;
    private readonly Entry _signupMobileEntry;
    private readonly Entry _signupPasswordEntry;
    private bool _hasLoadedSession;

    public AccountPage(MobileApiClient apiClient, SessionState sessionState)
    {
        _apiClient = apiClient;
        _sessionState = sessionState;
        Title = "Rekening";
        BackgroundColor = Color.FromArgb("#FFF9F0");

        _baseUrlEntry = new Entry { Text = _apiClient.BaseUrl, Placeholder = "Server URL" };
        _statusLabel = new Label { TextColor = Color.FromArgb("#5F5F5F") };
        _signedInState = new VerticalStackLayout { Spacing = 12 };
        _signedOutState = new VerticalStackLayout { Spacing = 18 };

        _loginEmailEntry = new Entry { Placeholder = "E-pos", Keyboard = Keyboard.Email };
        _loginPasswordEntry = new Entry { Placeholder = "Wagwoord", IsPassword = true };
        _signupFirstNameEntry = new Entry { Placeholder = "Naam" };
        _signupLastNameEntry = new Entry { Placeholder = "Van" };
        _signupDisplayNameEntry = new Entry { Placeholder = "Vertoonnaam" };
        _signupEmailEntry = new Entry { Placeholder = "E-pos", Keyboard = Keyboard.Email };
        _signupMobileEntry = new Entry { Placeholder = "Selfoon" };
        _signupPasswordEntry = new Entry { Placeholder = "Wagwoord", IsPassword = true };

        var saveBaseUrlButton = new Button
        {
            Text = "Stoor server URL",
            BackgroundColor = Color.FromArgb("#222222"),
            TextColor = Colors.White
        };
        saveBaseUrlButton.Clicked += async (_, _) =>
        {
            _apiClient.BaseUrl = _baseUrlEntry.Text ?? string.Empty;
            await RefreshSessionAsync("Server URL is gestoor.");
        };

        BuildSignedOutState();

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = new Thickness(20, 24),
                Spacing = 18,
                Children =
                {
                    PageHelpers.BuildSectionTitle("Rekening"),
                    new Label
                    {
                        Text = "Vir ontwikkeling wys die app standaard na jou plaaslike webbediener. Jy kan dit hieronder verander.",
                        TextColor = Color.FromArgb("#5F5F5F")
                    },
                    _baseUrlEntry,
                    saveBaseUrlButton,
                    _statusLabel,
                    _signedInState,
                    _signedOutState
                }
            }
        };

        _sessionState.Changed += _ => MainThread.BeginInvokeOnMainThread(ApplySessionState);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_hasLoadedSession)
        {
            return;
        }

        _hasLoadedSession = true;
        await RefreshSessionAsync();
    }

    private void BuildSignedOutState()
    {
        var loginButton = new Button
        {
            Text = "Teken in",
            BackgroundColor = Color.FromArgb("#0F766E"),
            TextColor = Colors.White
        };
        loginButton.Clicked += async (_, _) =>
        {
            try
            {
                var result = await _apiClient.SignInAsync(_loginEmailEntry.Text ?? string.Empty, _loginPasswordEntry.Text ?? string.Empty);
                await RefreshSessionAsync(result.Message);
            }
            catch (Exception ex)
            {
                _statusLabel.Text = ex.Message;
                _statusLabel.TextColor = Color.FromArgb("#B42318");
            }
        };

        var signupButton = new Button
        {
            Text = "Skep rekening",
            BackgroundColor = Color.FromArgb("#F59E0B"),
            TextColor = Colors.White
        };
        signupButton.Clicked += async (_, _) =>
        {
            try
            {
                var result = await _apiClient.SignUpAsync(
                    _signupFirstNameEntry.Text ?? string.Empty,
                    _signupLastNameEntry.Text ?? string.Empty,
                    _signupDisplayNameEntry.Text ?? string.Empty,
                    _signupEmailEntry.Text ?? string.Empty,
                    _signupMobileEntry.Text ?? string.Empty,
                    _signupPasswordEntry.Text ?? string.Empty);
                await RefreshSessionAsync(result.Message);
            }
            catch (Exception ex)
            {
                _statusLabel.Text = ex.Message;
                _statusLabel.TextColor = Color.FromArgb("#B42318");
            }
        };

        _signedOutState.Children.Clear();
        _signedOutState.Children.Add(new Label
        {
            Text = "Teken in",
            FontAttributes = FontAttributes.Bold,
            FontSize = 20
        });
        _signedOutState.Children.Add(_loginEmailEntry);
        _signedOutState.Children.Add(_loginPasswordEntry);
        _signedOutState.Children.Add(loginButton);
        _signedOutState.Children.Add(new BoxView { HeightRequest = 1, Color = Color.FromArgb("#E5E7EB") });
        _signedOutState.Children.Add(new Label
        {
            Text = "Of skep 'n rekening",
            FontAttributes = FontAttributes.Bold,
            FontSize = 20
        });
        _signedOutState.Children.Add(_signupFirstNameEntry);
        _signedOutState.Children.Add(_signupLastNameEntry);
        _signedOutState.Children.Add(_signupDisplayNameEntry);
        _signedOutState.Children.Add(_signupEmailEntry);
        _signedOutState.Children.Add(_signupMobileEntry);
        _signedOutState.Children.Add(_signupPasswordEntry);
        _signedOutState.Children.Add(signupButton);
    }

    private async Task RefreshSessionAsync(string? message = null)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await _apiClient.GetSessionAsync(timeout.Token);
            ApplySessionState();
            _statusLabel.Text = message ?? (_sessionState.Current.IsSignedIn
                ? "Jy is ingeteken."
                : "Jy is nie tans ingeteken nie.");
            _statusLabel.TextColor = Color.FromArgb("#5F5F5F");
        }
        catch (Exception ex)
        {
            _statusLabel.Text = ex.Message;
            _statusLabel.TextColor = Color.FromArgb("#B42318");
        }
    }

    private void ApplySessionState()
    {
        var session = _sessionState.Current;
        _baseUrlEntry.Text = _apiClient.BaseUrl;

        _signedInState.Children.Clear();
        if (session.IsSignedIn)
        {
            _signedInState.IsVisible = true;
            _signedOutState.IsVisible = false;

            var logoutButton = new Button
            {
                Text = "Teken uit",
                BackgroundColor = Color.FromArgb("#222222"),
                TextColor = Colors.White
            };
            logoutButton.Clicked += async (_, _) =>
            {
                await _apiClient.SignOutAsync();
                await RefreshSessionAsync("Jy is nou afgeteken.");
            };

            _signedInState.Children.Add(new Border
            {
                BackgroundColor = Colors.White,
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = 24 },
                Padding = 16,
                Content = new VerticalStackLayout
                {
                    Spacing = 10,
                    Children =
                    {
                        new Label
                        {
                            Text = session.Email ?? "Ingeteken",
                            FontSize = 20,
                            FontAttributes = FontAttributes.Bold
                        },
                        new Label
                        {
                            Text = session.HasPaidSubscription
                                ? "Jou betaalde luistertoegang is aktief."
                                : "Jy het tans gratis toegang.",
                            TextColor = Color.FromArgb("#5F5F5F")
                        },
                        logoutButton
                    }
                }
            });
        }
        else
        {
            _signedInState.IsVisible = false;
            _signedOutState.IsVisible = true;
        }
    }
}
