using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shink.Utilities;

namespace Shink.Tests;

[TestClass]
public class BlogMediaEmbeddingSourceTests
{
    [TestMethod]
    public void CloudflareVideoHelperAcceptsConfiguredR2AndStreamEmbeds()
    {
        var r2Video = BlogVideoUrlHelper.ResolveCloudflareVideo(
            "https://media.schink.example/uploaded/stories/video/blog.mp4",
            "https://media.schink.example/");
        var streamVideo = BlogVideoUrlHelper.ResolveCloudflareVideo(
            "https://iframe.videodelivery.net/4f106c50ee7e4e4e9e8e81f41f177a8b",
            "https://media.schink.example/");

        Assert.IsNotNull(r2Video);
        Assert.AreEqual(BlogVideoEmbedKind.DirectVideo, r2Video.Kind);
        Assert.IsNotNull(streamVideo);
        Assert.AreEqual(BlogVideoEmbedKind.Iframe, streamVideo.Kind);
    }

    [TestMethod]
    public void CloudflareVideoHelperRejectsOtherHostsAndUnsupportedFiles()
    {
        Assert.IsNull(BlogVideoUrlHelper.ResolveCloudflareVideo(
            "https://attacker.example/video.mp4",
            "https://media.schink.example/"));
        Assert.IsNull(BlogVideoUrlHelper.ResolveCloudflareVideo(
            "https://media.schink.example/video.mov",
            "https://media.schink.example/"));
    }

    [TestMethod]
    public void AdminEditorProvidesBilingualCursorBasedMediaInsertion()
    {
        var component = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "AdminBlogPanel.razor"));
        var javascript = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "AdminBlogPanel.razor.js"));

        StringAssert.Contains(component, "Prent in die inhoud");
        StringAssert.Contains(component, "Image in the content");
        StringAssert.Contains(component, "YouTube-video");
        StringAssert.Contains(component, "YouTube video");
        StringAssert.Contains(component, "Cloudflare-video");
        StringAssert.Contains(component, "Cloudflare video");
        StringAssert.Contains(component, "UploadAndInsertInlineImageAsync");
        StringAssert.Contains(component, "UploadAndInsertCloudflareVideoAsync");
        StringAssert.Contains(component, "InsertExistingCloudflareVideoAsync");
        StringAssert.Contains(javascript, "export async function insertBlogImage");
        StringAssert.Contains(javascript, "export async function insertBlogVideo");
        StringAssert.Contains(javascript, "state.lastSelectionIndex");
        StringAssert.Contains(javascript, "export async function uploadSelectedFileToR2");
    }

    [TestMethod]
    public void BlogVideoUploadUsesPresignedCloudflareR2Put()
    {
        var storageInterface = File.ReadAllText(GetRepoPath("Shink", "Services", "IStoryMediaStorageService.cs"));
        var storage = File.ReadAllText(GetRepoPath("Shink", "Services", "CloudflareR2StoryMediaStorageService.cs"));

        StringAssert.Contains(storageInterface, "CreateVideoDirectUploadAsync");
        StringAssert.Contains(storage, "\"uploaded/stories/video\"");
        StringAssert.Contains(storage, "ResolveVideoContentType");
        StringAssert.Contains(storage, "includePublicUrl: true");
    }

    [TestMethod]
    public void BlogMediaIsResponsiveAndAllowedByContentSecurityPolicy()
    {
        var publicCss = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "BlogPost.razor.css"));
        var adminCss = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "AdminBlogPanel.razor.css"));
        var program = File.ReadAllText(GetRepoPath("Shink", "Program.cs"));

        StringAssert.Contains(publicCss, "figure.blog-media-video");
        StringAssert.Contains(publicCss, "aspect-ratio: 16 / 9;");
        StringAssert.Contains(publicCss, "object-fit: contain;");
        StringAssert.Contains(adminCss, ".blog-admin-media-tools");
        StringAssert.Contains(adminCss, "grid-template-columns: repeat(3, minmax(0, 1fr));");
        StringAssert.Contains(program, "https://iframe.videodelivery.net");
        StringAssert.Contains(program, "https://*.cloudflarestream.com");
        StringAssert.Contains(program, "TryGetCspHostOrigin(cloudflareR2Options.PublicBaseUrl)");
    }

    private static string GetRepoPath(params string[] segments)
    {
        var parts = new[]
        {
            Path.GetDirectoryName(GetSourceFilePath())!,
            ".."
        }.Concat(segments).ToArray();

        return Path.GetFullPath(Path.Combine(parts));
    }

    private static string GetSourceFilePath([CallerFilePath] string path = "") => path;
}
