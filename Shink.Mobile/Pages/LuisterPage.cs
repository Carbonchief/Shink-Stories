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
    private IReadOnlyList<MobilePlaylist> _playlists = Array.Empty<MobilePlaylist>();
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
            await _apiClient.GetSessionAsync();
            var response = await _apiClient.GetLuisterAsync();
            if (response is null)
            {
                _content.Children.Clear();
                _content.Children.Add(new Label { Text = "Kon nie luister stories laai nie." });
                return;
            }

            _playlists = response.Playlists;
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
        var filteredPlaylists = FilterPlaylists(_playlists, _searchEntry.Text).ToArray();
        if (filteredPlaylists.Length == 0)
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

        _playlistContent.Children.Add(BuildPlaylistShowcase(filteredPlaylists));

        foreach (var playlist in filteredPlaylists)
        {
            _playlistContent.Children.Add(BuildPlaylistSection(playlist));
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

    private View BuildPlaylistShowcase(IReadOnlyList<MobilePlaylist> playlists)
    {
        var section = new VerticalStackLayout { Spacing = 10 };
        section.Children.Add(PageHelpers.BuildSectionTitle("Speellyste"));

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
            WidthRequest = 224,
            BackgroundColor = Color.FromArgb("#FFFDF7"),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 26 },
            Padding = 10,
            Shadow = new Shadow
            {
                Brush = Brush.Black,
                Offset = new Point(0, 8),
                Radius = 16,
                Opacity = 0.08f
            },
            Content = new VerticalStackLayout
            {
                Spacing = 8,
                Children =
                {
                    new Border
                    {
                        StrokeThickness = 0,
                        StrokeShape = new RoundRectangle { CornerRadius = 20 },
                        HeightRequest = 126,
                        Content = new Image
                        {
                            Source = resolvedImageUrl,
                            HeightRequest = 126,
                            Aspect = Aspect.AspectFill
                        }
                    },
                    new Label
                    {
                        Text = playlist.Title,
                        FontAttributes = FontAttributes.Bold,
                        FontSize = 16,
                        TextColor = Color.FromArgb("#222222"),
                        MaxLines = 2
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
            var card = PageHelpers.BuildStoryCard(story, _apiClient, OpenStoryAsync, ToggleFavoriteAsync);
            card.WidthRequest = 226;
            row.Children.Add(card);
        }

        section.Children.Add(new ScrollView
        {
            Orientation = ScrollOrientation.Horizontal,
            Content = row
        });

        return section;
    }

    private static IEnumerable<MobilePlaylist> FilterPlaylists(IReadOnlyList<MobilePlaylist> playlists, string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return playlists;
        }

        var normalizedQuery = query.Trim();
        return playlists
            .Select(playlist => playlist with
            {
                Stories = playlist.Stories
                    .Where(story =>
                        Contains(story.Title, normalizedQuery) ||
                        Contains(story.Description, normalizedQuery))
                    .ToArray()
            })
            .Where(playlist =>
                Contains(playlist.Title, normalizedQuery) ||
                Contains(playlist.Description, normalizedQuery) ||
                playlist.Stories.Count > 0);
    }

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
