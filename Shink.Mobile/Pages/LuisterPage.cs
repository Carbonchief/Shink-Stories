using Shink.Mobile.Models;
using Shink.Mobile.Services;

namespace Shink.Mobile.Pages;

public sealed class LuisterPage : ContentPage
{
    private readonly MobileApiClient _apiClient;
    private readonly SessionState _sessionState;
    private readonly VerticalStackLayout _content;

    public LuisterPage(MobileApiClient apiClient, SessionState sessionState)
    {
        _apiClient = apiClient;
        _sessionState = sessionState;
        Title = "Luister";
        BackgroundColor = Color.FromArgb("#FFF9F0");

        _content = new VerticalStackLayout
        {
            Padding = new Thickness(20, 24),
            Spacing = 14
        };

        Content = new ScrollView { Content = _content };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        _content.Children.Clear();
        _content.Children.Add(new ActivityIndicator { IsRunning = true, Color = Color.FromArgb("#0F766E") });

        try
        {
            var response = await _apiClient.GetLuisterAsync();
            _content.Children.Clear();
            if (response is null)
            {
                _content.Children.Add(new Label { Text = "Kon nie luister stories laai nie." });
                return;
            }

            _content.Children.Add(PageHelpers.BuildSectionTitle("Luister stories"));
            _content.Children.Add(new Label
            {
                Text = response.HasPaidSubscription
                    ? "Jy het toegang tot al die luister stories."
                    : "Sommige stories is gesluit totdat jy inteken.",
                TextColor = Color.FromArgb("#5F5F5F")
            });

            foreach (var playlist in response.Playlists)
            {
                _content.Children.Add(BuildPlaylistSection(playlist));
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

    private View BuildPlaylistSection(MobilePlaylist playlist)
    {
        var section = new VerticalStackLayout { Spacing = 10 };
        section.Children.Add(new Label
        {
            Text = playlist.Title,
            FontSize = 22,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#222222")
        });

        if (!string.IsNullOrWhiteSpace(playlist.Description))
        {
            section.Children.Add(new Label
            {
                Text = playlist.Description,
                FontSize = 14,
                TextColor = Color.FromArgb("#5F5F5F")
            });
        }

        var row = new HorizontalStackLayout { Spacing = 14 };
        foreach (var story in playlist.Stories)
        {
            row.Children.Add(PageHelpers.BuildStoryCard(story, OpenStoryAsync, ToggleFavoriteAsync));
        }

        section.Children.Add(new ScrollView
        {
            Orientation = ScrollOrientation.Horizontal,
            Content = row
        });

        return section;
    }

    private Task OpenStoryAsync(MobileStorySummary story) =>
        Shell.Current.GoToAsync($"{nameof(StoryDetailPage)}?slug={Uri.EscapeDataString(story.Slug)}&source=luister");

    private async Task ToggleFavoriteAsync(MobileStorySummary story)
    {
        if (!_sessionState.Current.IsSignedIn)
        {
            await DisplayAlert("Teken in", "Teken eers in om gunstelinge te stoor.", "Reg so");
            return;
        }

        await _apiClient.SetFavoriteAsync(story.Slug, story.Source, !story.IsFavorite);
        await LoadAsync();
    }
}
