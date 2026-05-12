using Shink.Mobile.Models;
using Shink.Mobile.Services;

namespace Shink.Mobile.Pages;

public sealed class HomePage : ContentPage
{
    private readonly MobileApiClient _apiClient;
    private readonly VerticalStackLayout _content;

    public HomePage(MobileApiClient apiClient)
    {
        _apiClient = apiClient;
        Title = "Tuis";
        BackgroundColor = Color.FromArgb("#FFF9F0");

        _content = new VerticalStackLayout
        {
            Padding = new Thickness(20, 24),
            Spacing = 18
        };

        Content = new RefreshView
        {
            Content = new ScrollView { Content = _content },
            Command = new Command(async () => await LoadAsync())
        };
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
            var home = await _apiClient.GetHomeAsync();
            _content.Children.Clear();
            if (home is null)
            {
                _content.Children.Add(new Label { Text = "Kon nie die tuisblad laai nie." });
                return;
            }

            _content.Children.Add(BuildHero(home));
            _content.Children.Add(BuildPreviewSection("Nuut op Schink", home.NewestStories));
            _content.Children.Add(BuildPreviewSection("Bybelstories", home.BibleStories));
            _content.Children.Add(PageHelpers.BuildSectionTitle("Begin gratis"));

            foreach (var story in home.FreeStories)
            {
                _content.Children.Add(PageHelpers.BuildStoryCard(story, _apiClient, OpenStoryAsync));
            }
        }
        catch (Exception ex)
        {
            _content.Children.Clear();
            _content.Children.Add(new Label
            {
                Text = $"Kon nie data laai nie.\n{ex.Message}",
                TextColor = Color.FromArgb("#B42318")
            });
        }
    }

    private View BuildHero(MobileHomeResponse home)
    {
        return new Border
        {
            BackgroundColor = Color.FromArgb("#222222"),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 28 },
            Padding = 18,
            Content = new VerticalStackLayout
            {
                Spacing = 14,
                Children =
                {
                    new Image { Source = _apiClient.BuildImageUrl(home.LogoImageUrl), HeightRequest = 84, Aspect = Aspect.AspectFit },
                    new Image { Source = _apiClient.BuildImageUrl(home.HeroImageUrl), HeightRequest = 220, Aspect = Aspect.AspectFit },
                    new Label
                    {
                        Text = home.HeroTitle,
                        TextColor = Colors.White,
                        FontSize = 28,
                        FontAttributes = FontAttributes.Bold
                    },
                    new Label
                    {
                        Text = home.HeroSubtitle,
                        TextColor = Color.FromArgb("#F3F4F6"),
                        FontSize = 16
                    }
                }
            }
        };
    }

    private View BuildPreviewSection(string title, IReadOnlyList<MobileStoryPreview> items)
    {
        var stack = new VerticalStackLayout { Spacing = 10 };
        stack.Children.Add(PageHelpers.BuildSectionTitle(title));

        var row = new HorizontalStackLayout { Spacing = 14 };
        foreach (var item in items)
        {
            var card = new Border
            {
                WidthRequest = 180,
                BackgroundColor = Colors.White,
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = 22 },
                Padding = 12,
                Content = new VerticalStackLayout
                {
                    Spacing = 10,
                    Children =
                    {
                        new Image { Source = _apiClient.BuildImageUrl(item.ImageUrl), HeightRequest = 120, Aspect = Aspect.AspectFill },
                        new Label
                        {
                            Text = item.Title,
                            FontAttributes = FontAttributes.Bold,
                            FontSize = 15,
                            TextColor = Color.FromArgb("#222222")
                        }
                    }
                }
            };

            var tap = new TapGestureRecognizer();
            tap.Tapped += async (_, _) =>
            {
                var route = item.DetailUrl.Contains("/gratis/", StringComparison.OrdinalIgnoreCase)
                    ? $"{nameof(StoryDetailPage)}?slug={ExtractSlug(item.DetailUrl)}&source=gratis"
                    : $"{nameof(StoryDetailPage)}?slug={ExtractSlug(item.DetailUrl)}&source=luister";
                await Shell.Current.GoToAsync(route);
            };
            card.GestureRecognizers.Add(tap);
            row.Children.Add(card);
        }

        stack.Children.Add(new ScrollView { Orientation = ScrollOrientation.Horizontal, Content = row });
        return stack;
    }

    private Task OpenStoryAsync(MobileStorySummary story) =>
        Shell.Current.GoToAsync($"{nameof(StoryDetailPage)}?slug={Uri.EscapeDataString(story.Slug)}&source={Uri.EscapeDataString(story.Source)}");

    private static string ExtractSlug(string detailUrl)
    {
        var uri = new Uri(detailUrl);
        return uri.Segments.Last().Trim('/');
    }
}
