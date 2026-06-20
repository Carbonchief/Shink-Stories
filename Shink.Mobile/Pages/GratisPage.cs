using Shink.Mobile.Models;
using Shink.Mobile.Services;

namespace Shink.Mobile.Pages;

public sealed class GratisPage : ContentPage
{
    private readonly MobileApiClient _apiClient;
    private readonly PlayerTransitionBackdropState _transitionBackdropState;
    private readonly VerticalStackLayout _content;

    public GratisPage(MobileApiClient apiClient, PlayerTransitionBackdropState transitionBackdropState)
    {
        _apiClient = apiClient;
        _transitionBackdropState = transitionBackdropState;
        Title = "Gratis";
        BackgroundColor = Color.FromArgb("#FFF9F0");
        SafeAreaEdges = new SafeAreaEdges(SafeAreaRegions.Container);

        _content = new VerticalStackLayout
        {
            Padding = new Thickness(20, 24),
            Spacing = 12
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
            var response = await _apiClient.GetGratisAsync();
            _content.Children.Clear();
            if (response is null)
            {
                _content.Children.Add(new Label { Text = "Kon nie gratis stories laai nie." });
                return;
            }

            _content.Children.Add(PageHelpers.BuildSectionTitle(response.Title));
            _content.Children.Add(new Label
            {
                Text = response.Description,
                FontSize = 15,
                TextColor = Color.FromArgb("#5F5F5F")
            });

            foreach (var story in response.Stories)
            {
                _content.Children.Add(PageHelpers.BuildStoryCard(story, _apiClient, OpenStoryAsync));
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

    private async Task OpenStoryAsync(MobileStorySummary story)
    {
        await CapturePlayerTransitionBackdropAsync();
        await Shell.Current.GoToAsync(
            $"{nameof(StoryDetailPage)}?slug={Uri.EscapeDataString(story.Slug)}&source=gratis",
            animate: false,
            parameters: new Dictionary<string, object>
            {
                ["preview"] = story
            });
    }

    private async Task CapturePlayerTransitionBackdropAsync()
    {
        try
        {
            await _transitionBackdropState.CaptureAsync();
        }
        catch
        {
            // Transition backdrop capture should never block opening the player.
        }
    }
}
