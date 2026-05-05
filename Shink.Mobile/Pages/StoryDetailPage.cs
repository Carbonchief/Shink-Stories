using System.Net;
using System.Globalization;
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
                _ = _apiClient.TrackStoryViewAsync(detail.Story.Slug, detail.Story.Source);
                _content.Children.Add(BuildAudioPlayer(detail));
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

    private View BuildAudioPlayer(MobileStoryDetailResponse detail)
    {
        var encoded = WebUtility.HtmlEncode(detail.AudioUrl ?? string.Empty);
        var trackingSessionId = Guid.NewGuid();
        var webView = new WebView
        {
            HeightRequest = 120,
            Source = new HtmlWebViewSource
            {
                Html = $$"""
                <html>
                <body style="margin:0;padding:16px;font-family:-apple-system;background:#ffffff;">
                  <audio id="story-audio" controls controlslist="nodownload noplaybackrate" style="width:100%;">
                    <source src="{{encoded}}" />
                  </audio>
                  <script>
                    (function () {
                      const audio = document.getElementById("story-audio");
                      const thresholdSeconds = 12;
                      const maxDeltaSeconds = 30;
                      const maxEventSeconds = 600;
                      const minEventSeconds = 0.2;
                      let pendingSeconds = 0;
                      let lastTickAt = null;

                      function captureDelta() {
                        if (lastTickAt === null) {
                          return;
                        }

                        const now = Date.now();
                        const elapsed = (now - lastTickAt) / 1000;
                        lastTickAt = now;
                        if (!Number.isFinite(elapsed) || elapsed <= 0 || elapsed > maxDeltaSeconds) {
                          return;
                        }

                        pendingSeconds += elapsed;
                      }

                      function flush(eventType, force) {
                        if (!force && pendingSeconds < thresholdSeconds) {
                          return;
                        }

                        if (pendingSeconds < minEventSeconds) {
                          return;
                        }

                        const listenedSeconds = Math.min(pendingSeconds, maxEventSeconds);
                        pendingSeconds = 0;
                        const positionSeconds = Number.isFinite(audio.currentTime) ? audio.currentTime : "";
                        const durationSeconds = Number.isFinite(audio.duration) && audio.duration > 0 ? audio.duration : "";
                        const query = new URLSearchParams({
                          eventType: eventType,
                          listenedSeconds: listenedSeconds.toFixed(3),
                          positionSeconds: positionSeconds === "" ? "" : positionSeconds.toFixed(3),
                          durationSeconds: durationSeconds === "" ? "" : durationSeconds.toFixed(3),
                          isCompleted: String(eventType === "ended")
                        });
                        window.location.href = "schink-track://listen?" + query.toString();
                      }

                      audio.addEventListener("play", function () {
                        lastTickAt = Date.now();
                      });
                      audio.addEventListener("pause", function () {
                        captureDelta();
                        flush("pause", true);
                        lastTickAt = null;
                      });
                      audio.addEventListener("timeupdate", function () {
                        if (!audio.paused) {
                          captureDelta();
                          flush("progress", false);
                        }
                      });
                      audio.addEventListener("ended", function () {
                        captureDelta();
                        flush("ended", true);
                        lastTickAt = null;
                      });
                      document.addEventListener("visibilitychange", function () {
                        if (document.visibilityState === "hidden") {
                          captureDelta();
                          flush("visibilityhidden", true);
                          lastTickAt = null;
                        } else if (!audio.paused) {
                          lastTickAt = Date.now();
                        }
                      });
                    })();
                  </script>
                </body>
                </html>
                """
            }
        };

        webView.Navigating += (_, args) =>
        {
            if (!Uri.TryCreate(args.Url, UriKind.Absolute, out var uri) ||
                !string.Equals(uri.Scheme, "schink-track", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            args.Cancel = true;
            if (!string.Equals(uri.Host, "listen", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _ = TrackMobileListenAsync(detail, trackingSessionId, uri);
        };

        return new Border
        {
            BackgroundColor = Colors.White,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 24 },
            HeightRequest = 140,
            Content = webView
        };
    }

    private Task TrackMobileListenAsync(MobileStoryDetailResponse detail, Guid trackingSessionId, Uri trackingUri)
    {
        var query = ParseQuery(trackingUri.Query);
        var eventType = query.TryGetValue("eventType", out var eventTypeValue) && !string.IsNullOrWhiteSpace(eventTypeValue)
            ? eventTypeValue
            : "progress";
        var listenedSeconds = ParseDecimal(query, "listenedSeconds") ?? 0;
        var positionSeconds = ParseDecimal(query, "positionSeconds");
        var durationSeconds = ParseDecimal(query, "durationSeconds");
        var isCompleted = query.TryGetValue("isCompleted", out var completedValue) &&
            bool.TryParse(completedValue, out var completed) &&
            completed;

        return _apiClient.TrackStoryListenAsync(
            detail.Story.Slug,
            detail.Story.Source,
            trackingSessionId,
            eventType,
            listenedSeconds,
            positionSeconds,
            durationSeconds,
            isCompleted);
    }

    private static decimal? ParseDecimal(IReadOnlyDictionary<string, string> query, string key)
    {
        return query.TryGetValue(key, out var value) &&
            decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static IReadOnlyDictionary<string, string> ParseQuery(string query)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separatorIndex = pair.IndexOf('=', StringComparison.Ordinal);
            var key = separatorIndex >= 0 ? pair[..separatorIndex] : pair;
            var value = separatorIndex >= 0 ? pair[(separatorIndex + 1)..] : string.Empty;
            values[Uri.UnescapeDataString(key)] = Uri.UnescapeDataString(value.Replace("+", " ", StringComparison.Ordinal));
        }

        return values;
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
