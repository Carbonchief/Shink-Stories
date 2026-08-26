using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using Shink.Mobile.Models;
using Shink.Mobile.Services;

namespace Shink.Mobile.Pages;

public sealed class SearchPage : ContentPage
{
    private const string PoppinsFontFamily = "Poppins";
    private const string PoppinsSemiBoldFontFamily = "PoppinsSemiBold";
    private const string PoppinsBoldFontFamily = "PoppinsBold";
    private const double BottomBarOverlayHeight = 152;
    private const int SearchDebounceMilliseconds = 220;
    private static bool IsAndroid => DeviceInfo.Current.Platform == DevicePlatform.Android;
    private static readonly Color SearchBackgroundColor = Color.FromArgb("#279AA1");
    private readonly MobileApiClient _apiClient;
    private readonly SessionState _sessionState;
    private readonly PlaylistPlaybackState _playlistPlaybackState;
    private readonly MobileAnalyticsService _analytics;
    private readonly NavigationGate _navigationGate = new();
    private readonly Image _backgroundImage;
    private readonly Entry _searchEntry;
    private readonly ActivityIndicator _searchActivity;
    private readonly Label _resultsSummary;
    private readonly CollectionView _resultsView;
    private readonly StorySearchResultCollection _visibleResults = new();
    private readonly RefreshView _refreshView;
    private readonly Border _topBarHost;
    private readonly VerticalStackLayout _searchHeader;
    private BoxView _heroSpacer = null!;
    private Image _heroMascot = null!;
    private Label _heroTitle = null!;
    private Label _heroSubtitle = null!;
    private CancellationTokenSource? _loadCancellation;
    private CancellationTokenSource? _searchCancellation;
    private IReadOnlyList<StorySearchCandidate> _catalog = Array.Empty<StorySearchCandidate>();
    private bool _isPageActive;
    private bool _hasLoadedCatalog;
    private bool _isLoadingCatalog;
    private bool _isCompactSearchHeader;
    private int _revealGeneration;

    public SearchPage(
        MobileApiClient apiClient,
        SessionState sessionState,
        PlaylistPlaybackState playlistPlaybackState,
        MobileAnalyticsService analytics)
    {
        _apiClient = apiClient;
        _sessionState = sessionState;
        _playlistPlaybackState = playlistPlaybackState;
        _analytics = analytics;

        Title = "Soek";
        SafeAreaEdges = SafeAreaEdges.None;
        Shell.SetNavBarIsVisible(this, false);
        BackgroundColor = SearchBackgroundColor;

        _backgroundImage = new Image
        {
            Source = "schink_background.jpeg",
            Aspect = Aspect.AspectFill,
            Opacity = 0.48,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Start,
            HeightRequest = 650,
            InputTransparent = true
        };

        _searchEntry = new Entry
        {
            Placeholder = "Soek stories",
            ClearButtonVisibility = ClearButtonVisibility.WhileEditing,
            Keyboard = Keyboard.Text,
            ReturnType = ReturnType.Search,
            FontFamily = PoppinsFontFamily,
            FontSize = 17,
            TextColor = Color.FromArgb("#171B1E"),
            PlaceholderColor = Color.FromArgb("#42484C"),
            BackgroundColor = Colors.Transparent,
            VerticalTextAlignment = TextAlignment.Center,
            AutomationId = "story-search-input"
        };
        _searchEntry.TextChanged += (_, _) => QueueSearch();
        _searchEntry.Focused += async (_, _) => await SetCompactSearchHeaderAsync(true);
        _searchEntry.Unfocused += async (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_searchEntry.Text))
            {
                await SetCompactSearchHeaderAsync(false);
            }
        };

        _searchActivity = new ActivityIndicator
        {
            IsRunning = false,
            IsVisible = false,
            Color = Color.FromArgb("#113D4D"),
            WidthRequest = 24,
            HeightRequest = 24,
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Center,
            InputTransparent = true
        };

        _resultsSummary = new Label
        {
            IsVisible = false,
            FontFamily = PoppinsSemiBoldFontFamily,
            FontSize = 15,
            TextColor = Colors.White,
            HorizontalTextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 18, 0, 2),
            AutomationId = "story-search-status"
        };

        _searchHeader = BuildSearchHeader();
        _resultsView = new CollectionView
        {
            Background = Brush.Transparent,
            ItemsSource = _visibleResults,
            ItemSizingStrategy = ItemSizingStrategy.MeasureFirstItem,
            SelectionMode = SelectionMode.None,
            ItemsLayout = new LinearItemsLayout(ItemsLayoutOrientation.Vertical)
            {
                ItemSpacing = 14
            },
            ItemTemplate = new DataTemplate(BuildResultContainer),
            Footer = new BoxView
            {
                HeightRequest = 170,
                Color = Colors.Transparent
            },
            HorizontalScrollBarVisibility = ScrollBarVisibility.Never,
            VerticalScrollBarVisibility = ScrollBarVisibility.Never
        };

        _refreshView = new RefreshView
        {
            SafeAreaEdges = SafeAreaEdges.None,
            Background = Brush.Transparent,
            Content = _resultsView,
            Command = new Command(() => _ = LoadCatalogAsync(forceRefresh: true))
        };

        var searchContent = new Grid
        {
            RowSpacing = 0,
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Star }
            },
            Children = { _searchHeader, _refreshView }
        };
        Grid.SetRow(_refreshView, 1);

        _topBarHost = new Border
        {
            BackgroundColor = Colors.Transparent,
            StrokeThickness = 0,
            Padding = 0,
            Margin = Thickness.Zero,
            HeightRequest = 62,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Start,
            ZIndex = 101,
            Content = BuildTopBar()
        };

        var topBarOverlay = new Grid
        {
            SafeAreaEdges = new SafeAreaEdges(
                SafeAreaRegions.Container,
                SafeAreaRegions.Container,
                SafeAreaRegions.Container,
                SafeAreaRegions.None),
            BackgroundColor = Colors.Transparent,
            Padding = new Thickness(0, 0, 0, 16),
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Start,
            InputTransparent = false,
            ZIndex = 100,
            Children = { _topBarHost }
        };

        var bottomBarOverlay = new Grid
        {
            SafeAreaEdges = SafeAreaEdges.None,
            BackgroundColor = Colors.Transparent,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.End,
            HeightRequest = BottomBarOverlayHeight,
            InputTransparent = false,
            ZIndex = 100,
            Children =
            {
                new ContentView
                {
                    Content = MobileBottomBar.Build(this, "search", FocusSearchAsync),
                    HorizontalOptions = LayoutOptions.Fill,
                    VerticalOptions = LayoutOptions.End,
                    HeightRequest = BottomBarOverlayHeight
                }
            }
        };

        Content = new Grid
        {
            SafeAreaEdges = SafeAreaEdges.None,
            BackgroundColor = SearchBackgroundColor,
            Children =
            {
                _backgroundImage,
                new BoxView
                {
                    Color = SearchBackgroundColor,
                    Opacity = 0.34,
                    InputTransparent = true
                },
                searchContent,
                topBarOverlay,
                bottomBarOverlay
            }
        };

        SizeChanged += (_, _) => ApplyResponsiveLayout();
        ApplyResponsiveLayout();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _isPageActive = true;
        _topBarHost.Content = BuildTopBar();
        if (!_hasLoadedCatalog && !_isLoadingCatalog)
        {
            _ = LoadCatalogAsync(forceRefresh: false);
        }
    }

    protected override void OnDisappearing()
    {
        _isPageActive = false;
        _searchCancellation?.Cancel();
        _loadCancellation?.Cancel();
        base.OnDisappearing();
    }

    private VerticalStackLayout BuildSearchHeader()
    {
        _heroSpacer = new BoxView { HeightRequest = 200, Color = Colors.Transparent };
        _heroMascot = new Image
        {
            Source = "knibbels_search.png",
            Aspect = Aspect.AspectFit,
            WidthRequest = 140,
            HeightRequest = 158,
            HorizontalOptions = LayoutOptions.Center,
            InputTransparent = true,
            AutomationId = "story-search-mascot"
        };
        _heroTitle = new Label
        {
            Text = "Storie soek...",
            FontFamily = PoppinsBoldFontFamily,
            FontSize = 37,
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.White,
            HorizontalTextAlignment = TextAlignment.Center,
            LineHeight = 1.04,
            Margin = new Thickness(0, 2, 0, 0),
            AutomationId = "story-search-title"
        };
        _heroSubtitle = new Label
        {
            Text = "Tik die naam van die storie wat jy wil luister",
            FontFamily = PoppinsFontFamily,
            FontSize = 16,
            TextColor = Colors.White,
            HorizontalTextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 3, 0, 0)
        };

        return new VerticalStackLayout
        {
            MaximumWidthRequest = 720,
            HorizontalOptions = LayoutOptions.Center,
            Padding = new Thickness(22, 0),
            Spacing = 0,
            Children =
            {
                _heroSpacer,
                _heroMascot,
                _heroTitle,
                _heroSubtitle,
                BuildSearchField(),
                _resultsSummary,
                new BoxView { HeightRequest = 32, Color = Colors.Transparent }
            }
        };
    }

    private Border BuildSearchField()
    {
        var searchIcon = new GraphicsView
        {
            Drawable = new MobileAndroidIconDrawable(MobileAndroidIcon.Search, Color.FromArgb("#171B1E")),
            WidthRequest = 29,
            HeightRequest = 29,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            InputTransparent = true
        };

        var grid = new Grid
        {
            ColumnSpacing = 0,
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = 48 },
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = 36 }
            },
            Children = { searchIcon, _searchEntry, _searchActivity }
        };
        Grid.SetColumn(_searchEntry, 1);
        Grid.SetColumn(_searchActivity, 2);

        var field = new Border
        {
            BackgroundColor = Color.FromArgb("#FBFCFD"),
            Stroke = Color.FromArgb("#2E2C2D"),
            StrokeThickness = 2.5,
            StrokeShape = new RoundRectangle { CornerRadius = 16 },
            HeightRequest = 50,
            Padding = new Thickness(4, 0, 8, 0),
            Margin = new Thickness(6, 20, 6, 0),
            Content = grid,
            Shadow = new Shadow
            {
                Brush = Brush.Black,
                Offset = new Point(0, 2),
                Radius = 4,
                Opacity = 0.16f
            },
            AutomationId = "story-search-field"
        };

        var focusTap = new TapGestureRecognizer();
        focusTap.Tapped += (_, _) => _searchEntry.Focus();
        field.GestureRecognizers.Add(focusTap);
        return field;
    }

    private View BuildTopBar() =>
        MobileTopBar.BuildStoriesTopBar(
            this,
            _apiClient,
            _sessionState.Current,
            notificationAction: OpenNotificationsAsync);

    private Task FocusSearchAsync()
    {
        _searchEntry.Focus();
        return Task.CompletedTask;
    }

    private async Task SetCompactSearchHeaderAsync(bool compact)
    {
        if (_isCompactSearchHeader == compact)
        {
            return;
        }

        _isCompactSearchHeader = compact;
        var heroViews = new View[] { _heroMascot, _heroTitle, _heroSubtitle };
        foreach (var view in heroViews)
        {
            view.CancelAnimations();
        }

        if (compact)
        {
            await Task.WhenAll(heroViews.Select(view => view.FadeToAsync(0, 130, Easing.CubicOut)));
            if (!_isCompactSearchHeader)
            {
                return;
            }

            foreach (var view in heroViews)
            {
                view.IsVisible = false;
            }

            _heroSpacer.HeightRequest = 98;
            return;
        }

        _heroSpacer.HeightRequest = 200;
        foreach (var view in heroViews)
        {
            view.Opacity = 0;
            view.IsVisible = true;
        }

        await Task.WhenAll(heroViews.Select(view => view.FadeToAsync(1, 210, Easing.CubicOut)));
    }

    private static Task OpenNotificationsAsync() =>
        Shell.Current.GoToAsync("//Luister?surface=notifications", animate: false);

    private async Task LoadCatalogAsync(bool forceRefresh)
    {
        if (_isLoadingCatalog)
        {
            return;
        }

        _isLoadingCatalog = true;
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = new CancellationTokenSource();
        var cancellationToken = _loadCancellation.Token;
        UpdateSearchActivity();

        try
        {
            if (!forceRefresh && !_hasLoadedCatalog)
            {
                var cachedResponse = await _apiClient.GetCachedLuisterAsync(cancellationToken);
                if (cachedResponse is not null && !cancellationToken.IsCancellationRequested)
                {
                    _catalog = BuildSearchCatalog(cachedResponse);
                    _hasLoadedCatalog = true;
                    await MainThread.InvokeOnMainThreadAsync(RenderSearchResults);
                }
            }

            var response = await _apiClient.GetLuisterAsync(cancellationToken);
            if (response is null || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            _catalog = BuildSearchCatalog(response);
            _hasLoadedCatalog = true;
            await MainThread.InvokeOnMainThreadAsync(RenderSearchResults);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _analytics.TrackException(ex, "mobile_story_search_load");
            if (!_hasLoadedCatalog)
            {
                await MainThread.InvokeOnMainThreadAsync(RenderSearchResults);
            }
        }
        finally
        {
            _isLoadingCatalog = false;
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                _refreshView.IsRefreshing = false;
                UpdateSearchActivity();
            });
        }
    }

    private void QueueSearch()
    {
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        _searchCancellation = new CancellationTokenSource();
        var cancellationToken = _searchCancellation.Token;
        UpdateSearchActivity();
        _ = DebounceSearchAsync(cancellationToken);
    }

    private async Task DebounceSearchAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(SearchDebounceMilliseconds, cancellationToken);
            if (cancellationToken.IsCancellationRequested || !_isPageActive)
            {
                return;
            }

            await MainThread.InvokeOnMainThreadAsync(RenderSearchResults);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                if (_searchCancellation?.Token == cancellationToken)
                {
                    _searchCancellation.Dispose();
                    _searchCancellation = null;
                }

                await MainThread.InvokeOnMainThreadAsync(UpdateSearchActivity);
            }
        }
    }

    private void RenderSearchResults()
    {
        var query = _searchEntry.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(query))
        {
            _visibleResults.ReplaceWith(Array.Empty<StorySearchResult>());
            _resultsSummary.IsVisible = false;
            return;
        }

        if (!_hasLoadedCatalog)
        {
            _visibleResults.ReplaceWith(Array.Empty<StorySearchResult>());
            _resultsSummary.Text = _isLoadingCatalog
                ? "Stories word gelaai..."
                : "Kon nie stories laai nie. Trek af om weer te probeer.";
            _resultsSummary.IsVisible = true;
            return;
        }

        var generation = ++_revealGeneration;
        var matches = SearchCandidates(_catalog, query)
            .Select((candidate, index) => new StorySearchResult(candidate, index, generation))
            .ToArray();
        // Only the result list is refreshed. The live search Entry is a stable sibling
        // above the CollectionView so iOS never removes it or dismisses its keyboard.
        _visibleResults.ReplaceWith(matches);
        _resultsSummary.Text = matches.Length switch
        {
            0 => $"Geen stories pas by \"{query}\" nie.",
            1 => $"1 storie pas by \"{query}\".",
            _ => $"{matches.Length} stories pas by \"{query}\"."
        };
        _resultsSummary.IsVisible = true;
        _analytics.TrackEvent("mobile_story_search", new Dictionary<string, object>
        {
            ["query_length"] = query.Length,
            ["result_count"] = matches.Length
        });
    }

    private void UpdateSearchActivity()
    {
        var isWaitingForDebounce = _searchCancellation is { IsCancellationRequested: false };
        var shouldShow = !string.IsNullOrWhiteSpace(_searchEntry.Text) &&
            (_isLoadingCatalog || isWaitingForDebounce);
        _searchActivity.IsVisible = shouldShow;
        _searchActivity.IsRunning = shouldShow;
    }

    private View BuildResultContainer()
    {
        var container = new ContentView
        {
            Padding = new Thickness(22, 0),
            MaximumWidthRequest = 760,
            HorizontalOptions = LayoutOptions.Fill
        };
        container.BindingContextChanged += (_, _) => BindResultContainer(container);
        return container;
    }

    private void BindResultContainer(ContentView container)
    {
        container.CancelAnimations();
        if (container.BindingContext is not StorySearchResult result)
        {
            container.Content = null;
            return;
        }

        container.Content = BuildResultCard(result.Candidate);
        if (IsAndroid)
        {
            container.Opacity = 1;
            container.TranslationY = 0;
            container.Scale = 1;
            return;
        }

        container.Opacity = 0;
        container.TranslationY = 24;
        container.Scale = 0.975;
        _ = AnimateResultContainerAsync(container, result);
    }

    private static async Task AnimateResultContainerAsync(
        ContentView container,
        StorySearchResult result)
    {
        try
        {
            await Task.Delay(Math.Min(result.RevealIndex, 8) * 55);
            if (container.BindingContext is not StorySearchResult current ||
                current.RevealGeneration != result.RevealGeneration)
            {
                return;
            }

            await Task.WhenAll(
                container.FadeToAsync(1, 260, Easing.CubicOut),
                container.TranslateToAsync(0, 0, 320, Easing.CubicOut),
                container.ScaleToAsync(1, 330, Easing.CubicOut));
        }
        catch (OperationCanceledException)
        {
        }
    }

    private View BuildResultCard(StorySearchCandidate candidate)
    {
        var story = candidate.Story;
        var artwork = new Border
        {
            WidthRequest = 94,
            HeightRequest = 124,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 14 },
            Content = new Image
            {
                Source = _apiClient.BuildCachedImageSource(
                    string.IsNullOrWhiteSpace(story.ThumbnailUrl) ? story.ImageUrl : story.ThumbnailUrl),
                Aspect = Aspect.AspectFill,
                WidthRequest = 94,
                HeightRequest = 124
            }
        };

        var actionLabel = story.IsLocked ? "Sien opsies" : "Luister";
        var details = new VerticalStackLayout
        {
            Spacing = 4,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Label
                {
                    Text = candidate.Kind,
                    FontFamily = PoppinsBoldFontFamily,
                    FontSize = 10,
                    CharacterSpacing = 1.1,
                    TextTransform = TextTransform.Uppercase,
                    TextColor = Color.FromArgb("#687472")
                },
                new Label
                {
                    Text = story.Title,
                    FontFamily = PoppinsBoldFontFamily,
                    FontSize = 18,
                    TextColor = Color.FromArgb("#113D4D"),
                    MaxLines = 2,
                    LineBreakMode = LineBreakMode.TailTruncation,
                    LineHeight = 1.08
                },
                new Label
                {
                    Text = story.Description,
                    FontFamily = PoppinsFontFamily,
                    FontSize = 12,
                    TextColor = Color.FromArgb("#394847"),
                    MaxLines = 2,
                    LineBreakMode = LineBreakMode.TailTruncation,
                    LineHeight = 1.08
                },
                new Border
                {
                    BackgroundColor = Color.FromArgb("#F39A32"),
                    StrokeThickness = 0,
                    StrokeShape = new RoundRectangle { CornerRadius = 999 },
                    Padding = new Thickness(12, 5),
                    Margin = new Thickness(0, 3, 0, 0),
                    HorizontalOptions = LayoutOptions.Start,
                    Content = new Label
                    {
                        Text = actionLabel,
                        FontFamily = PoppinsBoldFontFamily,
                        FontSize = 11,
                        TextColor = Color.FromArgb("#1E1E1E"),
                        InputTransparent = true
                    }
                }
            }
        };

        var content = new Grid
        {
            ColumnSpacing = 14,
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Star }
            },
            Children = { artwork, details }
        };
        Grid.SetColumn(details, 1);

        var card = new Border
        {
            BackgroundColor = Color.FromArgb("#FFFDF8"),
            Stroke = Color.FromArgb("#D5DCD7"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 20 },
            Padding = 12,
            MaximumWidthRequest = 716,
            HorizontalOptions = LayoutOptions.Center,
            Content = content,
            Shadow = IsAndroid
                ? null!
                : new Shadow
                {
                    Brush = Brush.Black,
                    Offset = new Point(0, 7),
                    Radius = 18,
                    Opacity = 0.13f
                },
            AutomationId = $"story-search-result-{story.Slug}"
        };
        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) => await OpenStoryAsync(candidate);
        card.GestureRecognizers.Add(tap);
        return card;
    }

    private async Task OpenStoryAsync(StorySearchCandidate candidate)
    {
        await _navigationGate.RunAsync(async () =>
        {
            SafeHapticFeedback.TryPerform(HapticFeedbackType.Click);
            var story = candidate.Story;
            if (story.IsLocked)
            {
                var source = ResolveStorySource(story);
                var returnUrl = $"/{source}/{Uri.EscapeDataString(story.Slug)}";
                await Shell.Current.GoToAsync(
                    $"{nameof(PlansPage)}?returnUrl={Uri.EscapeDataString(returnUrl)}",
                    animate: true);
                return;
            }

            if (candidate.Playlist is not null)
            {
                _playlistPlaybackState.Set(candidate.Playlist, story);
            }
            else
            {
                _playlistPlaybackState.Clear();
            }

            var routeSource = ResolveStorySource(story);
            var parameters = new Dictionary<string, object>
            {
                ["preview"] = story
            };
            if (candidate.Playlist is not null)
            {
                parameters["playlistTitle"] = candidate.Playlist.Title;
                parameters["playlistSlug"] = candidate.Playlist.Slug;
            }

            await Shell.Current.GoToAsync(
                $"{nameof(StoryDetailPage)}?slug={Uri.EscapeDataString(story.Slug)}&source={Uri.EscapeDataString(routeSource)}",
                animate: false,
                parameters: parameters);
        });
    }

    private static string ResolveStorySource(MobileStorySummary story) =>
        string.Equals(story.Source, "gratis", StringComparison.OrdinalIgnoreCase)
            ? "gratis"
            : "luister";

    private static IReadOnlyList<StorySearchCandidate> BuildSearchCatalog(MobileLuisterResponse response)
    {
        var playlists = EnumeratePlaylists(response).ToArray();
        var candidates = new Dictionary<string, StorySearchCandidate>(StringComparer.OrdinalIgnoreCase);

        foreach (var playlist in playlists)
        {
            foreach (var story in playlist.Stories.Concat(
                         playlist.ShowcaseStory is null
                             ? Array.Empty<MobileStorySummary>()
                             : new[] { playlist.ShowcaseStory }))
            {
                var key = $"{ResolveStorySource(story)}:{story.Slug}";
                var playlistKeywords = $"{playlist.Title} {playlist.Description}".Trim();
                if (candidates.TryGetValue(key, out var existing))
                {
                    candidates[key] = existing with
                    {
                        Keywords = $"{existing.Keywords} {playlistKeywords}".Trim()
                    };
                    continue;
                }

                candidates[key] = new StorySearchCandidate(
                    Story: story,
                    Playlist: playlist,
                    Kind: string.Equals(ResolveStorySource(story), "gratis", StringComparison.Ordinal)
                        ? "Gratis storie"
                        : "Alle stories",
                    Keywords: $"{playlistKeywords} luister stories kinders afrikaans oudiostorie {story.Source}".Trim(),
                    Score: 0);
            }
        }

        return candidates.Values.ToArray();
    }

    private static IEnumerable<MobilePlaylist> EnumeratePlaylists(MobileLuisterResponse response)
    {
        if (response.Sections is { Count: > 0 })
        {
            foreach (var section in response.Sections)
            {
                if (section.Playlist is not null)
                {
                    yield return section.Playlist;
                }

                foreach (var playlist in section.Playlists)
                {
                    yield return playlist;
                }
            }

            yield break;
        }

        foreach (var playlist in response.Playlists)
        {
            yield return playlist;
        }
    }

    private static IReadOnlyList<StorySearchCandidate> SearchCandidates(
        IReadOnlyList<StorySearchCandidate> candidates,
        string query)
    {
        var normalizedQuery = NormalizeForSearch(query);
        var queryTerms = normalizedQuery
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (queryTerms.Length == 0)
        {
            return Array.Empty<StorySearchCandidate>();
        }

        var results = new List<StorySearchCandidate>();
        foreach (var candidate in candidates)
        {
            var normalizedTitle = NormalizeForSearch(candidate.Story.Title);
            var normalizedBody = NormalizeForSearch(
                $"{candidate.Story.Description} {candidate.Keywords}");
            var normalizedContent = $"{normalizedTitle} {normalizedBody}";
            if (!queryTerms.All(term => normalizedContent.Contains(term, StringComparison.Ordinal)))
            {
                continue;
            }

            var score = 0;
            if (normalizedTitle.Contains(normalizedQuery, StringComparison.Ordinal))
            {
                score += 140;
            }

            if (normalizedBody.Contains(normalizedQuery, StringComparison.Ordinal))
            {
                score += 70;
            }

            foreach (var term in queryTerms)
            {
                if (normalizedTitle.Contains(term, StringComparison.Ordinal))
                {
                    score += 24;
                }

                if (normalizedBody.Contains(term, StringComparison.Ordinal))
                {
                    score += 10;
                }
            }

            results.Add(candidate with { Score = score });
        }

        return results
            .OrderByDescending(result => result.Score)
            .ThenBy(result => result.Story.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string NormalizeForSearch(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private void ApplyResponsiveLayout()
    {
        var width = MobileResponsiveLayout.ResolveWidth(Width);
        _backgroundImage.HeightRequest = Math.Clamp(Height * 0.66, 560, 710);
        if (DeviceInfo.Current.Platform == DevicePlatform.Android && DeviceInfo.Idiom == DeviceIdiom.Phone)
        {
            var phoneChromeWidth = Math.Max(280, width - 36);
            _topBarHost.WidthRequest = phoneChromeWidth;
            _topBarHost.MaximumWidthRequest = phoneChromeWidth;
            _topBarHost.HorizontalOptions = LayoutOptions.Center;
            return;
        }

        MobileResponsiveLayout.ApplyStoriesTopBar(_topBarHost, width, 1040);
    }

    private sealed record StorySearchCandidate(
        MobileStorySummary Story,
        MobilePlaylist? Playlist,
        string Kind,
        string Keywords,
        int Score);

    private sealed record StorySearchResult(
        StorySearchCandidate Candidate,
        int RevealIndex,
        int RevealGeneration);

    private sealed class StorySearchResultCollection : ObservableCollection<StorySearchResult>
    {
        public void ReplaceWith(IEnumerable<StorySearchResult> results)
        {
            Items.Clear();
            foreach (var result in results)
            {
                Items.Add(result);
            }

            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
            OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }
    }
}
