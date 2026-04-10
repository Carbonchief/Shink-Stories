using System.Net;
using Shink.Mobile.Models;
using Shink.Mobile.Services;

namespace Shink.Mobile.Pages;

[QueryProperty(nameof(StorySlug), "slug")]
[QueryProperty(nameof(Source), "source")]
public sealed class StoryDetailPage : ContentPage
{
    private readonly MobileApiClient _apiClient;
    private readonly SessionState _sessionState;
    private readonly VerticalStackLayout _content;
    private string? _loadedKey;

    public StoryDetailPage(MobileApiClient apiClient, SessionState sessionState)
    {
        _apiClient = apiClient;
        _sessionState = sessionState;
        BackgroundColor = Color.FromArgb("#FFF9F0");

        _content = new VerticalStackLayout
        {
            Padding = new Thickness(20, 24),
            Spacing = 16
        };

        Content = new ScrollView { Content = _content };
    }

    public string? StorySlug { get; set; }
    public string? Source { get; set; }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        var loadKey = $"{StorySlug}:{Source}";
        if (string.IsNullOrWhiteSpace(StorySlug) || loadKey == _loadedKey)
        {
            return;
        }

        _loadedKey = loadKey;
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        _content.Children.Clear();
        _content.Children.Add(new ActivityIndicator { IsRunning = true, Color = Color.FromArgb("#0F766E") });

        try
        {
            var detail = await _apiClient.GetStoryAsync(StorySlug ?? string.Empty, Source ?? "luister");
            _content.Children.Clear();

            if (detail is null)
            {
                _content.Children.Add(new Label { Text = "Storie nie gevind nie." });
                return;
            }

            Title = detail.Story.Title;
            _content.Children.Add(new Image
            {
                Source = detail.Story.ImageUrl,
                HeightRequest = 260,
                Aspect = Aspect.AspectFit
            });
            _content.Children.Add(PageHelpers.BuildSectionTitle(detail.Story.Title));
            _content.Children.Add(new Label
            {
                Text = detail.Story.Description,
                FontSize = 15,
                TextColor = Color.FromArgb("#5F5F5F")
            });

            if (detail.RequiresSubscription)
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
                            new Label
                            {
                                Text = "Hierdie storie is nog gesluit.",
                                FontSize = 20,
                                FontAttributes = FontAttributes.Bold
                            },
                            new Label
                            {
                                Text = "Teken in of kies 'n plan om hierdie storie op die app oop te maak.",
                                TextColor = Color.FromArgb("#5F5F5F")
                            },
                            BuildLinkButton("Teken in", detail.LoginUrl),
                            BuildLinkButton("Sien planne", detail.PlansUrl)
                        }
                    }
                });
            }
            else if (!string.IsNullOrWhiteSpace(detail.AudioUrl))
            {
                _content.Children.Add(BuildAudioPlayer(detail.AudioUrl));
            }

            if (_sessionState.Current.IsSignedIn)
            {
                var favoriteButton = new Button
                {
                    Text = detail.Story.IsFavorite ? "Verwyder uit gunstelinge" : "Voeg by gunstelinge",
                    BackgroundColor = detail.Story.IsFavorite ? Color.FromArgb("#FFF0EC") : Color.FromArgb("#F3F4F6"),
                    TextColor = detail.Story.IsFavorite ? Color.FromArgb("#B42318") : Color.FromArgb("#222222")
                };
                favoriteButton.Clicked += async (_, _) =>
                {
                    await _apiClient.SetFavoriteAsync(detail.Story.Slug, detail.Story.Source, !detail.Story.IsFavorite);
                    _loadedKey = null;
                    await LoadAsync();
                };
                _content.Children.Add(favoriteButton);
            }

            var shareButton = new Button
            {
                Text = "Deel storie",
                BackgroundColor = Color.FromArgb("#0F766E"),
                TextColor = Colors.White
            };
            shareButton.Clicked += async (_, _) =>
            {
                await Share.Default.RequestAsync(new ShareTextRequest
                {
                    Uri = detail.ShareUrl,
                    Title = detail.Story.Title
                });
            };
            _content.Children.Add(shareButton);

            _content.Children.Add(BuildPreviousNext(detail));

            if (detail.RelatedStories.Count > 0)
            {
                _content.Children.Add(PageHelpers.BuildSectionTitle("Ander stories"));
                foreach (var story in detail.RelatedStories)
                {
                    _content.Children.Add(PageHelpers.BuildStoryCard(story, OpenRelatedStoryAsync));
                }
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

    private static View BuildAudioPlayer(string audioUrl)
    {
        var encoded = WebUtility.HtmlEncode(audioUrl);
        return new Border
        {
            BackgroundColor = Colors.White,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 24 },
            HeightRequest = 140,
            Content = new WebView
            {
                HeightRequest = 120,
                Source = new HtmlWebViewSource
                {
                    Html = $$"""
                    <html>
                    <body style="margin:0;padding:16px;font-family:-apple-system;background:#ffffff;">
                      <audio controls controlslist="nodownload noplaybackrate" style="width:100%;">
                        <source src="{{encoded}}" />
                      </audio>
                    </body>
                    </html>
                    """
                }
            }
        };
    }

    private View BuildPreviousNext(MobileStoryDetailResponse detail)
    {
        var layout = new HorizontalStackLayout { Spacing = 12 };

        if (detail.PreviousStory is not null)
        {
            var previous = new Button
            {
                Text = "Vorige",
                BackgroundColor = Color.FromArgb("#F3F4F6"),
                TextColor = Color.FromArgb("#222222")
            };
            previous.Clicked += async (_, _) => await OpenStoryAsync(detail.PreviousStory);
            layout.Children.Add(previous);
        }

        if (detail.NextStory is not null)
        {
            var next = new Button
            {
                Text = "Volgende",
                BackgroundColor = Color.FromArgb("#F3F4F6"),
                TextColor = Color.FromArgb("#222222")
            };
            next.Clicked += async (_, _) => await OpenStoryAsync(detail.NextStory);
            layout.Children.Add(next);
        }

        return layout;
    }

    private static Button BuildLinkButton(string text, string url)
    {
        var button = new Button
        {
            Text = text,
            BackgroundColor = Color.FromArgb("#0F766E"),
            TextColor = Colors.White
        };
        button.Clicked += async (_, _) => await Browser.OpenAsync(url, BrowserLaunchMode.External);
        return button;
    }

    private async Task OpenRelatedStoryAsync(MobileStorySummary story) => await OpenStoryAsync(story);

    private async Task OpenStoryAsync(MobileStorySummary story)
    {
        _loadedKey = null;
        await Shell.Current.GoToAsync($"{nameof(StoryDetailPage)}?slug={Uri.EscapeDataString(story.Slug)}&source={Uri.EscapeDataString(story.Source)}");
    }
}
