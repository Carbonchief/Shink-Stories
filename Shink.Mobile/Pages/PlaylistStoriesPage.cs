using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Shink.Mobile.Models;
using Shink.Mobile.Services;

namespace Shink.Mobile.Pages;

public sealed class PlaylistStoriesPage : ContentPage, IQueryAttributable
{
    private const string BackIconGlyph = "\uf060";
    private const string PlaylistPlayIconGlyph = "\uf144";
    private const string DownIconGlyph = "\uf107";
    private const string PlayIconGlyph = "\uf04b";
    private const string LockIconGlyph = "\uf023";
    private const string HeartIconGlyph = "\uf004";
    private static bool IsAndroid => DeviceInfo.Current.Platform == DevicePlatform.Android;
    private static readonly Color DefaultPageColor = Color.FromArgb("#FFFFFF");
    private static readonly Color DefaultTextColor = Color.FromArgb("#33424B");
    private static readonly Color AccentColor = Color.FromArgb("#F5AA4F");
    private static readonly IReadOnlyDictionary<string, LegacyShowcaseAppearance> LegacyShowcaseAppearances =
        new Dictionary<string, LegacyShowcaseAppearance>(StringComparer.OrdinalIgnoreCase)
        {
            ["storie-hoekie"] = new(
                "https://www.schink.co.za/media/image?src=https%3A%2F%2Fmedia.prioritybit.co.za%2Fuploaded%2Fstories%2Fimages%2F2026%2F04%2Fstorie-hoekie-cover-20260422132833-8013b513811541a19f4465ae2af39198.png",
                "https://www.schink.co.za/media/image?src=https%3A%2F%2Fmedia.prioritybit.co.za%2Fuploaded%2Fstories%2Fimages%2F2026%2F04%2Fstorie-hoekie-thumbnail-20260422132701-aaa3275fa3a6477abad10838e9a37294.png",
                "#F5F9DC", "#F5F9DC", "#33424B"),
            ["bybelstories"] = new(
                "https://www.schink.co.za/branding/uploaded/playlists/2026/04/bybelstories-backdrop-20260405121904-fc3c2a39dc884cf4a9e039a1f4acc9ac.png",
                "https://www.schink.co.za/media/image?src=https%3A%2F%2Fmedia.prioritybit.co.za%2Fuploaded%2Fstories%2Fimages%2F2026%2F04%2Fbybelstories-thumbnail-20260415125842-de99946c1c264054a4b6640b995cb1e0.png",
                "#FFFFFF", "#FFFFFF", "#33424B"),
            ["woordjieland"] = new(
                "https://www.schink.co.za/media/image?src=https%3A%2F%2Fmedia.prioritybit.co.za%2Fuploaded%2Fstories%2Fimages%2F2026%2F04%2Fdankie-en-die-mislukke-skree-cover-20260415150055-c503ad918d3d434aa7d1322b42fa1a7b.jpg",
                null,
                "#FFFFFF", "#FFFFFF", "#33424B"),
            ["vrugte-eiland"] = new(
                "https://www.schink.co.za/media/image?src=https%3A%2F%2Fmedia.prioritybit.co.za%2Fuploaded%2Fstories%2Fimages%2F2026%2F03%2Fsuurlemoentjie-cover-20260331120905-b2800d79a79f4653ba68703964dd007c.jpg",
                null,
                "#FFFFFF", "#FFFFFF", "#33424B"),
            ["die-alledaagse-held"] = new(
                "https://www.schink.co.za/media/image?src=https%3A%2F%2Fmedia.prioritybit.co.za%2Fuploaded%2Fstories%2Fimages%2F2026%2F04%2Fdie-alledaagse-held-cover-20260422145207-23d7c3d29ebf4160b3eae82d224cee9b.png",
                "https://www.schink.co.za/media/image?src=https%3A%2F%2Fmedia.prioritybit.co.za%2Fuploaded%2Fstories%2Fimages%2F2026%2F04%2Fdie-alledaagse-held-thumbnail-20260422145205-9037a0a2436248e2805ab85e57e04ac6.png",
                "#FFFFFF", "#FFFFFF", "#33424B")
        };

    private readonly MobileApiClient _apiClient;
    private readonly SessionState _sessionState;
    private readonly PlaylistPlaybackState _playlistPlaybackState;
    private readonly PlayerTransitionBackdropState _transitionBackdropState;
    private readonly ObservableCollection<StoryCardItem> _stories = [];
    private readonly CollectionView _storiesView;
    private MobilePlaylist? _playlist;
    private Border? _hero;
    private Color _pageTextColor = DefaultTextColor;
    private double _lastPageHeight;

    public PlaylistStoriesPage(
        MobileApiClient apiClient,
        SessionState sessionState,
        PlaylistPlaybackState playlistPlaybackState,
        StoryPlaybackSession storyPlaybackSession,
        PlayerTransitionBackdropState transitionBackdropState)
    {
        _apiClient = apiClient;
        _sessionState = sessionState;
        _playlistPlaybackState = playlistPlaybackState;
        _transitionBackdropState = transitionBackdropState;

        Title = "Speellys stories";
        SafeAreaEdges = SafeAreaEdges.None;
        BackgroundColor = DefaultPageColor;
        Shell.SetNavBarIsVisible(this, false);
        Shell.SetTabBarIsVisible(this, false);

        _storiesView = new CollectionView
        {
            BackgroundColor = Colors.Transparent,
            ItemsSource = _stories,
            SelectionMode = SelectionMode.None,
            ItemSizingStrategy = ItemSizingStrategy.MeasureFirstItem,
            ItemsLayout = new GridItemsLayout(2, ItemsLayoutOrientation.Vertical)
            {
                HorizontalItemSpacing = 12,
                VerticalItemSpacing = 16
            },
            ItemTemplate = new DataTemplate(BuildStoryCard),
            Margin = 0,
            Footer = new BoxView { HeightRequest = 26, Color = Colors.Transparent },
            EmptyView = new Label
            {
                Text = "Geen stories is tans beskikbaar nie.",
                TextColor = DefaultTextColor,
                FontSize = 16,
                Margin = new Thickness(20),
                HorizontalTextAlignment = TextAlignment.Center
            }
        };

        var pageContent = new Grid
        {
            SafeAreaEdges = SafeAreaEdges.None,
            Children =
            {
                _storiesView,
                BuildFixedBackButtonOverlay("Gaan terug na Luister")
            }
        };
        Content = PersistentPlaybackHost.Wrap(pageContent, storyPlaybackSession);
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("playlist", out var value) && value is MobilePlaylist playlist)
        {
            SetPlaylist(playlist);
        }
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        if (_hero is null || height <= 0 || Math.Abs(height - _lastPageHeight) < 1)
        {
            return;
        }

        _lastPageHeight = height;
        _hero.HeightRequest = Math.Clamp(height * 0.8, 480, 720);
    }

    private void SetPlaylist(MobilePlaylist playlist)
    {
        _playlist = playlist;
        var appearance = ResolveShowcaseAppearance(playlist);
        _pageTextColor = appearance.FontColor;
        var pageBackground = new LinearGradientBrush(
            [
                new GradientStop(appearance.BackgroundStartColor, 0),
                new GradientStop(appearance.BackgroundEndColor, 1)
            ],
            new Point(0, 0),
            new Point(0, 1));
        Background = pageBackground;
        _storiesView.Background = pageBackground;
        _stories.Clear();
        foreach (var story in playlist.Stories)
        {
            _stories.Add(new StoryCardItem(
                story,
                PageHelpers.BuildStoryImageRequest(story, _apiClient, "schink_background.jpeg")));
        }

        _storiesView.Header = BuildHeader(playlist, appearance);
    }

    private View BuildHeader(MobilePlaylist playlist, ShowcaseAppearance appearance)
    {
        var body = new VerticalStackLayout
        {
            Spacing = 16,
            BackgroundColor = Colors.Transparent,
            Children =
            {
                BuildHero(playlist, appearance),
                BuildIntro(playlist)
            }
        };

        if (playlist.ShowcaseStory is { } showcaseStory)
        {
            body.Children.Add(BuildFeaturedStory(showcaseStory));
        }

        body.Children.Add(new Label
        {
            Text = "Stories in hierdie speellys",
            TextColor = _pageTextColor,
            FontFamily = "PoppinsSemiBold",
            FontSize = 22,
            HorizontalTextAlignment = TextAlignment.Center,
            Margin = new Thickness(14, 2, 14, 0)
        });
        return body;
    }

    private View BuildHero(MobilePlaylist playlist, ShowcaseAppearance appearance)
    {
        var backdrop = new ProgressiveCachedImage(
            _apiClient,
            new ProgressiveImageRequest(appearance.BackdropUrl, FallbackFile: "schink_background.jpeg"))
        {
            Aspect = Aspect.AspectFill
        };

        var heroGrid = new Grid
        {
            SafeAreaEdges = SafeAreaEdges.None,
            BackgroundColor = appearance.BackgroundStartColor,
            Children = { backdrop }
        };

        if (!string.IsNullOrWhiteSpace(appearance.LogoUrl))
        {
            heroGrid.Children.Add(new ProgressiveCachedImage(
                _apiClient,
                new ProgressiveImageRequest(appearance.LogoUrl))
            {
                Aspect = Aspect.AspectFit,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
                WidthRequest = 346,
                HeightRequest = 360,
                Margin = new Thickness(24, 34, 24, 82),
                Shadow = IsAndroid
                    ? null!
                    : new Shadow
                    {
                        Brush = Brush.Black,
                        Offset = new Point(0, 14),
                        Radius = 28,
                        Opacity = 0.36f
                    }
            });
        }

        var actions = new Border
        {
            BackgroundColor = Color.FromArgb("#EBFFFFFF"),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 28 },
            Padding = 5,
            Margin = new Thickness(12, 0, 12, 14),
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.End,
            Content = new HorizontalStackLayout
            {
                Spacing = 5,
                Children =
                {
                    BuildActionPill(
                        PlaylistPlayIconGlyph,
                        "Speel Speellys",
                        AccentColor,
                        OpenPlaylistPlayerAsync),
                    BuildActionPill(
                        DownIconGlyph,
                        "Kies 'n Storie",
                        Color.FromArgb("#F4F6F8"),
                        ScrollToStoriesAsync,
                        stroke: Color.FromArgb("#24202B35"))
                }
            }
        };
        heroGrid.Children.Add(actions);

        _hero = new Border
        {
            HeightRequest = 620,
            BackgroundColor = appearance.BackgroundStartColor,
            StrokeThickness = 0,
            Content = heroGrid
        };
        return _hero;
    }

    private View BuildIntro(MobilePlaylist playlist)
    {
        var stack = new VerticalStackLayout
        {
            Spacing = 8,
            Padding = new Thickness(18, 0),
            HorizontalOptions = LayoutOptions.Center,
            MaximumWidthRequest = 900
        };

        if (!string.IsNullOrWhiteSpace(playlist.Description))
        {
            stack.Children.Add(new Label
            {
                Text = playlist.Description,
                TextColor = _pageTextColor,
                FontSize = 15,
                LineHeight = 1.42,
                HorizontalTextAlignment = TextAlignment.Center
            });
        }

        stack.Children.Add(new Label
        {
            Text = playlist.Stories.Count == 1
                ? "1 STORIE OM TE LUISTER"
                : $"{playlist.Stories.Count} STORIES OM TE LUISTER",
            TextColor = _pageTextColor,
            FontFamily = "PoppinsBold",
            FontSize = 13,
            CharacterSpacing = 0.5,
            HorizontalTextAlignment = TextAlignment.Center
        });
        return stack;
    }

    private View BuildFeaturedStory(MobileStorySummary story)
    {
        var image = new ProgressiveCachedImage(
            _apiClient,
            PageHelpers.BuildStoryImageRequest(story, _apiClient, "schink_background.jpeg"))
        {
            Aspect = Aspect.AspectFill
        };
        var imageGrid = new Grid { Children = { image } };
        imageGrid.Children.Add(BuildPlayBadge(story.IsLocked, 64));
        imageGrid.Children.Add(BuildFavoriteButton(story, topMargin: 8, rightMargin: 8));

        var cover = new Border
        {
            BackgroundColor = Color.FromArgb("#0F1116"),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 20 },
            HeightRequest = IsMusicStory(story) ? 350 : 220,
            Content = imageGrid,
            Shadow = IsAndroid
                ? null!
                : new Shadow
                {
                    Brush = Brush.Black,
                    Offset = new Point(0, 16),
                    Radius = 28,
                    Opacity = 0.22f
                }
        };
        cover.SizeChanged += (_, _) => cover.HeightRequest = IsMusicStory(story)
            ? cover.Width
            : cover.Width * 9d / 16d;

        var card = new VerticalStackLayout
        {
            Spacing = 9,
            Margin = new Thickness(16, 0),
            MaximumWidthRequest = 680,
            HorizontalOptions = LayoutOptions.Center,
            Children =
            {
                cover,
                new Label
                {
                    Text = story.Title,
                    TextColor = _pageTextColor,
                    FontFamily = "PoppinsSemiBold",
                    FontSize = 21,
                    HorizontalTextAlignment = TextAlignment.Center
                },
                new Label
                {
                    Text = story.Description,
                    TextColor = _pageTextColor,
                    FontSize = 14,
                    LineHeight = 1.42,
                    HorizontalTextAlignment = TextAlignment.Center
                }
            }
        };
        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) => await OpenStoryAsync(story);
        card.GestureRecognizers.Add(tap);
        SemanticProperties.SetDescription(card, $"Luister na {story.Title}");
        return card;
    }

    private View BuildStoryCard()
    {
        var image = new ProgressiveCachedImage(_apiClient) { Aspect = Aspect.AspectFill };
        image.SetBinding(ProgressiveCachedImage.RequestProperty, nameof(StoryCardItem.ImageRequest));

        var playBadge = new Label
        {
            FontFamily = "FontAwesomeSolid",
            FontSize = 16,
            TextColor = Color.FromArgb("#1A1A1A"),
            BackgroundColor = AccentColor,
            WidthRequest = 48,
            HeightRequest = 48,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };
        playBadge.SetBinding(Label.TextProperty, nameof(StoryCardItem.ActionGlyph));

        var favorite = new Button
        {
            Text = HeartIconGlyph,
            FontFamily = "FontAwesomeSolid",
            FontSize = 17,
            BackgroundColor = Colors.Transparent,
            WidthRequest = 38,
            HeightRequest = 38,
            Padding = 0,
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Start,
            Margin = new Thickness(0, 5, 5, 0)
        };
        favorite.SetBinding(Button.TextColorProperty, nameof(StoryCardItem.FavoriteColor));
        favorite.Clicked += async (sender, _) =>
        {
            if ((sender as BindableObject)?.BindingContext is StoryCardItem item)
            {
                await ToggleFavoriteAsync(item.Story);
            }
        };

        var coverGrid = new Grid { Children = { image, playBadge, favorite } };
        var cover = new Border
        {
            BackgroundColor = Color.FromArgb("#0F1116"),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 16 },
            HeightRequest = 220,
            Content = coverGrid,
            Shadow = IsAndroid
                ? null!
                : new Shadow
                {
                    Brush = Brush.Black,
                    Offset = new Point(0, 9),
                    Radius = 18,
                    Opacity = 0.23f
                }
        };

        var title = new Label
        {
            TextColor = _pageTextColor,
            FontFamily = "Poppins",
            FontSize = 15,
            LineHeight = 1.3,
            MaxLines = 2,
            HorizontalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.TailTruncation
        };
        title.SetBinding(Label.TextProperty, nameof(StoryCardItem.Title));

        var card = new VerticalStackLayout
        {
            Spacing = 8,
            Margin = new Thickness(8, 0),
            Children = { cover, title }
        };
        card.SizeChanged += (_, _) =>
        {
            if (card.Width > 0)
            {
                cover.HeightRequest = card.Width * 4d / 3d;
            }
        };
        var tap = new TapGestureRecognizer();
        tap.Tapped += async (sender, _) =>
        {
            if ((sender as BindableObject)?.BindingContext is StoryCardItem item)
            {
                await OpenStoryAsync(item.Story);
            }
        };
        card.GestureRecognizers.Add(tap);
        return card;
    }

    private static Border BuildPlayBadge(bool isLocked, double size) =>
        new()
        {
            BackgroundColor = AccentColor,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = size / 2 },
            WidthRequest = size,
            HeightRequest = size,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Content = new Label
            {
                Text = isLocked ? LockIconGlyph : PlayIconGlyph,
                FontFamily = "FontAwesomeSolid",
                FontSize = size * 0.28,
                TextColor = Color.FromArgb("#1A1A1A"),
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center
            }
        };

    private Button BuildFavoriteButton(MobileStorySummary story, double topMargin, double rightMargin)
    {
        var button = new Button
        {
            Text = HeartIconGlyph,
            FontFamily = "FontAwesomeSolid",
            FontSize = 18,
            TextColor = story.IsFavorite ? Color.FromArgb("#FFE6EF") : Colors.White,
            BackgroundColor = Colors.Transparent,
            WidthRequest = 40,
            HeightRequest = 40,
            Padding = 0,
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Start,
            Margin = new Thickness(0, topMargin, rightMargin, 0)
        };
        SemanticProperties.SetDescription(button, story.IsFavorite ? "Verwyder uit gunstelinge" : "Voeg by gunstelinge");
        button.Clicked += async (_, _) => await ToggleFavoriteAsync(story);
        return button;
    }

    private static Border BuildActionPill(
        string glyph,
        string text,
        Color background,
        Func<Task> action,
        Color? stroke = null)
    {
        var content = new HorizontalStackLayout
        {
            Spacing = 7,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Label
                {
                    Text = glyph,
                    FontFamily = "FontAwesomeSolid",
                    FontSize = 14,
                    TextColor = Color.FromArgb("#151515"),
                    VerticalTextAlignment = TextAlignment.Center
                },
                new Label
                {
                    Text = text,
                    FontFamily = "PoppinsSemiBold",
                    FontSize = 13,
                    TextColor = Color.FromArgb("#151515"),
                    VerticalTextAlignment = TextAlignment.Center,
                    LineBreakMode = LineBreakMode.NoWrap
                }
            }
        };
        var pill = new Border
        {
            BackgroundColor = background,
            Stroke = stroke ?? Colors.Transparent,
            StrokeThickness = stroke is null ? 0 : 1,
            StrokeShape = new RoundRectangle { CornerRadius = 23 },
            HeightRequest = 46,
            Padding = new Thickness(12, 0),
            Content = content
        };
        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) => await action();
        pill.GestureRecognizers.Add(tap);
        SemanticProperties.SetDescription(pill, text);
        return pill;
    }

    private async Task OpenPlaylistPlayerAsync()
    {
        if (_playlist is null)
        {
            return;
        }

        var firstStory = _playlist.Stories.FirstOrDefault(story => !story.IsLocked)
            ?? _playlist.Stories.FirstOrDefault();
        _playlistPlaybackState.Set(_playlist, firstStory);
        await Shell.Current.GoToAsync(
            nameof(PlaylistDetailPage),
            animate: false,
            parameters: new Dictionary<string, object> { ["playlist"] = _playlist });
    }

    private Task ScrollToStoriesAsync()
    {
        if (_stories.Count > 0)
        {
            _storiesView.ScrollTo(0, position: ScrollToPosition.Start, animate: true);
        }
        return Task.CompletedTask;
    }

    private async Task OpenStoryAsync(MobileStorySummary story)
    {
        if (_playlist is null)
        {
            return;
        }

        if (story.IsLocked)
        {
            await Shell.Current.GoToAsync(nameof(PlansPage), animate: true);
            return;
        }

        _playlistPlaybackState.Set(_playlist, story);
        await _transitionBackdropState.CaptureAsync();
        await Shell.Current.GoToAsync(
            $"{nameof(StoryDetailPage)}?slug={Uri.EscapeDataString(story.Slug)}&source={Uri.EscapeDataString(story.Source)}",
            animate: false,
            parameters: new Dictionary<string, object>
            {
                ["preview"] = story,
                ["playlistTitle"] = _playlist.Title,
                ["playlistSlug"] = _playlist.Slug
            });
    }

    private async Task ToggleFavoriteAsync(MobileStorySummary story)
    {
        if (!_sessionState.Current.IsSignedIn)
        {
            await DisplayAlertAsync("Teken in", "Teken eers in om gunstelinge te stoor.", "Reg so");
            return;
        }

        try
        {
            var isFavorite = await _apiClient.SetFavoriteAsync(story.Slug, story.Source, !story.IsFavorite);
            foreach (var item in _stories.Where(item => SameStory(item.Story, story)))
            {
                item.SetFavorite(isFavorite);
            }

            if (_playlist is not null)
            {
                _playlist = _playlist with
                {
                    Stories = _playlist.Stories
                        .Select(candidate => SameStory(candidate, story)
                            ? candidate with { IsFavorite = isFavorite }
                            : candidate)
                        .ToArray(),
                    ShowcaseStory = _playlist.ShowcaseStory is { } showcase && SameStory(showcase, story)
                        ? showcase with { IsFavorite = isFavorite }
                        : _playlist.ShowcaseStory
                };
                _storiesView.Header = BuildHeader(_playlist, ResolveShowcaseAppearance(_playlist));
            }
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Kon nie stoor nie", ex.Message, "Reg so");
        }
    }

    private static bool SameStory(MobileStorySummary left, MobileStorySummary right) =>
        string.Equals(left.Slug, right.Slug, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(left.Source, right.Source, StringComparison.OrdinalIgnoreCase);

    private static Grid BuildFixedBackButtonOverlay(string description)
    {
        var backButton = new Button
        {
            Text = BackIconGlyph,
            FontFamily = "FontAwesomeSolid",
            FontSize = 22,
            TextColor = Colors.White,
            BackgroundColor = Colors.Transparent,
            BorderWidth = 0,
            WidthRequest = 52,
            HeightRequest = 52,
            Padding = 0,
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Start,
            Shadow = new Shadow
            {
                Brush = Brush.Black,
                Offset = new Point(0, 1),
                Radius = 4,
                Opacity = 0.45f
            }
        };
        SemanticProperties.SetDescription(backButton, description);
        backButton.Clicked += async (_, _) => await Shell.Current.GoToAsync("..", animate: false);

        return new Grid
        {
            SafeAreaEdges = new SafeAreaEdges(
                SafeAreaRegions.None,
                SafeAreaRegions.Container,
                SafeAreaRegions.None,
                SafeAreaRegions.None),
            HeightRequest = 96,
            Padding = new Thickness(16, 2, 0, 0),
            BackgroundColor = Colors.Transparent,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Start,
            ZIndex = 100,
            Children = { backButton }
        };
    }

    private static ShowcaseAppearance ResolveShowcaseAppearance(MobilePlaylist playlist)
    {
        var hasApiAppearance = !string.IsNullOrWhiteSpace(playlist.LogoUrl) ||
            !string.IsNullOrWhiteSpace(playlist.BackgroundStartColorHex) ||
            !string.IsNullOrWhiteSpace(playlist.BackgroundEndColorHex) ||
            !string.IsNullOrWhiteSpace(playlist.FontColorHex);
        if (hasApiAppearance)
        {
            var startColor = ParseColor(playlist.BackgroundStartColorHex, DefaultPageColor);
            return new ShowcaseAppearance(
                playlist.BackdropUrl,
                playlist.LogoUrl,
                startColor,
                ParseColor(playlist.BackgroundEndColorHex, startColor),
                ParseColor(playlist.FontColorHex, DefaultTextColor));
        }

        if (LegacyShowcaseAppearances.TryGetValue(playlist.Slug, out var legacy))
        {
            return new ShowcaseAppearance(
                legacy.BackdropUrl,
                legacy.LogoUrl,
                ParseColor(legacy.BackgroundStartColorHex, DefaultPageColor),
                ParseColor(legacy.BackgroundEndColorHex, DefaultPageColor),
                ParseColor(legacy.FontColorHex, DefaultTextColor));
        }

        return new ShowcaseAppearance(
            playlist.BackdropUrl,
            null,
            DefaultPageColor,
            DefaultPageColor,
            DefaultTextColor);
    }

    private static Color ParseColor(string? value, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        try
        {
            return Color.FromArgb(value.Trim());
        }
        catch (FormatException)
        {
            return fallback;
        }
    }

    private static bool IsMusicStory(MobileStorySummary story) =>
        string.Equals(story.StoryType, "music", StringComparison.OrdinalIgnoreCase);

    private sealed record ShowcaseAppearance(
        string BackdropUrl,
        string? LogoUrl,
        Color BackgroundStartColor,
        Color BackgroundEndColor,
        Color FontColor);

    private sealed record LegacyShowcaseAppearance(
        string BackdropUrl,
        string? LogoUrl,
        string BackgroundStartColorHex,
        string BackgroundEndColorHex,
        string FontColorHex);

    private sealed class StoryCardItem : INotifyPropertyChanged
    {
        private MobileStorySummary _story;

        public StoryCardItem(MobileStorySummary story, ProgressiveImageRequest imageRequest)
        {
            _story = story;
            ImageRequest = imageRequest;
        }

        public MobileStorySummary Story => _story;
        public string Title => _story.Title;
        public ProgressiveImageRequest ImageRequest { get; }
        public string ActionGlyph => _story.IsLocked ? LockIconGlyph : PlayIconGlyph;
        public Color FavoriteColor => _story.IsFavorite ? Color.FromArgb("#FFE6EF") : Colors.White;

        public event PropertyChangedEventHandler? PropertyChanged;

        public void SetFavorite(bool isFavorite)
        {
            _story = _story with { IsFavorite = isFavorite };
            Notify(nameof(FavoriteColor));
        }

        private void Notify([CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
