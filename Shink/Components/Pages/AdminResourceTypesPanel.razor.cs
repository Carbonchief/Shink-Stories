using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using Shink.Services;

namespace Shink.Components.Pages;

public partial class AdminResourceTypesPanel : ComponentBase
{
    private const int MaxResourceFilesPerBatch = 25;
    private const long MaxPdfUploadBytes = 64L * 1024L * 1024L;

    [Parameter]
    public string CurrentLanguageCode { get; set; } = "af";

    [Inject]
    private AuthenticationStateProvider AuthenticationStateProvider { get; set; } = default!;

    [Inject]
    private IAdminManagementService AdminManagementService { get; set; } = default!;

    [Inject]
    private IResourceDocumentStorageService ResourceDocumentStorageService { get; set; } = default!;

    [Inject]
    private ILogger<AdminResourceTypesPanel> Logger { get; set; } = default!;

    private IReadOnlyList<AdminResourceTypeRecord> ResourceTypes { get; set; } = Array.Empty<AdminResourceTypeRecord>();
    private IReadOnlyList<AdminResourceDocumentRecord> ResourceDocuments { get; set; } = Array.Empty<AdminResourceDocumentRecord>();
    private IReadOnlyList<IBrowserFile> SelectedFiles { get; set; } = Array.Empty<IBrowserFile>();
    private EditableResourceType? Editor { get; set; }
    private string? SignedInEmail { get; set; }
    private string? ResourceTypeSearch { get; set; }
    private string? DocumentSearch { get; set; }
    private string? ErrorMessage { get; set; }
    private string? StatusMessage { get; set; }
    private bool IsLoading { get; set; } = true;
    private bool IsLoadingDocuments { get; set; }
    private bool IsSaving { get; set; }
    private bool IsDeleting { get; set; }
    private bool IsUploading { get; set; }
    private HashSet<Guid> DeletingDocumentIds { get; } = [];

    private bool IsNewResourceType => Editor?.ResourceTypeId is not Guid resourceTypeId || resourceTypeId == Guid.Empty;
    private bool CanUploadDocuments => !IsNewResourceType && SelectedFiles.Count > 0;

    private IReadOnlyList<AdminResourceTypeRecord> FilteredResourceTypes =>
        string.IsNullOrWhiteSpace(ResourceTypeSearch)
            ? ResourceTypes
            : ResourceTypes
                .Where(type =>
                    type.Name.Contains(ResourceTypeSearch.Trim(), StringComparison.OrdinalIgnoreCase) ||
                    type.Slug.Contains(ResourceTypeSearch.Trim(), StringComparison.OrdinalIgnoreCase))
                .ToArray();

    private IReadOnlyList<AdminResourceDocumentRecord> FilteredDocuments =>
        string.IsNullOrWhiteSpace(DocumentSearch)
            ? ResourceDocuments
            : ResourceDocuments
                .Where(document =>
                    document.Title.Contains(DocumentSearch.Trim(), StringComparison.OrdinalIgnoreCase) ||
                    document.Slug.Contains(DocumentSearch.Trim(), StringComparison.OrdinalIgnoreCase) ||
                    document.FileName.Contains(DocumentSearch.Trim(), StringComparison.OrdinalIgnoreCase))
                .ToArray();

    protected override async Task OnInitializedAsync()
    {
        var authState = await AuthenticationStateProvider.GetAuthenticationStateAsync();
        SignedInEmail = authState.User.FindFirst(ClaimTypes.Email)?.Value
                        ?? authState.User.Identity?.Name;

        await ReloadAsync();
    }

    private async Task ReloadAsync(Guid? selectResourceTypeId = null)
    {
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            ResourceTypes = await AdminManagementService.GetResourceTypesAsync(SignedInEmail);
            var selected = selectResourceTypeId is Guid targetId && targetId != Guid.Empty
                ? ResourceTypes.FirstOrDefault(type => type.ResourceTypeId == targetId)
                : Editor?.ResourceTypeId is Guid currentId && currentId != Guid.Empty
                    ? ResourceTypes.FirstOrDefault(type => type.ResourceTypeId == currentId)
                    : ResourceTypes.FirstOrDefault();

            Editor = selected is null ? null : EditableResourceType.From(selected);
            SelectedFiles = Array.Empty<IBrowserFile>();

            if (selected is not null)
            {
                await ReloadDocumentsAsync(selected.ResourceTypeId);
            }
            else
            {
                ResourceDocuments = Array.Empty<AdminResourceDocumentRecord>();
            }
        }
        catch (Exception exception)
        {
            Logger.LogError(exception, "Failed to load resources admin data.");
            ResourceTypes = Array.Empty<AdminResourceTypeRecord>();
            ResourceDocuments = Array.Empty<AdminResourceDocumentRecord>();
            Editor = null;
            ErrorMessage = T("Kon nie hulpbronne nou laai nie.", "Could not load resources right now.");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task ReloadDocumentsAsync(Guid resourceTypeId)
    {
        if (resourceTypeId == Guid.Empty)
        {
            ResourceDocuments = Array.Empty<AdminResourceDocumentRecord>();
            return;
        }

        IsLoadingDocuments = true;

        try
        {
            ResourceDocuments = await AdminManagementService.GetResourceDocumentsAsync(SignedInEmail, resourceTypeId);
            if (Editor?.ResourceTypeId == resourceTypeId)
            {
                Editor.DocumentCount = ResourceDocuments.Count;
            }
        }
        catch (Exception exception)
        {
            Logger.LogError(exception, "Failed to load resource documents for {ResourceTypeId}.", resourceTypeId);
            ResourceDocuments = Array.Empty<AdminResourceDocumentRecord>();
            ErrorMessage = T("Kon nie hulpbron dokumente nou laai nie.", "Could not load resource documents right now.");
        }
        finally
        {
            IsLoadingDocuments = false;
        }
    }

    private void BeginCreateResourceType()
    {
        ErrorMessage = null;
        StatusMessage = null;
        SelectedFiles = Array.Empty<IBrowserFile>();
        ResourceDocuments = Array.Empty<AdminResourceDocumentRecord>();
        DocumentSearch = null;

        var nextSortOrder = ResourceTypes.Count == 0
            ? 10
            : ResourceTypes.Max(type => type.SortOrder) + 10;

        Editor = EditableResourceType.CreateEmpty(nextSortOrder);
    }

    private async Task SelectResourceTypeAsync(AdminResourceTypeRecord resourceType)
    {
        ErrorMessage = null;
        StatusMessage = null;
        SelectedFiles = Array.Empty<IBrowserFile>();
        DocumentSearch = null;
        Editor = EditableResourceType.From(resourceType);
        await ReloadDocumentsAsync(resourceType.ResourceTypeId);
    }

    private async Task SaveResourceTypeAsync()
    {
        if (Editor is null || IsSaving)
        {
            return;
        }

        IsSaving = true;
        ErrorMessage = null;
        StatusMessage = null;

        try
        {
            var effectiveSlug = BuildEffectiveSlug(Editor.Slug, Editor.Name);
            if (string.IsNullOrWhiteSpace(effectiveSlug))
            {
                ErrorMessage = T("Voeg asseblief eers 'n naam by.", "Please add a name first.");
                return;
            }

            var result = await AdminManagementService.SaveResourceTypeAsync(
                SignedInEmail,
                new AdminResourceTypeUpdateRequest(
                    ResourceTypeId: Editor.ResourceTypeId,
                    Slug: effectiveSlug,
                    Name: Editor.Name,
                    Description: Editor.Description,
                    SortOrder: Editor.SortOrder,
                    IsEnabled: Editor.IsEnabled));

            if (!result.IsSuccess)
            {
                ErrorMessage = TranslateServiceMessage(result.ErrorMessage) ??
                               T("Kon nie hulpbron tipe nou stoor nie.", "Could not save the resource type right now.");
                return;
            }

            await ReloadAsync(result.EntityId);
            StatusMessage = T("Hulpbron tipe suksesvol gestoor.", "Resource type saved successfully.");
        }
        catch (Exception exception)
        {
            Logger.LogError(exception, "Failed to save resource type.");
            ErrorMessage = T("Kon nie hulpbron tipe nou stoor nie.", "Could not save the resource type right now.");
        }
        finally
        {
            IsSaving = false;
        }
    }

    private async Task DeleteResourceTypeAsync()
    {
        if (Editor?.ResourceTypeId is not Guid resourceTypeId || resourceTypeId == Guid.Empty || IsDeleting)
        {
            return;
        }

        IsDeleting = true;
        ErrorMessage = null;
        StatusMessage = null;

        try
        {
            var result = await AdminManagementService.DeleteResourceTypeAsync(SignedInEmail, resourceTypeId);
            if (!result.IsSuccess)
            {
                ErrorMessage = TranslateServiceMessage(result.ErrorMessage) ??
                               T("Kon nie hulpbron tipe nou verwyder nie.", "Could not delete the resource type right now.");
                return;
            }

            Editor = null;
            ResourceDocuments = Array.Empty<AdminResourceDocumentRecord>();
            SelectedFiles = Array.Empty<IBrowserFile>();
            await ReloadAsync();
            StatusMessage = T("Hulpbron tipe verwyder.", "Resource type deleted.");
        }
        catch (Exception exception)
        {
            Logger.LogError(exception, "Failed to delete resource type.");
            ErrorMessage = T("Kon nie hulpbron tipe nou verwyder nie.", "Could not delete the resource type right now.");
        }
        finally
        {
            IsDeleting = false;
        }
    }

    private void OnResourceFilesSelected(InputFileChangeEventArgs args)
    {
        ErrorMessage = null;
        StatusMessage = null;

        try
        {
            SelectedFiles = args.GetMultipleFiles(MaxResourceFilesPerBatch).ToArray();
        }
        catch (InvalidOperationException)
        {
            SelectedFiles = Array.Empty<IBrowserFile>();
            ErrorMessage = T(
                $"Kies asseblief hoogstens {MaxResourceFilesPerBatch} PDF's op 'n slag.",
                $"Please choose at most {MaxResourceFilesPerBatch} PDFs at a time.");
        }
    }

    private void ClearSelectedFiles()
    {
        SelectedFiles = Array.Empty<IBrowserFile>();
        ErrorMessage = null;
        StatusMessage = null;
    }

    private async Task UploadSelectedDocumentsAsync()
    {
        if (Editor?.ResourceTypeId is not Guid resourceTypeId ||
            resourceTypeId == Guid.Empty ||
            SelectedFiles.Count == 0 ||
            IsUploading)
        {
            return;
        }

        IsUploading = true;
        ErrorMessage = null;
        StatusMessage = null;

        var successCount = 0;
        var failures = new List<string>();
        var usedSlugs = new HashSet<string>(ResourceDocuments.Select(document => document.Slug), StringComparer.OrdinalIgnoreCase);
        var nextSortOrder = ResourceDocuments.Count == 0 ? 10 : ResourceDocuments.Max(document => document.SortOrder) + 10;

        foreach (var file in SelectedFiles)
        {
            var validationMessage = ValidateResourceFile(file);
            if (!string.IsNullOrWhiteSpace(validationMessage))
            {
                failures.Add($"{file.Name}: {validationMessage}");
                continue;
            }

            UploadedResourceDocument? uploadedDocument = null;

            try
            {
                var title = BuildDocumentTitle(file.Name);
                var slug = BuildUniqueDocumentSlug(title, usedSlugs);

                await using var stream = file.OpenReadStream(MaxPdfUploadBytes);
                uploadedDocument = await ResourceDocumentStorageService.UploadDocumentAsync(
                    Editor.SlugPreview,
                    file.Name,
                    NormalizePdfContentType(file),
                    stream);

                var result = await AdminManagementService.CreateResourceDocumentAsync(
                    SignedInEmail,
                    new AdminResourceDocumentCreateRequest(
                        ResourceTypeId: resourceTypeId,
                        Slug: slug,
                        Title: title,
                        Description: null,
                        FileName: file.Name,
                        ContentType: uploadedDocument.ContentType,
                        SizeBytes: file.Size,
                        StorageProvider: "r2",
                        StorageBucket: uploadedDocument.Bucket,
                        StorageObjectKey: uploadedDocument.ObjectKey,
                        SortOrder: nextSortOrder,
                        IsEnabled: true));

                if (!result.IsSuccess)
                {
                    await ResourceDocumentStorageService.DeleteObjectIfExistsAsync(uploadedDocument.ObjectKey);
                    failures.Add($"{file.Name}: {TranslateServiceMessage(result.ErrorMessage) ?? T("Kon nie metadata stoor nie.", "Could not save metadata.")}");
                    continue;
                }

                usedSlugs.Add(slug);
                nextSortOrder += 10;
                successCount++;
            }
            catch (Exception exception)
            {
                if (uploadedDocument is not null)
                {
                    await ResourceDocumentStorageService.DeleteObjectIfExistsAsync(uploadedDocument.ObjectKey);
                }

                Logger.LogError(exception, "Failed to upload resource document {FileName}.", file.Name);
                failures.Add($"{file.Name}: {TranslateStorageMessage(exception.Message)}");
            }
        }

        SelectedFiles = Array.Empty<IBrowserFile>();
        await ReloadAsync(resourceTypeId);

        if (failures.Count == 0)
        {
            StatusMessage = successCount == 1
                ? T("1 dokument suksesvol opgelaai.", "1 document uploaded successfully.")
                : T($"{successCount} dokumente suksesvol opgelaai.", $"{successCount} documents uploaded successfully.");
        }
        else
        {
            var successPrefix = successCount > 0
                ? T($"{successCount} dokumente opgelaai. ", $"{successCount} documents uploaded. ")
                : string.Empty;
            ErrorMessage = successPrefix + string.Join(" ", failures.Take(3));
        }

        IsUploading = false;
    }

    private async Task DeleteDocumentAsync(AdminResourceDocumentRecord document)
    {
        if (!DeletingDocumentIds.Add(document.ResourceDocumentId))
        {
            return;
        }

        ErrorMessage = null;
        StatusMessage = null;

        try
        {
            var result = await AdminManagementService.DeleteResourceDocumentAsync(SignedInEmail, document.ResourceDocumentId);
            if (!result.IsSuccess)
            {
                ErrorMessage = TranslateServiceMessage(result.ErrorMessage) ??
                               T("Kon nie hulpbron dokument nou verwyder nie.", "Could not delete the resource document right now.");
                return;
            }

            await ResourceDocumentStorageService.DeleteObjectIfExistsAsync(document.StorageObjectKey);
            await ReloadAsync(document.ResourceTypeId);
            StatusMessage = T("Hulpbron dokument verwyder.", "Resource document deleted.");
        }
        catch (Exception exception)
        {
            Logger.LogError(exception, "Failed to delete resource document {ResourceDocumentId}.", document.ResourceDocumentId);
            ErrorMessage = T("Kon nie hulpbron dokument nou verwyder nie.", "Could not delete the resource document right now.");
        }
        finally
        {
            DeletingDocumentIds.Remove(document.ResourceDocumentId);
        }
    }

    private string BuildSelectedFilesHint()
    {
        if (SelectedFiles.Count == 0)
        {
            return T("Nog geen PDF's gekies nie.", "No PDFs selected yet.");
        }

        return SelectedFiles.Count == 1
            ? T("1 PDF gereed vir upload.", "1 PDF ready for upload.")
            : T($"{SelectedFiles.Count} PDF's gereed vir upload.", $"{SelectedFiles.Count} PDFs ready for upload.");
    }

    private string BuildDocumentMeta(AdminResourceDocumentRecord document)
    {
        var culture = string.Equals(CurrentLanguageCode, "en", StringComparison.OrdinalIgnoreCase)
            ? CultureInfo.GetCultureInfo("en-ZA")
            : CultureInfo.GetCultureInfo("af-ZA");
        var date = (document.UpdatedAt ?? document.CreatedAt).ToLocalTime().ToString("d MMM yyyy", culture);
        return $"{FormatFileSize(document.SizeBytes)} · {date}";
    }

    private static string FormatFileSize(long sizeBytes)
    {
        var units = new[] { "B", "KB", "MB", "GB" };
        var size = Convert.ToDouble(Math.Max(0, sizeBytes));
        var unitIndex = 0;

        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }

        return unitIndex == 0
            ? $"{sizeBytes} {units[unitIndex]}"
            : $"{size:0.#} {units[unitIndex]}";
    }

    private string? ValidateResourceFile(IBrowserFile file)
    {
        if (file.Size <= 0 || file.Size > MaxPdfUploadBytes)
        {
            return T("Die PDF moet kleiner as 64 MB wees.", "The PDF must be smaller than 64 MB.");
        }

        if (!file.Name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            return T("Net PDF files word ondersteun.", "Only PDF files are supported.");
        }

        return null;
    }

    private string TranslateStorageMessage(string message) =>
        message switch
        {
            "Cloudflare R2 is not fully configured for resource uploads." => T("Cloudflare R2 is nog nie volledig opgestel vir hulpbron uploads nie.", "Cloudflare R2 is not fully configured for resource uploads."),
            "Unsupported document file type. Use PDF files only." => T("Net PDF files word ondersteun.", "Only PDF files are supported."),
            _ => T("Die hulpbron upload het misluk. Probeer asseblief weer.", "The resource upload failed. Please try again.")
        };

    private string? TranslateServiceMessage(string? message) =>
        message switch
        {
            "Jy het nie admin toegang nie." => T("Jy het nie admin toegang nie.", "You do not have admin access."),
            "Resource type slug is ongeldig." => T("Die hulpbron tipe slug is ongeldig.", "The resource type slug is invalid."),
            "Resource type naam is verpligtend." => T("Die hulpbron tipe naam is verpligtend.", "The resource type name is required."),
            "Supabase URL is nog nie opgestel nie." => T("Supabase URL is nog nie opgestel nie.", "Supabase URL is not configured yet."),
            "Supabase ServiceRoleKey is nog nie opgestel nie." => T("Supabase ServiceRoleKey is nog nie opgestel nie.", "Supabase ServiceRoleKey is not configured yet."),
            "Resource type slug bestaan reeds." => T("Die hulpbron tipe slug bestaan reeds.", "The resource type slug already exists."),
            "Kon nie resource type skep nie." => T("Kon nie hulpbron tipe skep nie.", "Could not create the resource type."),
            "Kon nie resource type nou opdateer nie." => T("Kon nie hulpbron tipe nou opdateer nie.", "Could not update the resource type right now."),
            "Kies asseblief 'n geldige resource type." => T("Kies asseblief 'n geldige hulpbron tipe.", "Please choose a valid resource type."),
            "Kon nie resource type nou verwyder nie." => T("Kon nie hulpbron tipe nou verwyder nie.", "Could not delete the resource type right now."),
            "Kies asseblief 'n geldige hulpbron tipe." => T("Kies asseblief 'n geldige hulpbron tipe.", "Please choose a valid resource type."),
            "Resource document slug is ongeldig." => T("Die hulpbron dokument slug is ongeldig.", "The resource document slug is invalid."),
            "Resource document titel is verpligtend." => T("Die hulpbron dokument titel is verpligtend.", "The resource document title is required."),
            "Resource document file moet 'n PDF wees." => T("Die hulpbron dokument moet 'n PDF wees.", "The resource document must be a PDF."),
            "Resource document storage provider moet 'r2' wees." => T("Die hulpbron dokument storage provider moet 'r2' wees.", "The resource document storage provider must be 'r2'."),
            "Resource document storage metadata ontbreek." => T("Die hulpbron dokument se storage metadata ontbreek.", "The resource document storage metadata is missing."),
            "Resource document slug bestaan reeds." => T("Die hulpbron dokument slug bestaan reeds.", "The resource document slug already exists."),
            "Kon nie resource document skep nie." => T("Kon nie hulpbron dokument skep nie.", "Could not create the resource document."),
            "Kies asseblief 'n geldige resource document." => T("Kies asseblief 'n geldige hulpbron dokument.", "Please choose a valid resource document."),
            "Kon nie resource document nou verwyder nie." => T("Kon nie hulpbron dokument nou verwyder nie.", "Could not delete the resource document right now."),
            _ => null
        };

    private string T(string afrikaans, string english) =>
        string.Equals(CurrentLanguageCode, "en", StringComparison.OrdinalIgnoreCase) ? english : afrikaans;

    private static string BuildEffectiveSlug(string? slug, string? name)
    {
        var source = string.IsNullOrWhiteSpace(slug) ? name : slug;
        if (string.IsNullOrWhiteSpace(source))
        {
            return string.Empty;
        }

        var normalized = source.Trim().ToLowerInvariant();
        normalized = System.Text.RegularExpressions.Regex.Replace(normalized, "[^a-z0-9]+", "-");
        normalized = System.Text.RegularExpressions.Regex.Replace(normalized, "-{2,}", "-");
        return normalized.Trim('-');
    }

    private static string BuildDocumentTitle(string fileName)
    {
        var title = Path.GetFileNameWithoutExtension(fileName)?.Replace('_', ' ').Trim();
        return string.IsNullOrWhiteSpace(title) ? "PDF" : title;
    }

    private static string BuildUniqueDocumentSlug(string title, HashSet<string> usedSlugs)
    {
        var baseSlug = BuildEffectiveSlug(null, title);
        if (string.IsNullOrWhiteSpace(baseSlug))
        {
            baseSlug = "dokument";
        }

        var slug = baseSlug;
        var suffix = 2;
        while (!usedSlugs.Add(slug))
        {
            slug = $"{baseSlug}-{suffix}";
            suffix++;
        }

        return slug;
    }

    private static string NormalizePdfContentType(IBrowserFile file) =>
        file.Name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
            ? "application/pdf"
            : (string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType);

    private sealed class EditableResourceType
    {
        public Guid? ResourceTypeId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Slug { get; set; } = string.Empty;
        public string? Description { get; set; }
        public int SortOrder { get; set; }
        public bool IsEnabled { get; set; } = true;
        public int DocumentCount { get; set; }
        public string SlugPreview => string.IsNullOrWhiteSpace(Slug) ? BuildEffectiveSlug(null, Name) : Slug;

        public static EditableResourceType CreateEmpty(int sortOrder) =>
            new()
            {
                SortOrder = sortOrder,
                IsEnabled = true
            };

        public static EditableResourceType From(AdminResourceTypeRecord record) =>
            new()
            {
                ResourceTypeId = record.ResourceTypeId,
                Name = record.Name,
                Slug = record.Slug,
                Description = record.Description,
                SortOrder = record.SortOrder,
                IsEnabled = record.IsEnabled,
                DocumentCount = record.DocumentCount
            };
    }
}
