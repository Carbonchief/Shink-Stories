using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Shink.Tests;

[TestClass]
public class AdminResourceEditSourceTests
{
    [TestMethod]
    public void AdminResourcesExposeDocumentEditingWithoutPublishNotifications()
    {
        var panelMarkup = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "AdminResourceTypesPanel.razor"));
        var panelCode = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "AdminResourceTypesPanel.razor.cs"));
        var panelCss = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "AdminResourceTypesPanel.razor.css"));
        var serviceContract = File.ReadAllText(GetRepoPath("Shink", "Services", "IAdminManagementService.cs"));
        var service = File.ReadAllText(GetRepoPath("Shink", "Services", "SupabaseAdminManagementService.cs"));

        StringAssert.Contains(serviceContract, "UpdateResourceDocumentAsync");
        StringAssert.Contains(serviceContract, "AdminResourceDocumentUpdateRequest");
        StringAssert.Contains(panelMarkup, "BeginEditDocument(document)");
        StringAssert.Contains(panelMarkup, "SaveEditingDocumentAsync");
        StringAssert.Contains(panelMarkup, "@bind=\"EditingDocument.Title\"");
        StringAssert.Contains(panelMarkup, "@bind=\"EditingDocument.Slug\"");
        StringAssert.Contains(panelMarkup, "@bind=\"EditingDocument.Description\"");
        StringAssert.Contains(panelMarkup, "@bind=\"EditingDocument.SortOrder\"");
        StringAssert.Contains(panelMarkup, "@bind=\"EditingDocument.IsEnabled\"");
        StringAssert.Contains(panelCode, "EditableResourceDocument.From");
        StringAssert.Contains(panelCode, "new AdminResourceDocumentUpdateRequest");
        StringAssert.Contains(panelCss, ".resource-types-admin-document-edit-form");

        var updateStart = service.IndexOf("public async Task<AdminOperationResult> UpdateResourceDocumentAsync", StringComparison.Ordinal);
        var deleteStart = service.IndexOf("public async Task<AdminOperationResult> DeleteResourceDocumentAsync", StringComparison.Ordinal);
        Assert.AreNotEqual(-1, updateStart, "The Supabase admin service should expose a resource document update method.");
        Assert.IsLessThan(deleteStart, updateStart, "The update method should be placed before delete for this source guard.");

        var updateMethod = service[updateStart..deleteStart];
        StringAssert.Contains(updateMethod, "new HttpMethod(\"PATCH\")");
        StringAssert.Contains(updateMethod, "resource_documents?resource_document_id=eq.");
        Assert.IsFalse(
            updateMethod.Contains("CreatePublishedResourceDocumentNotificationsAsync", StringComparison.Ordinal),
            "Editing an existing resource must not create published resource notifications.");
    }

    private static string GetRepoPath(params string[] segments)
    {
        var parts = new[]
        {
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            ".."
        }.Concat(segments).ToArray();

        return Path.GetFullPath(Path.Combine(parts));
    }
}
