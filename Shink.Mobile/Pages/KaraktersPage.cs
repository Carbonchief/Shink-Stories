using Shink.Mobile.Models;
using Shink.Mobile.Services;

namespace Shink.Mobile.Pages;

public sealed class KaraktersPage : ContentPage
{
    private readonly MobileApiClient _apiClient;
    private readonly SessionState _sessionState;
    private readonly VerticalStackLayout _content;
    private readonly RefreshView _refreshView;
    private readonly Dictionary<string, ImageSource> _imageSourceCache = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _imageWarmupCancellation;

    public KaraktersPage(MobileApiClient apiClient, SessionState sessionState)
    {
        _apiClient = apiClient;
        _sessionState = sessionState;
        Title = "Karakters";
        BackgroundColor = Color.FromArgb("#FFF7E8");
        SafeAreaEdges = new SafeAreaEdges(SafeAreaRegions.Container);
        Shell.SetNavBarIsVisible(this, false);

        _content = new VerticalStackLayout
        {
            Padding = new Thickness(18, 18, 18, 28),
            Spacing = 16
        };

        _refreshView = new RefreshView
        {
            Background = Brush.Transparent,
            Content = new ScrollView { Content = _content },
            Command = new Command(async () => await LoadAsync())
        };

        Content = _refreshView;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_content.Children.Count == 0)
        {
            await LoadAsync();
        }
    }

    protected override void OnDisappearing()
    {
        _imageWarmupCancellation?.Cancel();
        base.OnDisappearing();
    }

    private async Task LoadAsync()
    {
        _imageWarmupCancellation?.Cancel();
        _content.Children.Clear();
        _content.Children.Add(MobileTopBar.Build(this, _apiClient, _sessionState.Current, leftAction: "back"));
        _content.Children.Add(new ActivityIndicator
        {
            IsRunning = true,
            Color = Color.FromArgb("#146D69"),
            Margin = new Thickness(0, 40, 0, 0)
        });

        try
        {
            var response = await _apiClient.GetCharactersAsync();
            _content.Children.Clear();
            _content.Children.Add(MobileTopBar.Build(this, _apiClient, _sessionState.Current, leftAction: "back"));

            if (response is null)
            {
                _content.Children.Add(BuildState("Kon nie die karakterblad laai nie."));
                return;
            }

            RenderCharacters(response);
            StartImageWarmup(response);
        }
        catch (Exception ex)
        {
            _content.Children.Clear();
            _content.Children.Add(MobileTopBar.Build(this, _apiClient, _sessionState.Current, leftAction: "back"));
            _content.Children.Add(BuildState(BuildLoadErrorMessage(ex), isError: true));
        }
        finally
        {
            _refreshView.IsRefreshing = false;
        }
    }

    private void RenderCharacters(MobileCharactersResponse response)
    {
        _content.Children.Clear();
        _content.Children.Add(MobileTopBar.Build(this, _apiClient, _sessionState.Current, leftAction: "back"));
        _content.Children.Add(BuildHero(response));
        if (response.Characters.Count == 0)
        {
            _content.Children.Add(BuildState("Geen karakters is nog beskikbaar nie."));
            return;
        }

        foreach (var character in response.Characters)
        {
            _content.Children.Add(BuildCharacterCard(character));
        }
    }

    private static View BuildHero(MobileCharactersResponse response)
    {
        var summaryText = response.IsSignedIn
            ? $"{response.UnlockedCount} van {response.TotalCount} karakters is oop"
            : "Teken in om julle ontsluitings te sien.";

        return new Border
        {
            BackgroundColor = Color.FromArgb("#222222"),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 26 },
            Padding = new Thickness(18, 20),
            Content = new VerticalStackLayout
            {
                Spacing = 10,
                Children =
                {
                    new Image
                    {
                        Source = "schink_stories_logo_white.png",
                        HeightRequest = 70,
                        Aspect = Aspect.AspectFit,
                        HorizontalOptions = LayoutOptions.Center
                    },
                    new Label
                    {
                        Text = "Ontmoet die karakters",
                        FontSize = 28,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = Colors.White,
                        HorizontalTextAlignment = TextAlignment.Center
                    },
                    new Label
                    {
                        Text = "Luister na stories om elke karakter se profiel oop te sluit.",
                        FontSize = 15,
                        TextColor = Color.FromArgb("#F8E9C9"),
                        HorizontalTextAlignment = TextAlignment.Center,
                        LineBreakMode = LineBreakMode.WordWrap
                    },
                    new Border
                    {
                        BackgroundColor = Color.FromArgb("#FFF7E8"),
                        StrokeThickness = 0,
                        StrokeShape = new RoundRectangle { CornerRadius = 999 },
                        Padding = new Thickness(14, 8),
                        HorizontalOptions = LayoutOptions.Center,
                        Content = new Label
                        {
                            Text = summaryText,
                            FontSize = 13,
                            FontAttributes = FontAttributes.Bold,
                            TextColor = Color.FromArgb("#243238")
                        }
                    }
                }
            }
        };
    }

    private View BuildCharacterCard(MobileCharacterCard character)
    {
        var body = new VerticalStackLayout
        {
            Spacing = 12
        };

        body.Children.Add(BuildCharacterHeader(character));
        body.Children.Add(new Label
        {
            Text = character.SummaryText,
            FontSize = 14,
            TextColor = Color.FromArgb("#52605C"),
            LineBreakMode = LineBreakMode.WordWrap
        });

        if (character.IsUnlocked)
        {
            var facts = BuildFacts(character);
            if (facts.Children.Count > 0)
            {
                body.Children.Add(facts);
            }
        }

        if (character.PrimaryStory is not null && !string.IsNullOrWhiteSpace(character.CallToActionLabel))
        {
            body.Children.Add(BuildStoryButton(character));
        }

        return new Border
        {
            BackgroundColor = Color.FromArgb("#FFFDF7"),
            Stroke = character.IsUnlocked ? Color.FromArgb("#E7D1A2") : Color.FromArgb("#D7CDC0"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 24 },
            Padding = 14,
            Shadow = new Shadow
            {
                Brush = Brush.Black,
                Offset = new Point(0, 8),
                Radius = 18,
                Opacity = 0.08f
            },
            Content = body
        };
    }

    private View BuildCharacterHeader(MobileCharacterCard character)
    {
        var imageFrame = new Grid
        {
            WidthRequest = 114,
            HeightRequest = 132,
            HorizontalOptions = LayoutOptions.Center,
            Children =
            {
                new Border
                {
                    BackgroundColor = Color.FromArgb("#F3E6CC"),
                    StrokeThickness = 0,
                    StrokeShape = new RoundRectangle { CornerRadius = 22 },
                    Padding = 8,
                    Content = new Image
                    {
                        Source = BuildCharacterImageSource(character.ImageUrl),
                        Aspect = Aspect.AspectFit,
                        AutomationId = $"character-image-{character.Slug}"
                    }
                }
            }
        };

        if (!character.IsUnlocked)
        {
            imageFrame.Children.Add(new Border
            {
                BackgroundColor = Color.FromArgb("#D9222222"),
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = 999 },
                WidthRequest = 38,
                HeightRequest = 38,
                HorizontalOptions = LayoutOptions.End,
                VerticalOptions = LayoutOptions.Start,
                Content = new Label
                {
                    Text = "!",
                    FontSize = 18,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Colors.White,
                    HorizontalTextAlignment = TextAlignment.Center,
                    VerticalTextAlignment = TextAlignment.Center
                }
            });
        }

        var textStack = new VerticalStackLayout
        {
            Spacing = 7,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Label
                {
                    Text = character.Heading,
                    FontSize = 24,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Color.FromArgb("#243238"),
                    LineBreakMode = LineBreakMode.WordWrap
                },
                new Label
                {
                    Text = character.IsUnlocked ? "Profiel oop" : "Nog gesluit",
                    FontSize = 12,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = character.IsUnlocked ? Color.FromArgb("#146D69") : Color.FromArgb("#7B6553")
                }
            }
        };

        var header = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star)
            },
            ColumnSpacing = 14,
            Children =
            {
                imageFrame,
                textStack
            }
        };
        Grid.SetColumn(textStack, 1);
        return header;
    }

    private static VerticalStackLayout BuildFacts(MobileCharacterCard character)
    {
        var facts = new VerticalStackLayout
        {
            Spacing = 8
        };

        AddFact(facts, "Spesie", character.Species);
        AddFact(facts, "Habitat", character.Habitat);
        AddFact(facts, "Gunsteling", character.FavoriteThing);
        AddFact(facts, "Karaktertrek", character.CharacterTrait);
        AddFact(facts, "Goue les", character.GoldenLesson);
        AddFact(facts, "Kernwaarde", character.CoreValue);
        AddFact(facts, "Vriende", character.Friends);
        AddFact(facts, "Dink-vraag", character.ReflectionQuestion);
        AddFact(facts, "Uitdaging", character.ChallengeText);
        return facts;
    }

    private static void AddFact(VerticalStackLayout facts, string label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        facts.Children.Add(new Border
        {
            BackgroundColor = Color.FromArgb("#F7EEDC"),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 16 },
            Padding = new Thickness(12, 10),
            Content = new VerticalStackLayout
            {
                Spacing = 2,
                Children =
                {
                    new Label
                    {
                        Text = label,
                        FontSize = 11,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = Color.FromArgb("#7B6553")
                    },
                    new Label
                    {
                        Text = value.Trim(),
                        FontSize = 14,
                        TextColor = Color.FromArgb("#243238"),
                        LineBreakMode = LineBreakMode.WordWrap
                    }
                }
            }
        });
    }

    private View BuildStoryButton(MobileCharacterCard character)
    {
        var button = new Button
        {
            Text = character.CallToActionLabel,
            BackgroundColor = Color.FromArgb("#146D69"),
            TextColor = Colors.White,
            FontAttributes = FontAttributes.Bold,
            CornerRadius = 18,
            HeightRequest = 52
        };

        button.Clicked += async (_, _) => await OpenPrimaryStoryAsync(character);
        return button;
    }

    private async Task OpenPrimaryStoryAsync(MobileCharacterCard character)
    {
        var story = character.PrimaryStory;
        if (story is null)
        {
            return;
        }

        await Shell.Current.GoToAsync(
            $"{nameof(StoryDetailPage)}?slug={Uri.EscapeDataString(story.Slug)}&source={Uri.EscapeDataString(story.Source)}",
            animate: true);
    }

    private static View BuildState(string message, bool isError = false) =>
        new Border
        {
            BackgroundColor = Colors.White,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 22 },
            Padding = 18,
            Content = new Label
            {
                Text = message,
                FontSize = 15,
                TextColor = isError ? Color.FromArgb("#B42318") : Color.FromArgb("#52605C"),
                HorizontalTextAlignment = TextAlignment.Center,
                LineBreakMode = LineBreakMode.WordWrap
            }
        };

    private ImageSource BuildCharacterImageSource(string? url)
    {
        var cacheKey = url?.Trim() ?? string.Empty;
        if (_imageSourceCache.TryGetValue(cacheKey, out var source))
        {
            return source;
        }

        source = _apiClient.BuildCachedImageSource(url, "schink_background.jpeg");
        _imageSourceCache[cacheKey] = source;
        return source;
    }

    private void StartImageWarmup(MobileCharactersResponse response)
    {
        _imageWarmupCancellation?.Cancel();
        _imageWarmupCancellation?.Dispose();
        _imageWarmupCancellation = new CancellationTokenSource();
        var token = _imageWarmupCancellation.Token;
        var imageUrls = response.Characters
            .Select(character => character.ImageUrl)
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (imageUrls.Length == 0)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await _apiClient.CacheImagesAsync(
                    imageUrls,
                    token,
                    maxImages: 48,
                    maxDegreeOfParallelism: 3);
                if (token.IsCancellationRequested || Handler is null)
                {
                    return;
                }

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (token.IsCancellationRequested || Handler is null)
                    {
                        return;
                    }

                    _imageSourceCache.Clear();
                    RenderCharacters(response);
                });
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
                // Character artwork warmup is best-effort; remote image sources remain available.
            }
        }, token);
    }

    private static string BuildLoadErrorMessage(Exception ex)
    {
        if (!string.IsNullOrWhiteSpace(ex.Message) &&
            ex.Message.Contains("Karakters-data", StringComparison.OrdinalIgnoreCase))
        {
            return ex.Message;
        }

        return "Kon nie die Karakters-data laai nie. Probeer asseblief weer.";
    }
}
