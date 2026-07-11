using Microsoft.Maui.Layouts;
using Shink.Mobile.Models;
using Shink.Mobile.Services;

namespace Shink.Mobile.Pages;

public sealed class KaraktersPage : ContentPage, IQueryAttributable
{
    private readonly MobileApiClient _apiClient;
    private readonly SessionState _sessionState;
    private readonly IAudioPlaybackService _audioPlaybackService;
    private readonly CollectionView _charactersView;
    private readonly RefreshView _refreshView;
    private readonly Dictionary<string, ImageSource> _imageSourceCache = new(StringComparer.OrdinalIgnoreCase);
    private MobileCharactersResponse? _response;
    private string? _pendingCharacterSlug;
    private bool _isPageActive;
    private CancellationTokenSource? _imageWarmupCancellation;
    private CancellationTokenSource? _loadCancellation;

    public KaraktersPage(
        MobileApiClient apiClient,
        SessionState sessionState,
        IAudioPlaybackService audioPlaybackService)
    {
        _apiClient = apiClient;
        _sessionState = sessionState;
        _audioPlaybackService = audioPlaybackService;
        Title = "Karakters";
        BackgroundColor = Color.FromArgb("#FFF7E8");
        SafeAreaEdges = new SafeAreaEdges(SafeAreaRegions.Container);
        Shell.SetNavBarIsVisible(this, false);

        _charactersView = new CollectionView
        {
            Background = Brush.Transparent,
            ItemsSource = Array.Empty<MobileCharacterCard>(),
            ItemSizingStrategy = ItemSizingStrategy.MeasureAllItems,
            SelectionMode = SelectionMode.None,
            ItemsLayout = new LinearItemsLayout(ItemsLayoutOrientation.Vertical)
            {
                ItemSpacing = 16
            },
            ItemTemplate = new DataTemplate(BuildCharacterItemView),
            Margin = new Thickness(18, 0, 18, 0),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Never,
            VerticalScrollBarVisibility = ScrollBarVisibility.Never
        };

        _refreshView = new RefreshView
        {
            Background = Brush.Transparent,
            Content = _charactersView,
            Command = new Command(async () => await LoadAsync(forceRefresh: true))
        };

        Content = _refreshView;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("karakter", out var value))
        {
            _pendingCharacterSlug = Uri.UnescapeDataString(value?.ToString() ?? string.Empty);
            _ = TryOpenPendingCharacterAsync();
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _isPageActive = true;
        if (_response is null)
        {
            await LoadAsync();
        }
        else
        {
            await TryOpenPendingCharacterAsync();
        }
    }

    protected override void OnDisappearing()
    {
        _isPageActive = false;
        _loadCancellation?.Cancel();
        _imageWarmupCancellation?.Cancel();
        base.OnDisappearing();
    }

    private async Task LoadAsync(bool forceRefresh = false)
    {
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = new CancellationTokenSource();
        var cancellationToken = _loadCancellation.Token;
        _imageWarmupCancellation?.Cancel();
        var renderedCachedData = _response is not null;
        if (!forceRefresh && !renderedCachedData)
        {
            var cachedResponse = await _apiClient.GetCachedCharactersAsync(cancellationToken);
            if (cachedResponse is not null && !cancellationToken.IsCancellationRequested)
            {
                _response = cachedResponse;
                RenderCharacters(cachedResponse);
                StartImageWarmup(cachedResponse);
                renderedCachedData = true;
            }
        }

        if (!renderedCachedData)
        {
            RenderLoadingState();
        }

        try
        {
            var response = await _apiClient.GetCharactersAsync(cancellationToken);
            if (cancellationToken.IsCancellationRequested || !_isPageActive)
            {
                return;
            }

            if (response is null)
            {
                if (!renderedCachedData)
                {
                    RenderState("Kon nie die karakterblad laai nie.");
                }
                return;
            }

            _response = response;
            RenderCharacters(response);
            StartImageWarmup(response);
            await TryOpenPendingCharacterAsync();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (!renderedCachedData && _isPageActive)
            {
                RenderState(BuildLoadErrorMessage(ex), isError: true);
            }
        }
        finally
        {
            _refreshView.IsRefreshing = false;
        }
    }

    private void RenderCharacters(MobileCharactersResponse response)
    {
        _charactersView.Header = BuildPageHeader(response);
        _charactersView.ItemsSource = response.Characters;
        _charactersView.Footer = response.Characters.Count == 0
            ? BuildState("Geen karakters is nog beskikbaar nie.")
            : new BoxView { HeightRequest = 28, Color = Colors.Transparent };
    }

    private View BuildCharacterItemView()
    {
        var host = new ContentView();
        host.BindingContextChanged += (_, _) =>
        {
            host.Content = host.BindingContext is MobileCharacterCard character
                ? BuildCharacterCard(character)
                : null;
        };
        return host;
    }

    private View BuildPageHeader(MobileCharactersResponse response) =>
        new VerticalStackLayout
        {
            Padding = new Thickness(0, 18, 0, 16),
            Spacing = 16,
            Children =
            {
                MobileTopBar.Build(this, _apiClient, _sessionState.Current, leftAction: "back"),
                BuildHero(response)
            }
        };

    private void RenderLoadingState()
    {
        _charactersView.Header = new VerticalStackLayout
        {
            Padding = new Thickness(0, 18, 0, 16),
            Spacing = 16,
            Children =
            {
                MobileTopBar.Build(this, _apiClient, _sessionState.Current, leftAction: "back"),
                new ActivityIndicator
                {
                    IsRunning = true,
                    Color = Color.FromArgb("#146D69"),
                    Margin = new Thickness(0, 40, 0, 0)
                }
            }
        };
        _charactersView.ItemsSource = Array.Empty<MobileCharacterCard>();
        _charactersView.Footer = null;
    }

    private void RenderState(string message, bool isError = false)
    {
        _charactersView.Header = new VerticalStackLayout
        {
            Padding = new Thickness(0, 18, 0, 16),
            Spacing = 16,
            Children =
            {
                MobileTopBar.Build(this, _apiClient, _sessionState.Current, leftAction: "back"),
                BuildState(message, isError)
            }
        };
        _charactersView.ItemsSource = Array.Empty<MobileCharacterCard>();
        _charactersView.Footer = null;
    }

    private View BuildHero(MobileCharactersResponse response)
    {
        var summary = new VerticalStackLayout
        {
            Spacing = 6,
            HorizontalOptions = LayoutOptions.Center,
            Children =
            {
                new Label
                {
                    Text = response.IsSignedIn
                        ? $"{response.UnlockedCount} van {response.TotalCount} karakters is oop"
                        : "Teken in om julle ontsluitings te sien.",
                    FontSize = 13,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Color.FromArgb("#243238"),
                    HorizontalTextAlignment = TextAlignment.Center
                }
            }
        };

        if (!response.IsSignedIn)
        {
            var signInButton = new Button
            {
                Text = "Teken in",
                BackgroundColor = Colors.Transparent,
                TextColor = Color.FromArgb("#146D69"),
                FontAttributes = FontAttributes.Bold,
                Padding = new Thickness(10, 2),
                HeightRequest = 36
            };
            signInButton.Clicked += async (_, _) => await Shell.Current.GoToAsync(nameof(AccountPage), animate: true);
            summary.Children.Add(signInButton);
        }

        return new Border
        {
            BackgroundColor = Color.FromArgb("#222222"),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 24 },
            Padding = new Thickness(18, 18, 18, 20),
            Content = new VerticalStackLayout
            {
                Spacing = 10,
                Children =
                {
                    new Image
                    {
                        Source = "schink_character_lineup.png",
                        HeightRequest = 150,
                        Aspect = Aspect.AspectFit,
                        HorizontalOptions = LayoutOptions.Fill
                    },
                    new Label
                    {
                        Text = "Schink Stories Karakters",
                        FontSize = 12,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = Color.FromArgb("#FFD45A"),
                        HorizontalTextAlignment = TextAlignment.Center
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
                        Text = "Op hierdie blad kan jy die Schink Stories-karakters sien, verken en hoor. Luister na stories om elke karakter se profiel oop te sluit. Of luister meer as een keer na spesifieke stories en ontdek bonuskarakters.",
                        FontSize = 14,
                        TextColor = Color.FromArgb("#F8E9C9"),
                        HorizontalTextAlignment = TextAlignment.Center,
                        LineBreakMode = LineBreakMode.WordWrap
                    },
                    new Border
                    {
                        BackgroundColor = Color.FromArgb("#FFF7E8"),
                        StrokeThickness = 0,
                        StrokeShape = new RoundRectangle { CornerRadius = 18 },
                        Padding = new Thickness(14, 8),
                        HorizontalOptions = LayoutOptions.Center,
                        Content = summary
                    }
                }
            }
        };
    }

    private View BuildCharacterCard(MobileCharacterCard character)
    {
        var body = new VerticalStackLayout
        {
            Spacing = 12,
            Children =
            {
                BuildCharacterHeader(character),
                new Label
                {
                    Text = character.SummaryText,
                    FontSize = 14,
                    TextColor = Color.FromArgb("#52605C"),
                    LineBreakMode = LineBreakMode.WordWrap
                }
            }
        };

        if (character.PrimaryStory is not null && !string.IsNullOrWhiteSpace(character.CallToActionLabel))
        {
            body.Children.Add(BuildStoryButton(character));
        }

        var card = new Border
        {
            BackgroundColor = Color.FromArgb("#FFFDF7"),
            Stroke = character.IsUnlocked ? Color.FromArgb("#E7D1A2") : Color.FromArgb("#D7CDC0"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 22 },
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

        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) =>
        {
            if (character.IsUnlocked)
            {
                await ShowCharacterProfileAsync(character);
                return;
            }

            await ShakeLockedCardAsync(card);
        };
        card.GestureRecognizers.Add(tap);
        return card;
    }

    private View BuildCharacterHeader(MobileCharacterCard character)
    {
        var imageFrame = new Grid
        {
            WidthRequest = 114,
            HeightRequest = 132,
            Children =
            {
                new Border
                {
                    BackgroundColor = Color.FromArgb("#F3E6CC"),
                    StrokeThickness = 0,
                    StrokeShape = new RoundRectangle { CornerRadius = 20 },
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
            imageFrame.Children.Add(BuildRoundBadge("🔒", "Nog gesluit", Color.FromArgb("#AA222222")));
        }
        else if (character.PreviewAudioClips.Count > 0)
        {
            var audioButton = BuildRoundButton("🔊", $"Speel {character.DisplayName} se stem");
            audioButton.Clicked += async (_, _) => await PlayCharacterAudioAsync(character);
            imageFrame.Children.Add(audioButton);
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
            Children = { imageFrame, textStack }
        };
        Grid.SetColumn(textStack, 1);
        return header;
    }

    private static Border BuildRoundBadge(string text, string automationName, Color backgroundColor) =>
        new()
        {
            BackgroundColor = backgroundColor,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 999 },
            WidthRequest = 40,
            HeightRequest = 40,
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Start,
            AutomationId = automationName,
            Content = new Label
            {
                Text = text,
                FontSize = 17,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center
            }
        };

    private static Button BuildRoundButton(string text, string automationName) =>
        new()
        {
            Text = text,
            FontSize = 17,
            BackgroundColor = Color.FromArgb("#146D69"),
            TextColor = Colors.White,
            CornerRadius = 20,
            WidthRequest = 40,
            HeightRequest = 40,
            Padding = 0,
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Start,
            AutomationId = automationName
        };

    private View BuildStoryButton(MobileCharacterCard character)
    {
        var button = new Button
        {
            Text = character.CallToActionLabel,
            BackgroundColor = Color.FromArgb("#146D69"),
            TextColor = Colors.White,
            FontAttributes = FontAttributes.Bold,
            CornerRadius = 16,
            HeightRequest = 50
        };
        button.Clicked += async (_, _) => await OpenPrimaryStoryAsync(character);
        return button;
    }

    private async Task ShowCharacterProfileAsync(MobileCharacterCard character)
    {
        if (!character.IsUnlocked)
        {
            return;
        }

        var closeButton = new Button
        {
            Text = "✕",
            FontSize = 20,
            BackgroundColor = Color.FromArgb("#146D69"),
            TextColor = Colors.White,
            CornerRadius = 22,
            WidthRequest = 44,
            HeightRequest = 44,
            Padding = 0,
            HorizontalOptions = LayoutOptions.End
        };
        var profileImage = new Image
        {
            Source = BuildCharacterImageSource(character.ImageUrl),
            HeightRequest = 260,
            Aspect = Aspect.AspectFit,
            HorizontalOptions = LayoutOptions.Fill
        };
        var imageButton = new Border
        {
            BackgroundColor = Color.FromArgb("#F3E6CC"),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 22 },
            Padding = 10,
            Content = profileImage
        };
        if (character.PreviewAudioClips.Count > 0)
        {
            var imageTap = new TapGestureRecognizer();
            imageTap.Tapped += async (_, _) =>
            {
                HapticFeedback.Default.Perform(HapticFeedbackType.Click);
                await imageButton.ScaleToAsync(1.04, 100, Easing.CubicOut);
                await imageButton.ScaleToAsync(1, 140, Easing.CubicIn);
                await PlayCharacterAudioAsync(character);
            };
            imageButton.GestureRecognizers.Add(imageTap);
        }

        var profileContent = new VerticalStackLayout
        {
            Padding = new Thickness(18, 12, 18, 32),
            Spacing = 14,
            Children =
            {
                closeButton,
                imageButton,
                new Label
                {
                    Text = character.DisplayName,
                    FontSize = 30,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Color.FromArgb("#243238"),
                    HorizontalTextAlignment = TextAlignment.Center
                }
            }
        };

        if (!string.IsNullOrWhiteSpace(character.Tagline))
        {
            profileContent.Children.Add(new Label
            {
                Text = character.Tagline,
                FontSize = 15,
                TextColor = Color.FromArgb("#52605C"),
                HorizontalTextAlignment = TextAlignment.Center,
                LineBreakMode = LineBreakMode.WordWrap
            });
        }

        AddProfilePanel(profileContent,
            ("Tipe", character.Species),
            ("Blyplek", character.Habitat),
            ("Sê-ding", character.Catchphrase),
            ("Gunsteling-ding", character.FavoriteThing),
            ("Eerste verskyning", character.FirstAppearance));
        AddProfilePanel(profileContent,
            ("Kenmerk", character.CharacterTrait),
            ("Goue Les", character.GoldenLesson),
            ("Kernwaarde", character.CoreValue));
        AddProfilePanel(profileContent,
            ("Vragie", character.ReflectionQuestion),
            ("Uitdaging vir jou", character.ChallengeText));

        var friends = BuildFriends(character);
        if (friends is not null)
        {
            profileContent.Children.Add(friends);
        }

        var relatedStories = BuildRelatedStories(character);
        if (relatedStories is not null)
        {
            profileContent.Children.Add(relatedStories);
        }

        var page = new ContentPage
        {
            Title = character.DisplayName,
            BackgroundColor = Color.FromArgb("#FFF7E8"),
            SafeAreaEdges = new SafeAreaEdges(SafeAreaRegions.Container),
            Content = new ScrollView { Content = profileContent }
        };
        Shell.SetNavBarIsVisible(page, false);
        closeButton.Clicked += async (_, _) => await Navigation.PopModalAsync(true);
        await Navigation.PushModalAsync(page, true);
    }

    private static void AddProfilePanel(
        VerticalStackLayout content,
        params (string Label, string? Value)[] fields)
    {
        var panel = new VerticalStackLayout { Spacing = 10 };
        foreach (var field in fields.Where(field => !string.IsNullOrWhiteSpace(field.Value)))
        {
            panel.Children.Add(new VerticalStackLayout
            {
                Spacing = 2,
                Children =
                {
                    new Label
                    {
                        Text = field.Label,
                        FontSize = 13,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = Color.FromArgb("#183D49")
                    },
                    new Label
                    {
                        Text = field.Value!.Trim(),
                        FontSize = 15,
                        TextColor = Color.FromArgb("#26373E"),
                        LineBreakMode = LineBreakMode.WordWrap
                    }
                }
            });
        }

        if (panel.Children.Count == 0)
        {
            return;
        }

        content.Children.Add(new Border
        {
            BackgroundColor = Color.FromArgb("#FFFDF7"),
            Stroke = Color.FromArgb("#E7D1A2"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 18 },
            Padding = 15,
            Content = panel
        });
    }

    private View? BuildFriends(MobileCharacterCard character)
    {
        var friendNames = SplitFriendNames(character.Friends);
        if (friendNames.Count == 0 || _response is null)
        {
            return null;
        }

        var layout = new FlexLayout
        {
            Direction = FlexDirection.Row,
            Wrap = FlexWrap.Wrap,
            JustifyContent = FlexJustify.SpaceAround,
            AlignItems = FlexAlignItems.Start
        };

        foreach (var friendName in friendNames)
        {
            var friend = _response.Characters.FirstOrDefault(candidate =>
                string.Equals(NormalizeFriendToken(candidate.DisplayName), NormalizeFriendToken(friendName), StringComparison.Ordinal) ||
                string.Equals(NormalizeFriendToken(candidate.Slug.Replace("-", " ")), NormalizeFriendToken(friendName), StringComparison.Ordinal));
            if (friend is null)
            {
                layout.Children.Add(BuildUnmatchedFriend(friendName));
                continue;
            }

            var tile = BuildFriendTile(friend);
            if (friend.IsUnlocked)
            {
                var tap = new TapGestureRecognizer();
                tap.Tapped += async (_, _) =>
                {
                    await Navigation.PopModalAsync(false);
                    await ShowCharacterProfileAsync(friend);
                };
                tile.GestureRecognizers.Add(tap);
            }
            layout.Children.Add(tile);
        }

        return BuildSection("Maats", layout);
    }

    private View BuildFriendTile(MobileCharacterCard friend)
    {
        var tile = new VerticalStackLayout
        {
            WidthRequest = 92,
            Padding = 6,
            Spacing = 5,
            Children =
            {
                new Image
                {
                    Source = BuildCharacterImageSource(friend.ImageUrl),
                    HeightRequest = 72,
                    WidthRequest = 72,
                    Aspect = Aspect.AspectFit
                },
                new Label
                {
                    Text = friend.Heading,
                    FontSize = 12,
                    TextColor = Color.FromArgb("#26373E"),
                    HorizontalTextAlignment = TextAlignment.Center,
                    LineBreakMode = LineBreakMode.WordWrap
                }
            }
        };
        return tile;
    }

    private static View BuildUnmatchedFriend(string name)
    {
        var tile = new VerticalStackLayout
        {
            WidthRequest = 92,
            Padding = 6,
            Spacing = 5,
            Children =
            {
                new Label
                {
                    Text = "?",
                    FontSize = 34,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Color.FromArgb("#7B6553"),
                    HeightRequest = 72,
                    HorizontalTextAlignment = TextAlignment.Center,
                    VerticalTextAlignment = TextAlignment.Center
                },
                new Label
                {
                    Text = name,
                    FontSize = 12,
                    TextColor = Color.FromArgb("#26373E"),
                    HorizontalTextAlignment = TextAlignment.Center
                }
            }
        };
        return tile;
    }

    private View? BuildRelatedStories(MobileCharacterCard character)
    {
        if (character.RelatedStories.Count == 0)
        {
            return null;
        }

        var stories = new HorizontalStackLayout { Spacing = 12 };
        foreach (var story in character.RelatedStories)
        {
            var tile = new VerticalStackLayout
            {
                WidthRequest = 132,
                Spacing = 7,
                Children =
                {
                    new Border
                    {
                        BackgroundColor = Color.FromArgb("#F3E6CC"),
                        StrokeThickness = 0,
                        StrokeShape = new RoundRectangle { CornerRadius = 12 },
                        HeightRequest = 150,
                        Content = new Image
                        {
                            Source = BuildCharacterImageSource(story.ImageUrl),
                            Aspect = Aspect.AspectFill
                        }
                    },
                    new Label
                    {
                        Text = story.Title,
                        FontSize = 13,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = Color.FromArgb("#243238"),
                        LineBreakMode = LineBreakMode.WordWrap
                    }
                }
            };
            var tap = new TapGestureRecognizer();
            tap.Tapped += async (_, _) => await OpenStoryAsync(story);
            tile.GestureRecognizers.Add(tap);
            stories.Children.Add(tile);
        }

        return BuildSection(
            "Stories met hierdie karakter",
            new ScrollView { Orientation = ScrollOrientation.Horizontal, Content = stories });
    }

    private static View BuildSection(string heading, View content) =>
        new Border
        {
            BackgroundColor = Color.FromArgb("#FFFDF7"),
            Stroke = Color.FromArgb("#E7D1A2"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 18 },
            Padding = 14,
            Content = new VerticalStackLayout
            {
                Spacing = 10,
                Children =
                {
                    new Label
                    {
                        Text = heading,
                        FontSize = 16,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = Color.FromArgb("#183D49")
                    },
                    content
                }
            }
        };

    private async Task PlayCharacterAudioAsync(MobileCharacterCard character)
    {
        if (!character.IsUnlocked || character.PreviewAudioClips.Count == 0)
        {
            return;
        }

        var clip = character.PreviewAudioClips[Random.Shared.Next(character.PreviewAudioClips.Count)];
        try
        {
            var playbackUrl = await _apiClient.PrepareAudioPlaybackSourceAsync(
                clip.AudioUrl,
                character.Slug,
                "karakter");
            await _audioPlaybackService.PlayAsync(
                playbackUrl,
                new AudioPlaybackMetadata(
                    character.DisplayName,
                    "Schink Stories Karakters",
                    _apiClient.BuildImageUrl(character.ImageUrl)));
            _ = _apiClient.TrackCharacterProfileListenAsync(character.Slug, clip.StreamSlug);
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Kon nie karakterklank speel nie", ex.Message, "Maak toe");
        }
    }

    private async Task OpenPrimaryStoryAsync(MobileCharacterCard character)
    {
        if (character.PrimaryStory is not null)
        {
            await OpenStoryAsync(character.PrimaryStory);
        }
    }

    private async Task OpenStoryAsync(MobileCharacterStoryLink story)
    {
        if (Navigation.ModalStack.Count > 0)
        {
            await Navigation.PopModalAsync(false);
        }

        await Shell.Current.GoToAsync(
            $"{nameof(StoryDetailPage)}?slug={Uri.EscapeDataString(story.Slug)}&source={Uri.EscapeDataString(story.Source)}",
            animate: true);
    }

    private async Task TryOpenPendingCharacterAsync()
    {
        if (string.IsNullOrWhiteSpace(_pendingCharacterSlug) || _response is null)
        {
            return;
        }

        var character = _response.Characters.FirstOrDefault(candidate =>
            candidate.IsUnlocked &&
            string.Equals(candidate.Slug, _pendingCharacterSlug, StringComparison.OrdinalIgnoreCase));
        _pendingCharacterSlug = null;
        if (character is not null)
        {
            await ShowCharacterProfileAsync(character);
        }
    }

    private static async Task ShakeLockedCardAsync(VisualElement card)
    {
        HapticFeedback.Default.Perform(HapticFeedbackType.Click);
        foreach (var offset in new[] { -10d, 10d, -8d, 8d, -4d, 4d, 0d })
        {
            await card.TranslateToAsync(offset, 0, 55, Easing.CubicInOut);
        }
    }

    private static IReadOnlyList<string> SplitFriendNames(string? friends)
    {
        if (string.IsNullOrWhiteSpace(friends))
        {
            return Array.Empty<string>();
        }

        return friends
            .Replace(" en ", ",", StringComparison.OrdinalIgnoreCase)
            .Replace("&", ",", StringComparison.OrdinalIgnoreCase)
            .Replace("/", ",", StringComparison.OrdinalIgnoreCase)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string NormalizeFriendToken(string value) =>
        new(value.Trim().ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

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
            .SelectMany(character => character.RelatedStories
                .Select(story => story.ImageUrl)
                .Prepend(character.ImageUrl))
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
                    maxImages: 64,
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
