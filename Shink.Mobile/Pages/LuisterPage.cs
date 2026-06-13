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
            await _apiClient.GetSessionAsync();
            RenderContent();
        }
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

    private void RenderContent()
    {
        if (!_hasLoaded)
        {
            return;
        }

        _content.Children.Clear();
        _content.Children.Add(MobileTopBar.Build(this, _apiClient, _sessionState.Current));
        _content.Children.Add(BuildHero());
        _content.Children.Add(BuildSearchBox());
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

    private View BuildHero()
    {
        return new Border
        {
            BackgroundColor = Color.FromArgb("#EAF7F4"),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 28 },
            Padding = 0,
            Shadow = new Shadow
            {
                Brush = Brush.Black,
                Offset = new Point(0, 8),
                Radius = 18,
                Opacity = 0.08f
            },
            Content = new Image
            {
                Source = "dis_storietyd.png",
                HeightRequest = 230,
                Aspect = Aspect.AspectFit
            }
        };
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

        var row = new HorizontalStackLayout { Spacing = 14 };
        foreach (var playlist in playlists)
        {
            row.Children.Add(BuildPlaylistCard(playlist));
        }

        section.Children.Add(new ScrollView
        {
            Orientation = ScrollOrientation.Horizontal,
            Content = row
        });

        return section;
    }

    private View BuildPlaylistCard(MobilePlaylist playlist)
    {
        var resolvedImageUrl = string.IsNullOrWhiteSpace(playlist.ArtworkUrl)
            ? "schink_background.jpeg"
            : _apiClient.BuildImageUrl(playlist.ArtworkUrl);
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
                        Shadow = new Shadow
                        {
                            Brush = Brush.Black,
                            Offset = new Point(0, 8),
                            Radius = 18,
                            Opacity = 0.22f
                        },
                        Content = new Grid
                        {
                            Children =
                            {
                                new Image
                                {
                                    Source = resolvedImageUrl,
                                    HeightRequest = 138,
                                    Aspect = Aspect.AspectFill
                                },
                                BuildCoverPlayBadge("▦")
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

        var row = new HorizontalStackLayout { Spacing = 14 };
        foreach (var story in playlist.Stories)
        {
            row.Children.Add(BuildLuisterStoryCarouselCard(story));
        }

        section.Children.Add(new ScrollView
        {
            Orientation = ScrollOrientation.Horizontal,
            Content = row
        });

        return section;
    }

    private View BuildLuisterStoryCarouselCard(MobileStorySummary story)
    {
        var cover = new Border
        {
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 16 },
            HeightRequest = 218,
            Shadow = new Shadow
            {
                Brush = Brush.Black,
                Offset = new Point(0, 9),
                Radius = 22,
                Opacity = 0.26f
            },
            Content = new Grid
            {
                HeightRequest = 218,
                Children =
                {
                    new Image
                    {
                        Source = PageHelpers.ResolveStoryCardImageSource(story, _apiClient),
                        Aspect = Aspect.AspectFill,
                        HeightRequest = 218
                    },
                    BuildLockedBadge(story),
                    BuildFavoriteOverlay(story),
                    BuildCoverPlayBadge("▶")
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

    private static View BuildCoverPlayBadge(string icon) =>
        new Grid
        {
            BackgroundColor = Color.FromArgb("#22000000"),
            InputTransparent = true,
            Children =
            {
                new Border
                {
                    WidthRequest = 54,
                    HeightRequest = 54,
                    BackgroundColor = Color.FromArgb("#EEF3B23F"),
                    StrokeThickness = 0,
                    StrokeShape = new RoundRectangle { CornerRadius = 999 },
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.Center,
                    Shadow = new Shadow
                    {
                        Brush = Brush.Black,
                        Offset = new Point(0, 6),
                        Radius = 18,
                        Opacity = 0.28f
                    },
                    Content = new Label
                    {
                        Text = icon,
                        TextColor = Color.FromArgb("#1A1A1A"),
                        FontSize = icon == "▶" ? 22 : 24,
                        FontAttributes = FontAttributes.Bold,
                        HorizontalTextAlignment = TextAlignment.Center,
                        VerticalTextAlignment = TextAlignment.Center,
                        Margin = icon == "▶" ? new Thickness(3, 0, 0, 0) : Thickness.Zero
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
