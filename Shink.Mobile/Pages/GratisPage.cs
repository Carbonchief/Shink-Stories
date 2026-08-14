using Shink.Mobile.Models;
using Shink.Mobile.Services;

namespace Shink.Mobile.Pages;

public sealed class GratisPage : ContentPage
{
    private readonly MobileApiClient _apiClient;
    private readonly PlayerTransitionBackdropState _transitionBackdropState;
    private readonly VerticalStackLayout _content;
    private MobileStoryCollectionResponse? _response;
    private double _lastResponsiveWidth = -1;
    private bool _responsiveRenderQueued;

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
        MobileResponsiveLayout.ApplyCenteredContent(_content, Width, 980);
        SizeChanged += (_, _) => HandleResponsiveSizeChanged();

        Content = new ScrollView { Content = _content };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_response is null)
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

            _response = response;
            _lastResponsiveWidth = MobileResponsiveLayout.ResolveWidth(Width);
            RenderStories(response);
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

    private void RenderStories(MobileStoryCollectionResponse response)
    {
        _content.Children.Clear();
        _content.Children.Add(PageHelpers.BuildSectionTitle(response.Title));
        _content.Children.Add(new Label
        {
            Text = response.Description,
            FontSize = 15,
            TextColor = Color.FromArgb("#5F5F5F")
        });
        _content.Children.Add(PageHelpers.BuildStoryCollection(
            response.Stories,
            _apiClient,
            OpenStoryAsync,
            Width));
    }

    private void HandleResponsiveSizeChanged()
    {
        var width = MobileResponsiveLayout.ResolveWidth(Width);
        MobileResponsiveLayout.ApplyCenteredContent(_content, width, 980);

        if (_response is null || _lastResponsiveWidth < 0 ||
            Math.Abs(width - _lastResponsiveWidth) < 32 || _responsiveRenderQueued)
        {
            if (_lastResponsiveWidth < 0)
            {
                _lastResponsiveWidth = width;
            }

            return;
        }

        _lastResponsiveWidth = width;
        _responsiveRenderQueued = true;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _responsiveRenderQueued = false;
            if (_response is not null)
            {
                RenderStories(_response);
            }
        });
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
