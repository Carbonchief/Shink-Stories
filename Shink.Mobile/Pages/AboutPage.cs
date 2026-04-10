using Shink.Mobile.Services;

namespace Shink.Mobile.Pages;

public sealed class AboutPage : ContentPage
{
    private readonly MobileApiClient _apiClient;
    private readonly VerticalStackLayout _content;

    public AboutPage(MobileApiClient apiClient)
    {
        _apiClient = apiClient;
        Title = "Meer oor ons";
        BackgroundColor = Color.FromArgb("#FFF9F0");

        _content = new VerticalStackLayout
        {
            Padding = new Thickness(20, 24),
            Spacing = 16
        };

        Content = new ScrollView { Content = _content };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_content.Children.Count == 0)
        {
            await LoadAsync();
        }
    }

    private async Task LoadAsync()
    {
        _content.Children.Clear();
        _content.Children.Add(new ActivityIndicator { IsRunning = true, Color = Color.FromArgb("#0F766E") });

        try
        {
            var response = await _apiClient.GetAboutAsync();
            _content.Children.Clear();
            if (response is null)
            {
                _content.Children.Add(new Label { Text = "Kon nie die blad laai nie." });
                return;
            }

            foreach (var block in response.Blocks)
            {
                _content.Children.Add(new Border
                {
                    BackgroundColor = Colors.White,
                    StrokeThickness = 0,
                    StrokeShape = new RoundRectangle { CornerRadius = 24 },
                    Padding = 16,
                    Content = new VerticalStackLayout
                    {
                        Spacing = 12,
                        Children =
                        {
                            new Image { Source = block.ImageUrl, HeightRequest = 220, Aspect = Aspect.AspectFit },
                            new Label
                            {
                                Text = block.Title,
                                FontSize = 24,
                                FontAttributes = FontAttributes.Bold,
                                TextColor = Color.FromArgb("#222222")
                            },
                            new Label
                            {
                                Text = block.Body,
                                FontSize = 15,
                                TextColor = Color.FromArgb("#5F5F5F")
                            }
                        }
                    }
                });
            }
        }
        catch (Exception ex)
        {
            _content.Children.Clear();
            _content.Children.Add(new Label
            {
                Text = ex.Message,
                TextColor = Color.FromArgb("#B42318")
            });
        }
    }
}
