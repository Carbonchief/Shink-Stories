using Shink.Mobile.Models;
using Shink.Mobile.Services;

namespace Shink.Mobile.Pages;

public sealed class MyStoriesPage : ContentPage
{
    private readonly MobileApiClient _api;
    private readonly SessionState _session;
    private readonly VerticalStackLayout _sections = new() { Padding = new Thickness(18, 14, 18, 28), Spacing = 16 };
    private readonly NavigationGate _navigation = new();
    private CancellationTokenSource? _loading;

    public MyStoriesPage(MobileApiClient api, SessionState session, StoryPlaybackSession playback)
    {
        _api = api;
        _session = session;
        Title = "My stories";
        BackgroundColor = Color.FromArgb("#FFF7E8");
        SafeAreaEdges = new SafeAreaEdges(SafeAreaRegions.Container);
        Shell.SetTabBarIsVisible(this, false);
        Shell.SetNavBarIsVisible(this, true);
        SizeChanged += (_, _) => MobileResponsiveLayout.ApplyCenteredContent(_sections, Width, 760);
        Content = PersistentPlaybackHost.Wrap(new ScrollView { Content = _sections }, playback);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadAsync();
    }

    protected override void OnDisappearing()
    {
        _loading?.Cancel();
        base.OnDisappearing();
    }

    private async Task LoadAsync()
    {
        _loading?.Cancel();
        using var loading = new CancellationTokenSource();
        _loading = loading;
        _sections.Clear();
        try
        {
            if (!_session.Current.IsSignedIn)
            {
                _sections.Add(Copy("Teken in om jou stories te sien."));
                _sections.Add(ActionButton("Teken in", () => Shell.Current.GoToAsync(
                    nameof(AccountPage), new ShellNavigationQueryParameters { ["returnUrl"] = "/my-stories", ["signIn"] = true })));
                return;
            }

            _sections.Add(new ActivityIndicator { IsRunning = true, Color = Colors.Teal });
            var library = await _api.GetMyStoriesAsync(loading.Token);
            loading.Token.ThrowIfCancellationRequested();
            if (library is null) throw new InvalidDataException();
            _sections.Clear();
            Render(library);
        }
        catch (OperationCanceledException) when (loading.IsCancellationRequested) { }
        catch (Exception)
        {
            _sections.Clear();
            _sections.Add(Copy("Ons kon nie jou stories laai nie. Gaan jou verbinding na en probeer weer."));
            _sections.Add(ActionButton("Probeer weer", LoadAsync));
        }
        finally
        {
            if (ReferenceEquals(_loading, loading)) _loading = null;
        }
    }

    private void Render(MobileStoryLibrary library)
    {
        var comparer = StringComparer.OrdinalIgnoreCase;
        var catalog = library.Catalog.ToDictionary(story => story.Slug, comparer);
        var history = library.Progress.DistinctBy(progress => progress.StorySlug, comparer)
            .Select(progress => new LibraryCard(catalog.GetValueOrDefault(progress.StorySlug), progress)).ToArray();
        var playedSlugs = history.Select(card => card.Progress!.StorySlug).ToHashSet(comparer);
        var favorites = history.Where(card => card.Story?.IsFavorite == true)
            .Concat(library.Catalog.Where(story => story.IsFavorite && !playedSlugs.Contains(story.Slug))
                .OrderBy(story => story.Title, comparer).Select(story => new LibraryCard(story, null))).ToArray();

        var profile = new VerticalStackLayout { Spacing = 8 };
        var heading = new Grid { ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star) }, ColumnSpacing = 12 };
        heading.Add(MobileTopBar.BuildProfileAvatar(_api, _session.Current), 0);
        heading.Add(new VerticalStackLayout
        {
            VerticalOptions = LayoutOptions.Center,
            Children = { Copy(_session.Current.DisplayName ?? "Schink luisteraar", 22, bold: true), Copy("Jou luisterreis", 14) }
        }, 1);
        profile.Add(heading);
        var stats = new Grid
        {
            ColumnDefinitions = { new(GridLength.Star), new(GridLength.Star) },
            RowDefinitions = { new(GridLength.Auto), new(GridLength.Auto) },
            ColumnSpacing = 12, RowSpacing = 10
        };
        AddStat("Stories geluister", history.Length.ToString(), 0, 0);
        AddStat("Klaar geluister", history.Count(card => card.Progress!.IsCompleted).ToString(), 1, 0);
        AddStat("Gunstelinge", favorites.Length.ToString(), 0, 1);
        AddStat("Totale luistertyd", FormatListenTime(history.Sum(card => Math.Max(0, card.Progress!.TotalListenedSeconds))), 1, 1);
        profile.Add(stats);
        _sections.Add(Panel(profile));

        var audioStories = library.GetPlaylistCatalog();
        _sections.Add(Copy("My speellyste", 22, bold: true));
        if (library.PlaylistError is { } error)
        {
            _sections.Add(Copy(error));
            _sections.Add(ActionButton("Probeer weer", LoadAsync));
        }
        else
        {
            var create = ActionButton("Skep ’n speellys", () => EditPlaylistAsync(null, audioStories));
            create.IsEnabled = library.Playlists.Count < 50;
            _sections.Add(create);
            foreach (var playlist in library.Playlists)
            {
                var playback = playlist.ToPlaybackPlaylist(audioStories);
                var count = playback.Stories.Count;
                var row = new VerticalStackLayout { Spacing = 8 };
                row.Add(Copy(playlist.Title, 18, bold: true));
                row.Add(Copy(count == 1 ? "1 storie" : $"{count} stories"));
                var actions = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Star) }, ColumnSpacing = 8 };
                var open = ActionButton("Maak oop", () => OpenPlaylistAsync(playback));
                open.IsEnabled = count > 0;
                actions.Add(open, 0);
                actions.Add(ActionButton("Wysig", () => EditPlaylistAsync(playlist, audioStories)), 1);
                row.Add(actions);
                _sections.Add(Panel(row));
            }
        }

        AddShelf("Jou gunstelinge", favorites);
        AddShelf("Jy luister tans", history.Where(card => !card.Progress!.IsCompleted).ToArray());
        AddShelf("Klaar geluister", history.Where(card => card.Progress!.IsCompleted).ToArray());
        if (favorites.Length == 0 && history.Length == 0)
        {
            _sections.Add(Copy("Jy het nog nie begin luister nie."));
            _sections.Add(ActionButton("Gaan na stories", () => Shell.Current.GoToAsync("//Luister")));
        }

        void AddStat(string label, string value, int column, int row) =>
            stats.Add(new VerticalStackLayout { Children = { Copy(value, 20, bold: true), Copy(label, 14) } }, column, row);
    }

    private Task EditPlaylistAsync(MobilePersonalPlaylist? playlist, IReadOnlyDictionary<string, MobileStorySummary> catalog) =>
        _navigation.RunAsync(() => Navigation.PushModalAsync(new PersonalPlaylistEditorPage(_api, playlist, catalog)));

    private Task OpenPlaylistAsync(MobilePlaylist playlist) =>
        _navigation.RunAsync(() => Shell.Current.GoToAsync(nameof(PlaylistDetailPage),
            new ShellNavigationQueryParameters { ["playlist"] = playlist }));

    private void AddShelf(string title, LibraryCard[] cards)
    {
        if (cards.Length == 0) return;
        _sections.Add(Copy(title, 22, bold: true));
        _sections.Add(new CollectionView
        {
            HeightRequest = 200,
            SelectionMode = SelectionMode.None,
            ItemsLayout = new LinearItemsLayout(ItemsLayoutOrientation.Horizontal) { ItemSpacing = 10 },
            ItemsSource = cards,
            ItemTemplate = new DataTemplate(BuildShelfCard)
        });
    }

    private View BuildShelfCard()
    {
        var image = new ProgressiveCachedImage(_api) { HeightRequest = 102, Aspect = Aspect.AspectFill };
        var title = Copy(string.Empty, 15, bold: true);
        title.MaxLines = 2;
        title.LineBreakMode = LineBreakMode.TailTruncation;
        var status = Copy(string.Empty, 13);
        var column = new VerticalStackLayout { Spacing = 4, Children = { image, title, status } };
        var card = Panel(column, padding: 7);
        card.WidthRequest = 154;
        card.VerticalOptions = LayoutOptions.Start;
        card.BindingContextChanged += (_, _) =>
        {
            if (card.BindingContext is not LibraryCard item) return;
            title.Text = item.Story?.Title ?? item.Progress!.StorySlug.Replace('-', ' ');
            image.Request = item.Story is { } story ? PageHelpers.BuildStoryCardImageRequest(story, _api)
                : new ProgressiveImageRequest(null, FallbackFile: PageHelpers.StoryPlaceholderFile);
            status.Text = item.Status;
            card.IsEnabled = item.Story is not null;
            SemanticProperties.SetDescription(card, $"{title.Text}. {item.Status}");
        };
        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) =>
        {
            if (card.BindingContext is LibraryCard { Story: { } story } item)
                await _navigation.RunAsync(async () =>
                {
                    if (story.IsLocked)
                    {
                        await PageHelpers.OpenPlansForStoryAsync(story, _session);
                        return;
                    }
                    var parameters = new ShellNavigationQueryParameters { ["preview"] = story };
                    if (item.Progress is { IsCompleted: false, LastPositionSeconds: > 0 } progress)
                        parameters["resumeSeconds"] = progress.LastPositionSeconds.Value;
                    await Shell.Current.GoToAsync(nameof(StoryDetailPage), parameters);
                });
        };
        card.GestureRecognizers.Add(tap);
        return card;
    }

    private sealed record LibraryCard(MobileStorySummary? Story, MobileStoryProgress? Progress)
    {
        public string Status
        {
            get
            {
                if (Story is null) return "Nie meer beskikbaar nie";
                if (Story.IsLocked) return "Gesluit";
                if (Progress?.IsCompleted == true) return "Klaar geluister";
                var duration = Progress?.DurationSeconds ?? Story.DurationSeconds;
                if (Progress?.LastPositionSeconds is > 0 && duration is > 0)
                    return $"{Math.Clamp(decimal.Round(100 * Progress.LastPositionSeconds.Value / duration.Value), 0, 100)}% geluister";
                return duration is > 0 ? $"{(int)duration / 60}:{(int)duration % 60:00}" : string.Empty;
            }
        }
    }

    private static string FormatListenTime(decimal seconds)
    {
        var minutes = seconds > 0 ? Math.Max(1, (int)decimal.Round(seconds / 60, MidpointRounding.AwayFromZero)) : 0;
        if (minutes < 60) return $"{minutes} min";
        return minutes % 60 == 0 ? $"{minutes / 60} uur" : $"{minutes / 60} uur {minutes % 60} min";
    }

    internal static Label Copy(string text, double size = 16, bool bold = false) => new()
    {
        Text = text, FontSize = size, TextColor = Color.FromArgb("#243238"),
        FontAttributes = bold ? FontAttributes.Bold : FontAttributes.None
    };

    private static Border Panel(View content, double padding = 14) => new()
    {
        Content = content, Padding = padding, BackgroundColor = Colors.White,
        Stroke = Color.FromArgb("#E8DDC8"), StrokeShape = new RoundRectangle { CornerRadius = 16 }
    };

    internal static Button ActionButton(string text, Func<Task> action)
    {
        var button = new Button
        {
            Text = text, BackgroundColor = Color.FromArgb("#123F3F"), TextColor = Colors.White,
            CornerRadius = 14, MinimumHeightRequest = 44, Padding = new Thickness(12, 4)
        };
        button.Clicked += async (_, _) => await action();
        return button;
    }
}
