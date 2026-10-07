using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Components;
using Shink.Components.Content;
using Shink.Services;

namespace Shink.Components.Pages;

public partial class MyPlaylists
{
    [Parameter] public IReadOnlyList<StoryItem> Stories { get; set; } = [];
    private readonly CancellationTokenSource _lifetime = new();
    private IReadOnlyList<StoryItem> AudioStories { get; set; } = [];
    private Dictionary<string, StoryItem> StoriesBySlug { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    private List<SubscriberPlaylist> Playlists { get; set; } = [];
    private bool IsLoading { get; set; } = true;
    private bool IsBusy { get; set; }
    private string? LoadError { get; set; }
    private string? OperationError { get; set; }
    private string? Message { get; set; }
    private Guid? EditingId { get; set; }
    private Guid? DeleteId { get; set; }
    private PlaylistDraft? Draft { get; set; }
    private List<string> SelectedSlugs { get; set; } = [];
    private string SearchTerm { get; set; } = string.Empty;
    private IEnumerable<StoryItem> MatchingStories => AudioStories.Where(story =>
        story.Title.Contains(SearchTerm.Trim(), StringComparison.OrdinalIgnoreCase));

    protected override void OnParametersSet()
    {
        AudioStories = Stories.Where(SupabaseSubscriberPlaylistService.IsAudioStory)
            .DistinctBy(story => story.Slug, StringComparer.OrdinalIgnoreCase)
            .OrderBy(story => story.Title, StringComparer.OrdinalIgnoreCase).ToArray();
        StoriesBySlug = AudioStories.ToDictionary(story => story.Slug, StringComparer.OrdinalIgnoreCase);
    }

    protected override Task OnInitializedAsync() => LoadPlaylistsAsync();

    private async Task LoadPlaylistsAsync()
    {
        IsLoading = true;
        try
        {
            var result = await PlaylistService.GetPlaylistsAsync(cancellationToken: _lifetime.Token);
            LoadError = result.IsSuccess ? null : result.ErrorMessage;
            if (result.IsSuccess) Playlists = result.Playlists.ToList();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        finally { IsLoading = false; }
    }

    private void CreatePlaylist()
    {
        EditingId = null;
        Draft = new();
        SelectedSlugs = [];
        ResetEditorFeedback();
    }

    private void EditPlaylist(SubscriberPlaylist playlist)
    {
        EditingId = playlist.PlaylistId;
        Draft = new() { Title = playlist.Title };
        SelectedSlugs = playlist.StorySlugs.ToList();
        ResetEditorFeedback();
    }

    private void ResetEditorFeedback()
    {
        SearchTerm = string.Empty;
        OperationError = null;
        Message = null;
        DeleteId = null;
    }

    private void CloseEditor()
    {
        Draft = null;
        EditingId = null;
        OperationError = null;
    }

    private StoryItem? FindStory(string slug) => StoriesBySlug.GetValueOrDefault(slug);
    private IReadOnlyList<StoryItem> ResolveStories(SubscriberPlaylist playlist) => playlist.StorySlugs
        .Select(FindStory).OfType<StoryItem>().ToArray();

    private void AddStory(string slug)
    {
        if (SelectedSlugs.Count < SupabaseSubscriberPlaylistService.MaximumStories && !SelectedSlugs.Contains(slug, StringComparer.OrdinalIgnoreCase))
            SelectedSlugs.Add(slug);
    }
    private void RemoveStory(string slug) => SelectedSlugs.Remove(slug);
    private void MoveStory(int index, int direction)
    {
        var destination = index + direction;
        if (destination >= 0 && destination < SelectedSlugs.Count)
            (SelectedSlugs[index], SelectedSlugs[destination]) = (SelectedSlugs[destination], SelectedSlugs[index]);
    }

    private async Task SavePlaylistAsync()
    {
        if (IsBusy || Draft is null) return;
        IsBusy = true;
        OperationError = null;
        try
        {
            var result = await PlaylistService.SavePlaylistAsync(EditingId, Draft.Title, SelectedSlugs.ToArray(), _lifetime.Token);
            if (result.IsSuccess && result.Playlist is { } saved)
            {
                Playlists.RemoveAll(playlist => playlist.PlaylistId == saved.PlaylistId);
                Playlists.Insert(0, saved);
                CloseEditor();
                Message = "Jou speellys is gestoor.";
            }
            else OperationError = result.ErrorMessage;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        finally { IsBusy = false; }
    }

    private void AskToDelete(Guid playlistId)
    {
        DeleteId = playlistId;
        Message = null;
        OperationError = null;
    }

    private async Task DeletePlaylistAsync()
    {
        if (IsBusy || DeleteId is not { } id) return;
        IsBusy = true;
        OperationError = null;
        try
        {
            if (await PlaylistService.DeletePlaylistAsync(id, _lifetime.Token))
            {
                Playlists.RemoveAll(playlist => playlist.PlaylistId == id);
                DeleteId = null;
                if (EditingId == id) CloseEditor();
                Message = "Jou speellys is verwyder.";
            }
            else OperationError = "Ons kon nie jou speellys verwyder nie. Probeer asseblief weer.";
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        finally { IsBusy = false; }
    }

    public ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
        return ValueTask.CompletedTask;
    }

    private sealed class PlaylistDraft
    {
        [Required(ErrorMessage = "Gee jou speellys 'n naam.")]
        [StringLength(80, ErrorMessage = "Gebruik hoogstens 80 karakters.")]
        public string Title { get; set; } = string.Empty;
    }
}
