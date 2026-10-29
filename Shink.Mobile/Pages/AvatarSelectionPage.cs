using Shink.Mobile.Services;
using Shink.Shared;

namespace Shink.Mobile.Pages;

public sealed class AvatarSelectionPage : ContentPage, IQueryAttributable
{
    private readonly MobileApiClient _apiClient;
    private readonly Grid _avatarGrid;
    private readonly VerticalStackLayout _content;
    private readonly Button _continueButton;
    private readonly Button _backButton;
    private readonly Label _status;
    private readonly ActivityIndicator _spinner;
    private readonly Dictionary<string, (ImageButton Button, Border Ring, Border Check)> _choices = new();
    private string? _selectedId;
    private bool _isEditing;
    private bool _isSaving;
    private double _portraitSize;

    public AvatarSelectionPage(MobileApiClient apiClient, SessionState sessionState)
    {
        _apiClient = apiClient;
        _selectedId = ProfileAvatarOptions.IdFromImageUrl(sessionState.Current.ProfileImageUrl);
        Title = "Profielprentjie";
        AutomationId = "avatar-selection-page";
        SafeAreaEdges = new SafeAreaEdges(SafeAreaRegions.Container);
        Background = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0), EndPoint = new Point(0, 1),
            GradientStops =
            {
                new GradientStop(Color.FromArgb("#3F9CAD"), 0),
                new GradientStop(Color.FromArgb("#78BD9F"), 0.55f),
                new GradientStop(Color.FromArgb("#D2D278"), 1)
            }
        };
        Shell.SetNavBarIsVisible(this, false);
        Shell.SetTabBarIsVisible(this, false);

        _backButton = new Button
        {
            Text = "Terug", IsVisible = false,
            BackgroundColor = Colors.Transparent, TextColor = Color.FromArgb("#173F4F"),
            HorizontalOptions = LayoutOptions.Start, MinimumHeightRequest = 44
        };
        _backButton.Clicked += async (_, _) =>
        {
            if (!_isSaving)
                await Shell.Current.GoToAsync("..", animate: true);
        };

        _avatarGrid = new Grid
        {
            ColumnSpacing = 14, RowSpacing = 14,
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new(GridLength.Star), new(GridLength.Star), new(GridLength.Star), new(GridLength.Star)
            },
            RowDefinitions = new RowDefinitionCollection
            {
                new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto)
            }
        };
        _continueButton = new Button
        {
            Text = "Begin luister", AutomationId = "avatar-continue",
            BackgroundColor = Color.FromArgb("#00875D"), TextColor = Colors.White,
            FontFamily = "PoppinsBold", FontSize = 20, CornerRadius = 16,
            HeightRequest = 58, MaximumWidthRequest = 396, HorizontalOptions = LayoutOptions.Fill,
            Margin = new Thickness(0, 12, 0, 0)
        };
        _continueButton.Clicked += async (_, _) => await SaveAsync();
        _status = new Label
        {
            IsVisible = false, FontSize = 14, TextColor = Color.FromArgb("#8B2516"),
            HorizontalTextAlignment = TextAlignment.Center
        };
        _spinner = new ActivityIndicator { IsVisible = false, IsRunning = false, Color = Colors.White };
        _content = new VerticalStackLayout
        {
            Spacing = 20, Padding = new Thickness(22, 30, 22, 40),
            HorizontalOptions = LayoutOptions.Center, MaximumWidthRequest = 680,
            Children =
            {
                _backButton,
                new Label
                {
                    Text = "Kies ’n prentjie vir jou profiel", FontFamily = "Poppins", FontSize = 24,
                    TextColor = Color.FromArgb("#173F4F"), HorizontalTextAlignment = TextAlignment.Center,
                    Margin = new Thickness(0, 8, 0, 4)
                },
                _avatarGrid, _continueButton, _spinner, _status
            }
        };
        Content = new ScrollView { Content = _content };
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        _isEditing = query.TryGetValue("mode", out var mode) && string.Equals(mode?.ToString(), "edit", StringComparison.Ordinal);
        _continueButton.Text = _isEditing ? "Save" : "Begin luister";
        _backButton.IsVisible = _isEditing;
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        if (width <= 0 || _content is null)
            return;

        var contentWidth = Math.Min(width, 680);
        _content.WidthRequest = contentWidth;
        var size = Math.Min(142, (contentWidth - 44 - 42) / 4);
        if (size > 0 && Math.Abs(size - _portraitSize) > 0.5)
        {
            _portraitSize = size;
            BuildPortraits();
        }
    }

    private void BuildPortraits()
    {
        _avatarGrid.Children.Clear();
        _choices.Clear();
        for (var index = 0; index < ProfileAvatarOptions.All.Count; index++)
        {
            var avatar = ProfileAvatarOptions.All[index];
            var image = new ImageButton
            {
                Source = avatar.MobileImage, Aspect = Aspect.AspectFill,
                WidthRequest = _portraitSize, HeightRequest = _portraitSize,
                Padding = 0, BackgroundColor = Colors.Transparent,
                AutomationId = $"avatar-{avatar.Id}"
            };
            var ring = new Border
            {
                WidthRequest = _portraitSize, HeightRequest = _portraitSize,
                StrokeShape = new Ellipse(), StrokeThickness = 3,
                Stroke = Colors.Transparent, Padding = 0, Content = image
            };
            var check = new Border
            {
                BackgroundColor = Color.FromArgb("#00875D"), Stroke = Colors.White, StrokeThickness = 2,
                StrokeShape = new RoundRectangle { CornerRadius = 12 }, WidthRequest = 24, HeightRequest = 24,
                HorizontalOptions = LayoutOptions.End, VerticalOptions = LayoutOptions.End, InputTransparent = true,
                Content = new Label
                {
                    Text = "\uf00c", FontFamily = "FontAwesomeSolid", FontSize = 12, TextColor = Colors.White,
                    HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center
                }
            };
            image.Clicked += (_, _) =>
            {
                if (_isSaving)
                    return;
                _selectedId = avatar.Id;
                UpdateSelection();
            };
            _choices[avatar.Id] = (image, ring, check);
            _avatarGrid.Add(new Grid { Children = { ring, check } }, index % 4, index / 4);
        }
        UpdateSelection();
    }

    private void UpdateSelection()
    {
        foreach (var avatar in ProfileAvatarOptions.All)
        {
            if (!_choices.TryGetValue(avatar.Id, out var choice))
                continue;
            var selected = _selectedId == avatar.Id;
            choice.Ring.Stroke = selected ? Color.FromArgb("#00875D") : Colors.Transparent;
            choice.Check.IsVisible = selected;
            SemanticProperties.SetDescription(choice.Button, selected ? $"{avatar.Name}, gekies" : avatar.Name);
        }
    }

    private async Task SaveAsync()
    {
        if (_isSaving)
            return;
        if (_isEditing && _selectedId is null)
        {
            _status.Text = "Kies asseblief ’n profielprentjie.";
            _status.IsVisible = true;
            return;
        }

        _isSaving = true;
        _continueButton.IsEnabled = false;
        _backButton.IsEnabled = false;
        _avatarGrid.IsEnabled = false;
        _status.IsVisible = false;
        _spinner.IsVisible = _spinner.IsRunning = true;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await _apiClient.UpdateAvatarAsync(_selectedId, useRandomIfUnselected: !_isEditing, timeout.Token);
            if (_isEditing)
                await Shell.Current.GoToAsync("..", animate: true);
            // Completing onboarding changes AppShell's root to Luister via SessionState.
        }
        catch (Exception exception)
        {
            _status.Text = exception.Message;
            _status.IsVisible = true;
        }
        finally
        {
            _isSaving = false;
            _continueButton.IsEnabled = true;
            _backButton.IsEnabled = true;
            _avatarGrid.IsEnabled = true;
            _spinner.IsVisible = _spinner.IsRunning = false;
        }
    }
}
