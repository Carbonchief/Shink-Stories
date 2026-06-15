using Shink.Mobile.Models;
using Shink.Mobile.Services;

namespace Shink.Mobile.Pages;

public sealed class LuisterPage : ContentPage
{
    private readonly MobileApiClient _apiClient;
    private readonly SessionState _sessionState;
    private readonly PlaylistPlaybackState _playlistPlaybackState;
    private readonly VerticalStackLayout _content;
    private readonly VerticalStackLayout _playlistContent;
    private readonly Entry _searchEntry;
    private readonly Entry _loginEmailEntry;
    private readonly Entry _loginPasswordEntry;
    private readonly Label _loginStatusLabel;
    private IReadOnlyList<MobileLuisterSection> _sections = Array.Empty<MobileLuisterSection>();
    private bool _hasLoaded;
    private bool _isSearchVisible;
    private CancellationTokenSource? _imageWarmupCancellation;

    public LuisterPage(
        MobileApiClient apiClient,
        SessionState sessionState,
        PlaylistPlaybackState playlistPlaybackState)
    {
        _apiClient = apiClient;
        _sessionState = sessionState;
        _playlistPlaybackState = playlistPlaybackState;
        Title = "Luister";
        BackgroundColor = Color.FromArgb("#FFF7E8");

        _content = new VerticalStackLayout
        {
            Padding = new Thickness(18, 18, 18, 28),
            Spacing = 16
        };
        _playlistContent = new VerticalStackLayout
        {
            Spacing = 14
        };

        _searchEntry = new Entry
        {
            Placeholder = "Soek stories",
            ClearButtonVisibility = ClearButtonVisibility.WhileEditing,
            Keyboard = Keyboard.Text
        };
        _searchEntry.TextChanged += (_, _) => RenderPlaylistContent();

        _loginEmailEntry = new Entry
        {
            Placeholder = "E-pos",
            Keyboard = Keyboard.Email
        };
        _loginPasswordEntry = new Entry
        {
            Placeholder = "Wagwoord",
            IsPassword = true
        };
        _loginStatusLabel = new Label
        {
            TextColor = Color.FromArgb("#5F5F5F"),
            FontSize = 13
        };

        Content = new RefreshView
        {
            Content = new ScrollView { Content = _content },
            Command = new Command(async () => await LoadAsync(forceRefresh: true))
        };

        _sessionState.Changed += _ => MainThread.BeginInvokeOnMainThread(RenderContent);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!_hasLoaded)
        {
            await LoadAsync(forceRefresh: true);
        }
        else
        {
            RenderContent();
            _ = RefreshSessionInBackgroundAsync();
        }
    }

    protected override void OnDisappearing()
    {
        _imageWarmupCancellation?.Cancel();
        base.OnDisappearing();
    }

    private async Task LoadAsync(bool forceRefresh = false)
    {
        if (_hasLoaded && !forceRefresh)
        {
            RenderContent();
            return;
        }

        _content.Children.Clear();
        _content.Children.Add(new ActivityIndicator { IsRunning = true, Color = Color.FromArgb("#0F766E") });

        try
        {
            var sessionTask = _apiClient.GetSessionAsync();
            var luisterTask = _apiClient.GetLuisterAsync();
            await Task.WhenAll(sessionTask, luisterTask);

            var response = await luisterTask;
            if (response is null)
            {
                _content.Children.Clear();
                _content.Children.Add(new Label { Text = "Kon nie luister stories laai nie." });
                return;
            }

            _sections = response.Sections is { Count: > 0 }
                ? response.Sections
                : BuildLegacySections(response.Playlists);
            _hasLoaded = true;
            RenderContent();
            StartImageWarmup();
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

    private async Task RefreshSessionInBackgroundAsync()
    {
        try
        {
            await _apiClient.GetSessionAsync();
            MainThread.BeginInvokeOnMainThread(RenderContent);
        }
        catch
        {
            // Keep cached Luister content visible if session refresh is temporarily unavailable.
        }
    }

    private void RenderContent()
    {
        if (!_hasLoaded)
        {
            return;
        }

        _content.Children.Clear();
        _content.Children.Add(BuildLuisterTopBar());
        if (_isSearchVisible || !string.IsNullOrWhiteSpace(_searchEntry.Text))
        {
            _content.Children.Add(BuildSearchBox());
        }

        _content.Children.Add(BuildAccountPanel());
        _content.Children.Add(_playlistContent);

        RenderPlaylistContent();
    }

    private void RenderPlaylistContent()
    {
        _playlistContent.Children.Clear();
        var filteredSections = FilterSections(_sections, _searchEntry.Text).ToArray();
        if (filteredSections.Length == 0)
        {
            _playlistContent.Children.Add(new Border
            {
                BackgroundColor = Colors.White,
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = 18 },
                Padding = 16,
                Content = new Label
                {
                    Text = "Geen stories pas by jou soektog nie.",
                    TextColor = Color.FromArgb("#5F5F5F")
                }
            });
            return;
        }

        foreach (var section in filteredSections)
        {
            if (IsSpeellysteSection(section))
            {
                _playlistContent.Children.Add(BuildPlaylistShowcase(section.Title, section.Playlists));
                continue;
            }

            if (section.Playlist is not null)
            {
                _playlistContent.Children.Add(BuildPlaylistSection(section.Playlist));
            }
        }
    }

    private View BuildLuisterTopBar()
    {
        var menuButton = BuildHeaderCircleButton("☰", 22, Colors.White, Color.FromArgb("#123F3F"));
        var menuTap = new TapGestureRecognizer();
        menuTap.Tapped += async (_, _) => await ShowMenuAsync();
        menuButton.GestureRecognizers.Add(menuTap);

        var searchButton = BuildHeaderCircleButton("⌕", 25, Color.FromArgb("#0B3534"), Color.FromArgb("#F4E9D1"));
        var searchTap = new TapGestureRecognizer();
        searchTap.Tapped += (_, _) =>
        {
            _isSearchVisible = !_isSearchVisible;
            RenderContent();
            if (_isSearchVisible)
            {
                MainThread.BeginInvokeOnMainThread(() => _searchEntry.Focus());
            }
        };
        searchButton.GestureRecognizers.Add(searchTap);

        var profileButton = BuildProfileButton();
        var profileTap = new TapGestureRecognizer();
        profileTap.Tapped += (_, _) => OpenAccountTab();
        profileButton.GestureRecognizers.Add(profileTap);

        var rightActions = new HorizontalStackLayout
        {
            Spacing = 10,
            HorizontalOptions = LayoutOptions.End,
            Children =
            {
                searchButton,
                profileButton
            }
        };

        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Auto }
            },
            Children =
            {
                menuButton,
                rightActions
            }
        };

        Grid.SetColumn(rightActions, 2);
        return grid;
    }

    private View BuildSearchBox()
    {
        return new Border
        {
            BackgroundColor = Colors.White,
            Stroke = Color.FromArgb("#E6DDCA"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 22 },
            Padding = new Thickness(16, 4),
            Shadow = new Shadow
            {
                Brush = Brush.Black,
                Offset = new Point(0, 4),
                Radius = 12,
                Opacity = 0.05f
            },
            Content = _searchEntry
        };
    }

    private static Border BuildHeaderCircleButton(string text, double fontSize, Color textColor, Color backgroundColor) =>
        new()
        {
            BackgroundColor = backgroundColor,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 23 },
            WidthRequest = 46,
            HeightRequest = 46,
            VerticalOptions = LayoutOptions.Center,
            Content = new Label
            {
                Text = text,
                FontSize = fontSize,
                FontAttributes = FontAttributes.Bold,
                TextColor = textColor,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center,
                Margin = text == "⌕" ? new Thickness(0, -2, 0, 0) : Thickness.Zero
            }
        };

    private Border BuildProfileButton()
    {
        var session = _sessionState.Current;
        if (string.IsNullOrWhiteSpace(session.ProfileImageUrl))
        {
            return BuildHeaderCircleButton(BuildInitials(session), 15, Color.FromArgb("#0B3534"), Color.FromArgb("#FFD45A"));
        }

        return new Border
        {
            BackgroundColor = Color.FromArgb("#F7EAD0"),
            Stroke = Colors.White,
            StrokeThickness = 2,
            StrokeShape = new RoundRectangle { CornerRadius = 23 },
            WidthRequest = 46,
            HeightRequest = 46,
            Padding = 0,
            VerticalOptions = LayoutOptions.Center,
            Content = new Image
            {
                Source = _apiClient.BuildCachedImageSource(session.ProfileImageUrl),
                Aspect = Aspect.AspectFill,
                WidthRequest = 46,
                HeightRequest = 46
            }
        };
    }

    private async Task ShowMenuAsync()
    {
        var choice = await DisplayActionSheetAsync("Menu", "Kanselleer", null, "Settings", "Manage Account");
        switch (choice)
        {
            case "Settings":
                await DisplayAlertAsync("Settings", "Instellings kom binnekort.", "Reg so");
                break;
            case "Manage Account":
                OpenAccountTab();
                break;
        }
    }

    private static void OpenAccountTab()
    {
        if (Shell.Current?.CurrentItem is not TabBar tabs)
        {
            return;
        }

        var accountTab = tabs.Items.FirstOrDefault(item =>
            string.Equals(item.Title, "Rekening", StringComparison.OrdinalIgnoreCase));
        if (accountTab is not null)
        {
            tabs.CurrentItem = accountTab;
        }
    }

    private static string BuildInitials(MobileSession session)
    {
        var source = !string.IsNullOrWhiteSpace(session.DisplayName)
            ? session.DisplayName
            : session.Email;

        if (string.IsNullOrWhiteSpace(source))
        {
            return "S";
        }

        var localName = source.Contains('@', StringComparison.Ordinal)
            ? source[..source.IndexOf('@')]
            : source;
        var tokens = localName
            .Split([' ', '.', '_', '-'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Take(2)
            .ToArray();

        if (tokens.Length >= 2)
        {
            return $"{char.ToUpperInvariant(tokens[0][0])}{char.ToUpperInvariant(tokens[1][0])}";
        }

        if (tokens.Length == 1)
        {
            var token = tokens[0];
            return token.Length >= 2
                ? $"{char.ToUpperInvariant(token[0])}{char.ToUpperInvariant(token[1])}"
                : char.ToUpperInvariant(token[0]).ToString();
        }

        return "S";
    }

    private View BuildAccountPanel()
    {
        var session = _sessionState.Current;
        if (session.IsSignedIn)
        {
            return new Border
            {
                BackgroundColor = Colors.White,
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = 18 },
                Padding = 14,
                Content = new HorizontalStackLayout
                {
                    Spacing = 10,
                    Children =
                    {
                        new Label
                        {
                            Text = session.HasPaidSubscription ? "Alles oop" : "Gratis toegang",
                            FontAttributes = FontAttributes.Bold,
                            TextColor = Color.FromArgb("#0F766E"),
                            VerticalTextAlignment = TextAlignment.Center
                        },
                        new Label
                        {
                            Text = session.Email ?? "Ingeteken",
                            TextColor = Color.FromArgb("#5F5F5F"),
                            VerticalTextAlignment = TextAlignment.Center
                        }
                    }
                }
            };
        }

        var loginButton = new Button
        {
            Text = "Teken in",
            BackgroundColor = Color.FromArgb("#0F766E"),
            TextColor = Colors.White,
            CornerRadius = 16
        };
        loginButton.Clicked += async (_, _) => await SignInAsync();

        var plansButton = new Button
        {
            Text = "Sien opsies",
            BackgroundColor = Color.FromArgb("#F3F4F6"),
            TextColor = Color.FromArgb("#222222"),
            CornerRadius = 16
        };
        plansButton.Clicked += async (_, _) =>
        {
            var plansUrl = string.IsNullOrWhiteSpace(_sessionState.Current.PlansUrl)
                ? _apiClient.BuildAbsoluteUrl("/opsies")
                : _sessionState.Current.PlansUrl;
            await Browser.OpenAsync(plansUrl, BrowserLaunchMode.External);
        };

        return new Border
        {
            BackgroundColor = Colors.White,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 22 },
            Padding = 16,
            Content = new VerticalStackLayout
            {
                Spacing = 12,
                Children =
                {
                    new Label
                    {
                        Text = "Teken in vir jou volle luistertoegang",
                        FontAttributes = FontAttributes.Bold,
                        FontSize = 18,
                        TextColor = Color.FromArgb("#222222")
                    },
                    new Label
                    {
                        Text = "Jy kan steeds rondkyk, maar geslote stories maak oop wanneer jou rekening aktief is.",
                        TextColor = Color.FromArgb("#5F5F5F"),
                        FontSize = 13
                    },
                    _loginEmailEntry,
                    _loginPasswordEntry,
                    new HorizontalStackLayout
                    {
                        Spacing = 10,
                        Children = { loginButton, plansButton }
                    },
                    _loginStatusLabel
                }
            }
        };
    }

    private async Task SignInAsync()
    {
        try
        {
            _loginStatusLabel.Text = "Teken in...";
            _loginStatusLabel.TextColor = Color.FromArgb("#5F5F5F");
            var result = await _apiClient.SignInAsync(_loginEmailEntry.Text ?? string.Empty, _loginPasswordEntry.Text ?? string.Empty);
            _loginPasswordEntry.Text = string.Empty;
            _loginStatusLabel.Text = result.Message;
            _loginStatusLabel.TextColor = Color.FromArgb("#0F766E");
            await LoadAsync(forceRefresh: true);
        }
        catch (Exception ex)
        {
            _loginStatusLabel.Text = ex.Message;
            _loginStatusLabel.TextColor = Color.FromArgb("#B42318");
        }
    }

    private View BuildPlaylistShowcase(string title, IReadOnlyList<MobilePlaylist> playlists)
    {
        var section = new VerticalStackLayout { Spacing = 10 };
        section.Children.Add(PageHelpers.BuildSectionTitle(string.IsNullOrWhiteSpace(title) ? "Speellyste" : title));

        section.Children.Add(BuildHorizontalCarousel(
            playlists,
            186,
            playlist => BuildPlaylistCard(playlist)));

        return section;
    }

    private View BuildPlaylistCard(MobilePlaylist playlist)
    {
        var imageSource = _apiClient.BuildCachedImageSource(playlist.ArtworkUrl, "schink_background.jpeg");
        var card = new Border
        {
            WidthRequest = 246,
            BackgroundColor = Colors.Transparent,
            StrokeThickness = 0,
            Padding = 0,
            Content = new VerticalStackLayout
            {
                Spacing = 9,
                Children =
                {
                    new Border
                    {
                        StrokeThickness = 0,
                        StrokeShape = new RoundRectangle { CornerRadius = 16 },
                        HeightRequest = 138,
                        Content = new Grid
                        {
                            Children =
                            {
                                new Image
                                {
                                    Source = imageSource,
                                    HeightRequest = 138,
                                    Aspect = Aspect.AspectFill
                                },
                                BuildCoverPlayBadge("▦", 38, 19, 0)
                            }
                        },
                    },
                    new Label
                    {
                        Text = playlist.Title,
                        FontSize = 17,
                        TextColor = Color.FromArgb("#1B2231"),
                        MaxLines = 2,
                        LineBreakMode = LineBreakMode.TailTruncation,
                        LineHeight = 1.15
                    }
                }
            }
        };

        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) => await OpenPlaylistAsync(playlist);
        card.GestureRecognizers.Add(tap);
        return card;
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

        section.Children.Add(BuildHorizontalCarousel(
            playlist.Stories,
            286,
            story => BuildLuisterStoryCarouselCard(story)));

        return section;
    }

    private static CollectionView BuildHorizontalCarousel<T>(
        IReadOnlyList<T> items,
        double heightRequest,
        Func<T, View> buildItem)
    {
        var carousel = new CollectionView
        {
            ItemsSource = items,
            HeightRequest = heightRequest,
            ItemsLayout = new LinearItemsLayout(ItemsLayoutOrientation.Horizontal)
            {
                ItemSpacing = 14,
                SnapPointsType = SnapPointsType.None
            },
            HorizontalScrollBarVisibility = ScrollBarVisibility.Never,
            VerticalScrollBarVisibility = ScrollBarVisibility.Never,
            ItemSizingStrategy = ItemSizingStrategy.MeasureFirstItem,
            SelectionMode = SelectionMode.None,
            ItemTemplate = new DataTemplate(() =>
            {
                var host = new ContentView();
                host.BindingContextChanged += (_, _) =>
                {
                    if (host.BindingContext is T item)
                    {
                        host.Content = buildItem(item);
                    }
                };
                return host;
            })
        };

        return carousel;
    }

    private View BuildLuisterStoryCarouselCard(MobileStorySummary story)
    {
        var cover = new Border
        {
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 16 },
            HeightRequest = 218,
            Content = new Grid
            {
                HeightRequest = 218,
                Children =
                {
                    new Image
                    {
                        Source = _apiClient.BuildCachedImageSource(
                            PageHelpers.ResolveStoryCardImageSource(story, _apiClient)),
                        Aspect = Aspect.AspectFill,
                        HeightRequest = 218
                    },
                    BuildLockedBadge(story),
                    BuildFavoriteOverlay(story),
                    BuildCoverPlayBadge("▶", 38, 17, 2)
                }
            }
        };

        var card = new Border
        {
            WidthRequest = 168,
            BackgroundColor = Colors.Transparent,
            StrokeThickness = 0,
            Padding = 0,
            Margin = new Thickness(0, 0, 0, 10),
            Content = new VerticalStackLayout
            {
                Spacing = 9,
                Children =
                {
                    cover,
                    new Label
                    {
                        Text = story.Title,
                        FontSize = 16,
                        TextColor = Color.FromArgb("#1B2231"),
                        MaxLines = 2,
                        LineBreakMode = LineBreakMode.TailTruncation,
                        LineHeight = 1.16
                    }
                }
            }
        };

        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) => await OpenStoryAsync(story);
        card.GestureRecognizers.Add(tap);
        return card;
    }

    private static View BuildLockedBadge(MobileStorySummary story) =>
        new Border
        {
            IsVisible = story.IsLocked,
            BackgroundColor = Color.FromArgb("#D9222222"),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 999 },
            Padding = new Thickness(9, 4),
            Margin = new Thickness(10),
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Start,
            Content = new Label
            {
                Text = "Gesluit",
                TextColor = Colors.White,
                FontSize = 10,
                FontAttributes = FontAttributes.Bold
            }
        };

    private View BuildFavoriteOverlay(MobileStorySummary story)
    {
        var heart = new Label
        {
            Text = story.IsFavorite ? "♥" : "♡",
            TextColor = story.IsFavorite ? Color.FromArgb("#FFE6EF") : Colors.White,
            FontSize = 25,
            FontAttributes = FontAttributes.Bold,
            Shadow = new Shadow
            {
                Brush = Brush.Black,
                Offset = new Point(0, 2),
                Radius = 7,
                Opacity = 0.88f
            },
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center
        };

        var target = new Grid
        {
            WidthRequest = 44,
            HeightRequest = 44,
            Margin = new Thickness(0, 6, 6, 0),
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Start,
            Children = { heart }
        };

        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) => await ToggleFavoriteAsync(story);
        target.GestureRecognizers.Add(tap);
        return target;
    }

    private static View BuildCoverPlayBadge(string icon, double size, double fontSize, double leftOffset) =>
        new Grid
        {
            InputTransparent = true,
            Children =
            {
                new Border
                {
                    WidthRequest = size,
                    HeightRequest = size,
                    BackgroundColor = Color.FromArgb("#8AF3B23F"),
                    StrokeThickness = 0,
                    StrokeShape = new RoundRectangle { CornerRadius = 999 },
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.Center,
                    Content = new Label
                    {
                        Text = icon,
                        Opacity = 0.78,
                        TextColor = Color.FromArgb("#2A1C05"),
                        FontSize = fontSize,
                        FontAttributes = FontAttributes.Bold,
                        HorizontalTextAlignment = TextAlignment.Center,
                        VerticalTextAlignment = TextAlignment.Center,
                        Margin = new Thickness(leftOffset, 0, 0, 0)
                    }
                }
            }
        };

    private static IReadOnlyList<MobileLuisterSection> BuildLegacySections(IReadOnlyList<MobilePlaylist> playlists) =>
        playlists
            .Select((playlist, index) => new MobileLuisterSection(
                Kind: "playlist",
                Title: playlist.Title,
                SortOrder: index,
                Playlist: playlist,
                Playlists: Array.Empty<MobilePlaylist>()))
            .ToArray();

    private static IEnumerable<MobileLuisterSection> FilterSections(IReadOnlyList<MobileLuisterSection> sections, string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return sections.Where(SectionHasContent);
        }

        return sections
            .Select(section =>
            {
                if (IsSpeellysteSection(section))
                {
                    var filteredPlaylists = FilterPlaylists(section.Playlists, query).ToArray();
                    return section with { Playlists = filteredPlaylists };
                }

                if (section.Playlist is null)
                {
                    return section;
                }

                return FilterPlaylist(section.Playlist, query) is { } playlist
                    ? section with { Playlist = playlist }
                    : section with { Playlist = null };
            })
            .Where(SectionHasContent);
    }

    private static IEnumerable<MobilePlaylist> FilterPlaylists(IReadOnlyList<MobilePlaylist> playlists, string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return playlists;
        }

        var normalizedQuery = query.Trim();
        return playlists
            .Select(playlist => FilterPlaylist(playlist, normalizedQuery))
            .Where(playlist => playlist is not null)
            .Cast<MobilePlaylist>();
    }

    private static MobilePlaylist? FilterPlaylist(MobilePlaylist playlist, string query)
    {
        var playlistMatches = Contains(playlist.Title, query) || Contains(playlist.Description, query);
        var matchingStories = playlist.Stories
            .Where(story => Contains(story.Title, query))
            .ToArray();

        return playlistMatches || matchingStories.Length > 0
            ? playlist with { Stories = playlistMatches ? playlist.Stories : matchingStories }
            : null;
    }

    private static bool IsSpeellysteSection(MobileLuisterSection section) =>
        string.Equals(section.Kind, "speellyste", StringComparison.OrdinalIgnoreCase);

    private static bool SectionHasContent(MobileLuisterSection section) =>
        IsSpeellysteSection(section)
            ? section.Playlists.Count > 0
            : section.Playlist is not null;

    private static bool Contains(string? value, string query) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Contains(query, StringComparison.OrdinalIgnoreCase);

    private Task OpenStoryAsync(MobileStorySummary story)
    {
        _playlistPlaybackState.Clear();
        return Shell.Current.GoToAsync(
            $"{nameof(StoryDetailPage)}?slug={Uri.EscapeDataString(story.Slug)}&source=luister",
            animate: false,
            parameters: new Dictionary<string, object>
            {
                ["preview"] = story
            });
    }

    private void StartImageWarmup()
    {
        _imageWarmupCancellation?.Cancel();
        _imageWarmupCancellation?.Dispose();
        _imageWarmupCancellation = new CancellationTokenSource();
        var token = _imageWarmupCancellation.Token;
        var imageUrls = EnumerateLuisterImageUrls().ToArray();

        _ = Task.Run(async () =>
        {
            try
            {
                await _apiClient.CacheImagesAsync(imageUrls, token);
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
                // Image warmup is best-effort; the remote image source remains available.
            }
        }, token);
    }

    private IEnumerable<string?> EnumerateLuisterImageUrls()
    {
        foreach (var section in _sections)
        {
            if (IsSpeellysteSection(section))
            {
                foreach (var playlist in section.Playlists)
                {
                    yield return playlist.ArtworkUrl;
                    foreach (var story in playlist.Stories)
                    {
                        yield return PageHelpers.ResolveStoryCardImageSource(story, _apiClient);
                    }
                }

                continue;
            }

            if (section.Playlist is null)
            {
                continue;
            }

            yield return section.Playlist.ArtworkUrl;
            foreach (var story in section.Playlist.Stories)
            {
                yield return PageHelpers.ResolveStoryCardImageSource(story, _apiClient);
            }
        }
    }

    private Task OpenPlaylistAsync(MobilePlaylist playlist)
    {
        var firstStory = playlist.Stories.FirstOrDefault();
        return firstStory is null
            ? Task.CompletedTask
            : OpenPlaylistStoryAsync(firstStory, playlist);
    }

    private Task OpenPlaylistStoryAsync(MobileStorySummary story, MobilePlaylist playlist)
    {
        _playlistPlaybackState.Set(playlist);
        return Shell.Current.GoToAsync(
            $"{nameof(StoryDetailPage)}?slug={Uri.EscapeDataString(story.Slug)}&source=luister",
            animate: false,
            parameters: new Dictionary<string, object>
            {
                ["preview"] = story,
                ["playlistTitle"] = playlist.Title,
                ["playlistSlug"] = playlist.Slug
            });
    }

    private async Task ToggleFavoriteAsync(MobileStorySummary story)
    {
        if (!_sessionState.Current.IsSignedIn)
        {
            await DisplayAlertAsync("Teken in", "Teken eers in om gunstelinge te stoor.", "Reg so");
            return;
        }

        await _apiClient.SetFavoriteAsync(story.Slug, story.Source, !story.IsFavorite);
        await LoadAsync();
    }
}
