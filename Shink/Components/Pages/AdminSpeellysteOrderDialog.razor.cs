using Microsoft.AspNetCore.Components;
using MudBlazor;
using Shink.Components.Content;
using Shink.Services;

namespace Shink.Components.Pages;

public partial class AdminSpeellysteOrderDialog
{
    [Inject] private IAdminManagementService AdminManagementService { get; set; } = null!;
    [Inject] private ILogger<AdminSpeellysteOrderDialog> Logger { get; set; } = null!;
    [CascadingParameter] private IMudDialogInstance Dialog { get; set; } = null!;

    [Parameter, EditorRequired] public string? AdminEmail { get; set; }
    [Parameter, EditorRequired] public string LanguageCode { get; set; } = "af";
    [Parameter, EditorRequired] public IReadOnlyList<AdminPlaylistRecord> Playlists { get; set; } = [];

    private List<AdminPlaylistRecord> _draft = [];
    private IReadOnlyList<Guid> _initialOrder = [];
    private readonly HashSet<Guid> _squarePlaylistIds = [];
    private AdminSpeellysteCarouselSettings? _savedSettings;
    private string? _error;
    private bool _isLoading = true;
    private bool _isSaving;
    private bool _hasConflict;

    private bool IsEditingDisabled => _isSaving || _hasConflict;

    private bool HasOrderChanges =>
        !_draft.Select(playlist => playlist.PlaylistId).SequenceEqual(_initialOrder);

    private bool CanSave => _savedSettings is not null && !IsEditingDisabled &&
        (HasOrderChanges || !_squarePlaylistIds.SetEquals(_savedSettings.SquarePlaylistIds));

    protected override async Task OnInitializedAsync()
    {
        try
        {
            _savedSettings = await AdminManagementService.GetSpeellysteCarouselSettingsAsync(AdminEmail);
            if (_savedSettings is null)
            {
                _error = T("Kon nie die Speellyste-instellings laai nie.", "Could not load the Speellyste settings.");
                return;
            }

            _squarePlaylistIds.UnionWith(_savedSettings.SquarePlaylistIds);
            _draft = SpeellysteCarouselOrderPolicy.Apply(Playlists, _savedSettings.PlaylistOrder, static playlist => playlist.PlaylistId).ToList();
            _initialOrder = _draft.Select(playlist => playlist.PlaylistId).ToArray();
        }
        catch (Exception exception)
        {
            Logger.LogWarning(exception, "Could not load the Speellyste settings.");
            _error = T("Kon nie die Speellyste-instellings laai nie.", "Could not load the Speellyste settings.");
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void MovePlaylist(Guid playlistId, int offset)
    {
        var index = _draft.FindIndex(playlist => playlist.PlaylistId == playlistId);
        var target = index + offset;
        if (IsEditingDisabled || index < 0 || target < 0 || target >= _draft.Count)
        {
            return;
        }

        (_draft[index], _draft[target]) = (_draft[target], _draft[index]);
    }

    private void SetSquareCover(Guid playlistId, bool useSquareCover)
    {
        if (IsEditingDisabled)
        {
            return;
        }

        if (useSquareCover)
        {
            _squarePlaylistIds.Add(playlistId);
        }
        else
        {
            _squarePlaylistIds.Remove(playlistId);
        }
    }

    private async Task SaveAsync()
    {
        if (!CanSave || _savedSettings is null)
        {
            return;
        }

        _isSaving = true;
        _error = null;
        try
        {
            var orderedIds = HasOrderChanges
                ? _draft.Select(playlist => playlist.PlaylistId).ToArray()
                : _savedSettings.PlaylistOrder;
            var settings = new AdminSpeellysteCarouselSettings(orderedIds, _squarePlaylistIds.ToArray());
            var result = await AdminManagementService.SaveSpeellysteCarouselSettingsAsync(AdminEmail, _savedSettings, settings);
            if (result == AdminSpeellysteSettingsSaveResult.Saved)
            {
                Dialog.Close(DialogResult.Ok(true));
                return;
            }

            if (result == AdminSpeellysteSettingsSaveResult.Conflict)
            {
                _hasConflict = true;
                _error = T(
                    "Die Speellyste-instellings het intussen verander. Maak hierdie venster weer oop om die jongste instellings te laai.",
                    "The Speellyste settings have changed. Reopen this dialog to load the latest settings.");
                return;
            }
        }
        catch (Exception exception)
        {
            Logger.LogWarning(exception, "Could not save the Speellyste settings.");
        }
        finally
        {
            _isSaving = false;
        }

        _error = T("Kon nie die Speellyste-instellings stoor nie. Probeer weer.", "Could not save the Speellyste settings. Try again.");
    }

    private void Cancel() => Dialog.Cancel();

    private string T(string afrikaans, string english) => LanguageCode == "en" ? english : afrikaans;
}
