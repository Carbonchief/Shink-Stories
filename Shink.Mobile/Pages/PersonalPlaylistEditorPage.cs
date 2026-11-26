using Shink.Mobile.Models;
using Shink.Mobile.Services;
using static Shink.Mobile.Pages.MyStoriesPage;

namespace Shink.Mobile.Pages;

internal sealed class PersonalPlaylistEditorPage : ContentPage
{
    private readonly MobileApiClient _api;
    private readonly MobilePersonalPlaylist? _playlist;
    private readonly IReadOnlyDictionary<string, MobileStorySummary> _catalog;
    private readonly List<string> _selected;
    private readonly Entry _name = new() { Placeholder = "Naam van speellys", MaxLength = 80 };
    private readonly SearchBar _search = new() { Placeholder = "Soek ’n storie" };
    private readonly Label _error = Copy(string.Empty, 14);
    private const int PageSize = 20;
    private readonly VerticalStackLayout _rows = new() { Spacing = 1 };
    private readonly ScrollView _list = new();
    private int _visibleRows = PageSize;
    private readonly Button _selectedTab;
    private readonly Button _catalogTab;
    private readonly Button _save;
    private bool _picking;
    private bool _busy;

    public PersonalPlaylistEditorPage(MobileApiClient api, MobilePersonalPlaylist? playlist,
        IReadOnlyDictionary<string, MobileStorySummary> catalog)
    {
        _api = api;
        _playlist = playlist;
        _catalog = catalog;
        // Keep unavailable saved slugs visible so the listener can remove them before saving.
        _selected = playlist?.StorySlugs.Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(slug => catalog.GetValueOrDefault(slug)?.Slug ?? slug).ToList() ?? [];
        _picking = _selected.Count == 0;
        _name.Text = playlist?.Title;
        Title = playlist is null ? "Skep ’n speellys" : "Wysig speellys";
        BackgroundColor = Color.FromArgb("#FFF7E8");
        SafeAreaEdges = new SafeAreaEdges(SafeAreaRegions.All);
        _name.TextColor = _search.TextColor = Color.FromArgb("#243238");
        _name.PlaceholderColor = _search.PlaceholderColor = Colors.DimGray;
        SemanticProperties.SetDescription(_name, "Naam van speellys");
        _search.TextChanged += (_, _) => { if (_picking) ResetList(); };
        _error.TextColor = Color.FromArgb("#8F3B3B");
        _error.IsVisible = false;
        _list.Content = _rows;

        _selectedTab = ActionButton("Gekies", () => SetModeAsync(false));
        _catalogTab = ActionButton("Voeg stories by", () => SetModeAsync(true));
        var tabs = TwoColumns(_selectedTab, _catalogTab);
        _save = ActionButton("Stoor", SaveAsync);
        _name.TextChanged += (_, _) => UpdateActions();
        var footer = TwoColumns(ActionButton("Kanselleer", CloseAsync), _save);
        var header = new VerticalStackLayout { Spacing = 6 };
        header.Add(Copy(Title, 22, bold: true));
        header.Add(_name);
        header.Add(tabs);
        header.Add(_search);
        header.Add(_error);
        var bottom = new VerticalStackLayout { Spacing = 6, Children = { footer } };
        if (playlist is not null)
        {
            var delete = ActionButton("Verwyder speellys", DeleteAsync);
            delete.BackgroundColor = Color.FromArgb("#8F3B3B");
            bottom.Add(delete);
        }
        var layout = new Grid
        {
            Padding = new Thickness(16, 12), RowSpacing = 8,
            RowDefinitions = { new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) }
        };
        layout.Add(header, 0, 0);
        layout.Add(_list, 0, 1);
        layout.Add(bottom, 0, 2);
        SizeChanged += (_, _) => MobileResponsiveLayout.ApplyCenteredContent(layout, Width, 720);
        Content = layout;
        RefreshList();
    }

    private static Grid TwoColumns(View left, View right)
    {
        var grid = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Star) }, ColumnSpacing = 8 };
        grid.Add(left, 0);
        grid.Add(right, 1);
        return grid;
    }

    private Task SetModeAsync(bool picking)
    {
        _picking = picking;
        _name.Unfocus();
        _search.Unfocus();
        ResetList();
        return Task.CompletedTask;
    }

    private void UpdateActions()
    {
        _selectedTab.Text = $"Gekies ({_selected.Count})";
        _selectedTab.Opacity = _picking ? 0.65 : 1;
        _catalogTab.Opacity = _picking ? 1 : 0.65;
        _save.IsEnabled = !string.IsNullOrWhiteSpace(_name.Text);
    }

    private void ResetList()
    {
        _visibleRows = PageSize;
        _ = _list.ScrollToAsync(0, 0, false);
        RefreshList();
    }

    private void RefreshList()
    {
        _search.IsVisible = _picking;
        var term = _search.Text?.Trim() ?? string.Empty;
        var slugs = _picking
            ? _catalog.Values.Where(story => story.Title.Contains(term, StringComparison.OrdinalIgnoreCase))
                .Select(story => story.Slug).ToArray()
            : _selected.ToArray();
        _rows.Clear();
        foreach (var slug in slugs.Take(_visibleRows)) _rows.Add(BuildRow(slug));
        if (slugs.Length == 0)
            _rows.Add(Copy(_picking ? "Geen stories gevind nie." : "Kies ‘Voeg stories by’ om te begin."));
        if (slugs.Length > _visibleRows)
            _rows.Add(ActionButton("Wys meer", () =>
            {
                _visibleRows += PageSize;
                RefreshList();
                return Task.CompletedTask;
            }));
        UpdateActions();
    }

    private View BuildRow(string slug)
    {
        var art = new ProgressiveCachedImage(_api) { HeightRequest = 44, WidthRequest = 36, Aspect = Aspect.AspectFill };
        var name = Copy(string.Empty, 15);
        name.VerticalOptions = LayoutOptions.Center;
        var actions = new HorizontalStackLayout { Spacing = 3, VerticalOptions = LayoutOptions.Center };
        var row = new Grid
        {
            Padding = new Thickness(0, 5), ColumnSpacing = 6,
            ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) }
        };
        row.Add(art, 0);
        row.Add(name, 1);
        row.Add(actions, 2);
        var story = _catalog.GetValueOrDefault(slug);
        name.Text = story?.Title ?? $"{slug} (nie meer beskikbaar nie)";
        art.Request = story is null ? new ProgressiveImageRequest(null, FallbackFile: PageHelpers.StoryPlaceholderFile)
            : PageHelpers.BuildStoryCardImageRequest(story, _api);
        if (_picking)
        {
            var toggle = RowAction("", name.Text, button => ToggleStory(button, slug, name.Text));
            UpdateToggle(toggle, slug, name.Text);
            actions.Add(toggle);
            return row;
        }
        var index = _selected.IndexOf(slug);
        var up = RowAction("↑", $"Skuif {name.Text} op", _ => Move(slug, -1));
        var down = RowAction("↓", $"Skuif {name.Text} af", _ => Move(slug, 1));
        up.IsEnabled = index > 0;
        down.IsEnabled = index >= 0 && index < _selected.Count - 1;
        actions.Add(up);
        actions.Add(down);
        actions.Add(RowAction("×", $"Verwyder {name.Text}", _ => { _selected.Remove(slug); RefreshList(); }));
        return row;
    }

    private void ToggleStory(Button button, string slug, string title)
    {
        if (!_selected.Remove(slug))
        {
            if (_selected.Count >= 100)
            {
                ShowError("Kies hoogstens 100 stories vir jou speellys.");
                return;
            }
            _selected.Add(slug);
        }
        _error.IsVisible = false;
        UpdateToggle(button, slug, title);
        UpdateActions();
    }

    private void UpdateToggle(Button button, string slug, string title)
    {
        var selected = _selected.Contains(slug);
        button.Text = selected ? "✓" : "+";
        SemanticProperties.SetDescription(button, selected ? $"Verwyder {title}" : $"Voeg {title} by");
    }

    private static Button RowAction(string text, string description, Action<Button> action)
    {
        var button = new Button
        {
            Text = text, WidthRequest = 44, HeightRequest = 44, Padding = 0, FontSize = 20,
            CornerRadius = 12, BackgroundColor = Color.FromArgb("#E5EEE6"), TextColor = Color.FromArgb("#123F3F")
        };
        SemanticProperties.SetDescription(button, description);
        button.Clicked += (_, _) => action(button);
        return button;
    }

    private void Move(string slug, int direction)
    {
        var index = _selected.IndexOf(slug);
        var target = index + direction;
        if (index < 0 || target < 0 || target >= _selected.Count) return;
        (_selected[index], _selected[target]) = (_selected[target], _selected[index]);
        RefreshList();
    }

    private Task SaveAsync() => CommitAsync(async () =>
    {
        var saved = await _api.SavePersonalPlaylistAsync(_playlist?.PlaylistId, _name.Text.Trim(), _selected.ToArray(), CancellationToken.None);
        if (saved is null) throw new InvalidOperationException("Ons kon nie jou speellys stoor nie.");
    });

    private async Task DeleteAsync()
    {
        if (_busy || _playlist is null || !await DisplayAlertAsync("Verwyder speellys?",
            $"Wil jy “{_playlist.Title}” verwyder?", "Verwyder", "Kanselleer")) return;
        await CommitAsync(() => _api.DeletePersonalPlaylistAsync(_playlist.PlaylistId, CancellationToken.None));
    }

    private async Task CommitAsync(Func<Task> operation)
    {
        if (_busy) return;
        _busy = true;
        Content.IsEnabled = false;
        try
        {
            await operation();
            if (Navigation.ModalStack.LastOrDefault() == this) await Navigation.PopModalAsync();
        }
        catch (Exception ex)
        {
            ShowError(ex is InvalidOperationException ? ex.Message : "Ons kon nie jou verandering stoor nie. Probeer asseblief weer.");
        }
        finally
        {
            _busy = false;
            Content.IsEnabled = true;
        }
    }

    private void ShowError(string message)
    {
        _error.Text = message;
        _error.IsVisible = true;
    }

    protected override bool OnBackButtonPressed() => _busy || base.OnBackButtonPressed();

    private Task CloseAsync() => _busy ? Task.CompletedTask : Navigation.PopModalAsync();
}
