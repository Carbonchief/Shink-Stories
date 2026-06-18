using Shink.Mobile.Models;
using Shink.Mobile.Services;

namespace Shink.Mobile.Pages;

public sealed class ProfilePage : ContentPage
{
    private static readonly Color PageBackgroundColor = Color.FromArgb("#FFF7E8");
    private static readonly Color TextColor = Color.FromArgb("#1B2231");
    private static readonly Color MutedTextColor = Color.FromArgb("#69716D");
    private static readonly Color AccentColor = Color.FromArgb("#123F3F");
    private static readonly Color FieldBackgroundColor = Colors.White;
    private static readonly Color ErrorColor = Color.FromArgb("#B42318");

    private readonly MobileApiClient _apiClient;
    private readonly SessionState _sessionState;
    private readonly Grid _avatarHost;
    private readonly Entry _emailEntry;
    private readonly Entry _firstNameEntry;
    private readonly Entry _lastNameEntry;
    private readonly Entry _displayNameEntry;
    private readonly Entry _mobileNumberEntry;
    private readonly ActivityIndicator _saveSpinner;
    private readonly Button _saveButton;
    private readonly Label _statusLabel;

    private bool _isSaving;

    public ProfilePage(MobileApiClient apiClient, SessionState sessionState)
    {
        _apiClient = apiClient;
        _sessionState = sessionState;

        Title = "Profiel";
        BackgroundColor = PageBackgroundColor;
        Shell.SetNavBarIsVisible(this, false);
        Shell.SetTabBarIsVisible(this, false);

        _avatarHost = new Grid
        {
            WidthRequest = 96,
            HeightRequest = 96,
            HorizontalOptions = LayoutOptions.Center
        };
        _emailEntry = BuildEntry("E-pos");
        _emailEntry.IsReadOnly = true;
        _firstNameEntry = BuildEntry("Naam");
        _lastNameEntry = BuildEntry("Van");
        _displayNameEntry = BuildEntry("Vertoonnaam");
        _mobileNumberEntry = BuildEntry("Selfoonnommer");
        _mobileNumberEntry.Keyboard = Keyboard.Telephone;

        _saveSpinner = new ActivityIndicator
        {
            Color = Colors.White,
            IsVisible = false,
            WidthRequest = 18,
            HeightRequest = 18,
            VerticalOptions = LayoutOptions.Center
        };

        _saveButton = new Button
        {
            Text = "Stoor profiel",
            BackgroundColor = AccentColor,
            TextColor = Colors.White,
            FontAttributes = FontAttributes.Bold,
            FontSize = 16,
            CornerRadius = 24,
            HeightRequest = 52
        };
        _saveButton.Clicked += async (_, _) => await SaveAsync();

        _statusLabel = new Label
        {
            FontSize = 14,
            TextColor = MutedTextColor,
            HorizontalTextAlignment = TextAlignment.Center,
            IsVisible = false
        };

        Content = new ScrollView
        {
            BackgroundColor = PageBackgroundColor,
            Content = new VerticalStackLayout
            {
                Padding = new Thickness(20, 18, 20, 30),
                Spacing = 20,
                Children =
                {
                    BuildHeader(),
                    _avatarHost,
                    BuildForm(),
                    BuildSaveRow(),
                    _statusLabel
                }
            }
        };

        ApplySession(_sessionState.Current);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        ApplySession(_sessionState.Current);

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            var session = await _apiClient.GetSessionAsync(timeout.Token);
            ApplySession(session);
        }
        catch
        {
            // The cached session is good enough for editing if a refresh is temporarily unavailable.
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
        backTap.Tapped += async (_, _) => await Shell.Current.GoToAsync("..", animate: true);
        backButton.GestureRecognizers.Add(backTap);

        var title = new Label
        {
            Text = "Profiel",
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

    private View BuildForm() =>
        new VerticalStackLayout
        {
            Spacing = 14,
            Children =
            {
                BuildField("E-pos", _emailEntry),
                BuildField("Naam", _firstNameEntry),
                BuildField("Van", _lastNameEntry),
                BuildField("Vertoonnaam", _displayNameEntry),
                BuildField("Selfoonnommer", _mobileNumberEntry)
            }
        };

    private static Entry BuildEntry(string placeholder) =>
        new()
        {
            Placeholder = placeholder,
            FontSize = 16,
            TextColor = TextColor,
            PlaceholderColor = MutedTextColor,
            BackgroundColor = Colors.Transparent,
            HeightRequest = 44,
            ClearButtonVisibility = ClearButtonVisibility.WhileEditing
        };

    private static View BuildField(string label, Entry entry) =>
        new VerticalStackLayout
        {
            Spacing = 6,
            Children =
            {
                new Label
                {
                    Text = label,
                    FontSize = 13,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = MutedTextColor
                },
                new Border
                {
                    BackgroundColor = FieldBackgroundColor,
                    Stroke = Color.FromArgb("#E8DDC8"),
                    StrokeThickness = 1,
                    StrokeShape = new RoundRectangle { CornerRadius = 20 },
                    Padding = new Thickness(16, 3),
                    Content = entry
                }
            }
        };

    private View BuildSaveRow()
    {
        var overlay = new Grid
        {
            Children =
            {
                _saveButton,
                new HorizontalStackLayout
                {
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.Center,
                    Spacing = 8,
                    InputTransparent = true,
                    Children = { _saveSpinner }
                }
            }
        };

        return overlay;
    }

    private void ApplySession(MobileSession session)
    {
        var email = FirstValue(session.Email, _emailEntry.Text);
        var displayName = FirstValue(session.DisplayName, _displayNameEntry.Text);
        var firstName = FirstValue(session.FirstName, _firstNameEntry.Text);
        var lastName = FirstValue(session.LastName, _lastNameEntry.Text);
        var mobileNumber = FirstValue(session.MobileNumber, _mobileNumberEntry.Text);

        if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName))
        {
            var nameParts = SplitDisplayName(displayName);
            firstName = FirstValue(firstName, nameParts.FirstName);
            lastName = FirstValue(lastName, nameParts.LastName);
        }

        _emailEntry.Text = email ?? string.Empty;
        _firstNameEntry.Text = firstName ?? string.Empty;
        _lastNameEntry.Text = lastName ?? string.Empty;
        _displayNameEntry.Text = displayName ?? BuildDisplayName(firstName, lastName) ?? string.Empty;
        _mobileNumberEntry.Text = mobileNumber ?? string.Empty;
        RenderAvatar(session);
    }

    private void RenderAvatar(MobileSession session)
    {
        _avatarHost.Children.Clear();

        var imageUrl = string.IsNullOrWhiteSpace(session.ProfileImageUrl)
            ? null
            : _apiClient.BuildImageUrl(session.ProfileImageUrl);

        if (!string.IsNullOrWhiteSpace(imageUrl))
        {
            _avatarHost.Children.Add(new Border
            {
                BackgroundColor = Color.FromArgb("#F7EAD0"),
                Stroke = Colors.White,
                StrokeThickness = 3,
                StrokeShape = new RoundRectangle { CornerRadius = 48 },
                WidthRequest = 96,
                HeightRequest = 96,
                Padding = 0,
                Content = new Image
                {
                    Source = ImageSource.FromUri(new Uri(imageUrl, UriKind.Absolute)),
                    Aspect = Aspect.AspectFill,
                    WidthRequest = 96,
                    HeightRequest = 96
                }
            });
            return;
        }

        _avatarHost.Children.Add(new Border
        {
            BackgroundColor = Color.FromArgb("#FFD45A"),
            Stroke = Colors.White,
            StrokeThickness = 3,
            StrokeShape = new RoundRectangle { CornerRadius = 48 },
            WidthRequest = 96,
            HeightRequest = 96,
            Content = new Label
            {
                Text = BuildInitials(session),
                FontSize = 28,
                FontAttributes = FontAttributes.Bold,
                TextColor = Color.FromArgb("#0B3534"),
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center
            }
        });
    }

    private async Task SaveAsync()
    {
        if (_isSaving)
        {
            return;
        }

        SetSaving(true);
        try
        {
            var result = await _apiClient.UpdateProfileAsync(
                _firstNameEntry.Text ?? string.Empty,
                _lastNameEntry.Text ?? string.Empty,
                _displayNameEntry.Text ?? string.Empty,
                _mobileNumberEntry.Text ?? string.Empty);
            _statusLabel.Text = result.Message;
            _statusLabel.TextColor = MutedTextColor;
            _statusLabel.IsVisible = true;
            ApplySession(_sessionState.Current);
        }
        catch (Exception ex)
        {
            _statusLabel.Text = ex.Message;
            _statusLabel.TextColor = ErrorColor;
            _statusLabel.IsVisible = true;
        }
        finally
        {
            SetSaving(false);
        }
    }

    private void SetSaving(bool isSaving)
    {
        _isSaving = isSaving;
        _saveButton.IsEnabled = !isSaving;
        _saveButton.Text = isSaving ? "Stoor..." : "Stoor profiel";
        _saveSpinner.IsVisible = isSaving;
        _saveSpinner.IsRunning = isSaving;
    }

    private static string BuildInitials(MobileSession session)
    {
        var source = string.Join(
            " ",
            new[] { session.FirstName, session.LastName }
                .Where(part => !string.IsNullOrWhiteSpace(part))
                .Select(part => part!.Trim()));

        if (string.IsNullOrWhiteSpace(source))
        {
            source = session.DisplayName ?? session.Email ?? "SS";
        }

        var initials = source
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Take(2)
            .Select(part => char.ToUpperInvariant(part[0]))
            .ToArray();

        return initials.Length == 0 ? "SS" : new string(initials);
    }

    private static string? FirstValue(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private static (string? FirstName, string? LastName) SplitDisplayName(string? displayName)
    {
        var parts = (displayName ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return parts.Length switch
        {
            0 => (null, null),
            1 => (parts[0], null),
            _ => (parts[0], string.Join(" ", parts.Skip(1)))
        };
    }

    private static string? BuildDisplayName(string? firstName, string? lastName)
    {
        var displayName = string.Join(
            " ",
            new[] { firstName, lastName }
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!.Trim()));

        return string.IsNullOrWhiteSpace(displayName) ? null : displayName;
    }
}
