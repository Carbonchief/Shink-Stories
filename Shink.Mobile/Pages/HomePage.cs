using Shink.Mobile.Models;
using Shink.Mobile.Services;

namespace Shink.Mobile.Pages;

public sealed class HomePage : ContentPage
{
    private readonly MobileApiClient _apiClient;
    private readonly PlayerTransitionBackdropState _transitionBackdropState;
    private readonly VerticalStackLayout _content;
    private MobileHomeResponse? _homeResponse;
    private double _lastResponsiveWidth = -1;
    private bool _responsiveRenderQueued;

    public HomePage(MobileApiClient apiClient, PlayerTransitionBackdropState transitionBackdropState)
    {
        _apiClient = apiClient;
        _transitionBackdropState = transitionBackdropState;
        Title = "Tuis";
        BackgroundColor = Color.FromArgb("#FFF9F0");
        SafeAreaEdges = new SafeAreaEdges(SafeAreaRegions.Container);

        _content = new VerticalStackLayout
        {
            Padding = new Thickness(20, 24),
            Spacing = 18
        };
        MobileResponsiveLayout.ApplyCenteredContent(_content, Width, 980);
        SizeChanged += (_, _) => HandleResponsiveSizeChanged();

        Content = new RefreshView
        {
            Content = new ScrollView { Content = _content },
            Command = new Command(async () => await LoadAsync())
        };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_homeResponse is null)
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

            _homeResponse = home;
            _lastResponsiveWidth = MobileResponsiveLayout.ResolveWidth(Width);
            RenderHome(home);
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

    private void RenderHome(MobileHomeResponse home)
    {
        _content.Children.Clear();
        _content.Children.Add(BuildHero(home));
        _content.Children.Add(BuildPreviewSection("Nuut op Schink", home.NewestStories));
        _content.Children.Add(BuildPreviewSection("Bybelstories", home.BibleStories));
        _content.Children.Add(PageHelpers.BuildSectionTitle("Begin gratis"));
        _content.Children.Add(PageHelpers.BuildStoryCollection(
            home.FreeStories,
            _apiClient,
            OpenStoryAsync,
            Width));
    }

    private void HandleResponsiveSizeChanged()
    {
        var width = MobileResponsiveLayout.ResolveWidth(Width);
        MobileResponsiveLayout.ApplyCenteredContent(_content, width, 980);

        if (_homeResponse is null || _lastResponsiveWidth < 0 ||
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
            if (_homeResponse is not null)
            {
                RenderHome(_homeResponse);
            }
        });
    }

    private View BuildHero(MobileHomeResponse home)
    {
        var hero = new Border
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
                    new Image
                    {
                        Source = _apiClient.BuildImageUrl(home.HeroImageUrl),
                        HeightRequest = MobileResponsiveLayout.IsWide(Width) ? 300 : 220,
                        Aspect = Aspect.AspectFit
                    },
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
        MobileResponsiveLayout.ApplyCenteredContent(hero, Width, 820);
        return hero;
    }

    private View BuildPreviewSection(string title, IReadOnlyList<MobileStoryPreview> items)
    {
        var stack = new VerticalStackLayout { Spacing = 10 };
        stack.Children.Add(PageHelpers.BuildSectionTitle(title));

        var row = new HorizontalStackLayout { Spacing = 14 };
        var cardWidth = MobileResponsiveLayout.ResolveHomePreviewCardWidth(Width);
        var imageHeight = MobileResponsiveLayout.ResolveHomePreviewImageHeight(Width);
        foreach (var item in items)
        {
            var card = new Border
            {
                WidthRequest = cardWidth,
                BackgroundColor = Colors.White,
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = 22 },
                Padding = 12,
                Content = new VerticalStackLayout
                {
                    Spacing = 10,
                    Children =
                    {
                        new Image
                        {
                            Source = _apiClient.BuildImageUrl(item.ImageUrl),
                            HeightRequest = imageHeight,
                            Aspect = Aspect.AspectFill
                        },
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
                await CapturePlayerTransitionBackdropAsync();
                await Shell.Current.GoToAsync(route, animate: false);
            };
            card.GestureRecognizers.Add(tap);
            row.Children.Add(card);
        }

        stack.Children.Add(new ScrollView { Orientation = ScrollOrientation.Horizontal, Content = row });
        return stack;
    }

    private async Task OpenStoryAsync(MobileStorySummary story)
    {
        await CapturePlayerTransitionBackdropAsync();
        await Shell.Current.GoToAsync(
            $"{nameof(StoryDetailPage)}?slug={Uri.EscapeDataString(story.Slug)}&source={Uri.EscapeDataString(story.Source)}",
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

    private static string ExtractSlug(string detailUrl)
    {
        var uri = new Uri(detailUrl);
        return uri.Segments.Last().Trim('/');
    }
}
