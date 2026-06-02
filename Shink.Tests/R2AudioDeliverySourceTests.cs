using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shink.Services;
using System.Runtime.CompilerServices;

namespace Shink.Tests;

[TestClass]
public class R2AudioDeliverySourceTests
{
    [TestMethod]
    public void R2AudioPlaybackRedirectsToSignedReadUrlAfterAuthorization()
    {
        var program = File.ReadAllText(GetRepoPath("Shink", "Program.cs"));

        StringAssert.Contains(program, "audioAccessService.IsTokenValid(slug, token)");
        StringAssert.Contains(program, "HasRequiredStoryAccessAsync(");
        StringAssert.Contains(program, "ApplyAudioResponseSecurityHeaders(httpContext);");
        StringAssert.Contains(program, "CreateAudioReadUrlAsync(");
        StringAssert.Contains(program, "Results.Redirect(");
        StringAssert.Contains(program, ".RequireRateLimiting(\"audio-stream\")");
        Assert.AreEqual(0, CountOccurrences(program, "ProxyAudioFromOriginAsync("));
    }

    [TestMethod]
    public void R2StorageServiceCreatesSignedAudioGetUrls()
    {
        var storageInterface = File.ReadAllText(GetRepoPath("Shink", "Services", "IStoryMediaStorageService.cs"));
        var storageService = File.ReadAllText(GetRepoPath("Shink", "Services", "CloudflareR2StoryMediaStorageService.cs"));

        StringAssert.Contains(storageInterface, "CreateAudioReadUrlAsync(");
        StringAssert.Contains(storageService, "CreateAudioReadUrlAsync(");
        StringAssert.Contains(storageService, "Verb = HttpVerb.GET");
        StringAssert.Contains(storageService, "GetPreSignedURL(request)");
    }

    [TestMethod]
    public void ContentSecurityPolicyAllowsSignedR2AudioRedirects()
    {
        var program = File.ReadAllText(GetRepoPath("Shink", "Program.cs"));

        StringAssert.Contains(program, "var cloudflareR2Options = app.Services.GetRequiredService<IOptions<CloudflareR2Options>>().Value;");
        StringAssert.Contains(program, "BuildContentSecurityPolicy(httpContext.Request, app.Environment.IsDevelopment(), postHogHostUrl, cspNonce, cloudflareR2Options)");
        StringAssert.Contains(program, "var mediaSources = BuildMediaSources(cloudflareR2Options);");
        StringAssert.Contains(program, "media-src {mediaSources}");
        StringAssert.Contains(program, "r2.cloudflarestorage.com");
    }

    [TestMethod]
    public async Task R2StorageServiceSignsLegacyPublicHostAudioKeysWithoutContactingAzure()
    {
        using var storageService = CreateStorageService();

        var signedUri = await storageService.CreateAudioReadUrlAsync(
            "media.prioritybit.co.za",
            "uploaded/stories/audio/demo.mp3",
            TimeSpan.FromMinutes(30));

        Assert.IsNotNull(signedUri);
        Assert.AreEqual("https", signedUri.Scheme);
        StringAssert.Contains(signedUri.Host, "r2.cloudflarestorage.com");
        StringAssert.Contains(signedUri.AbsoluteUri, "X-Amz-Signature=");
        StringAssert.Contains(signedUri.AbsolutePath, "/schink-test-media/uploaded/stories/audio/demo.mp3");
    }

    [TestMethod]
    public async Task R2StorageServiceSignsReadUrlsEvenWhenMediaRequestWasCanceled()
    {
        using var storageService = CreateStorageService();
        using var canceledRequest = new CancellationTokenSource();
        await canceledRequest.CancelAsync();

        var signedUri = await storageService.CreateAudioReadUrlAsync(
            "media.prioritybit.co.za",
            "uploaded/stories/audio/demo.mp3",
            TimeSpan.FromMinutes(30),
            canceledRequest.Token);

        Assert.IsNotNull(signedUri);
        StringAssert.Contains(signedUri.AbsoluteUri, "X-Amz-Signature=");
    }

    [TestMethod]
    public async Task R2StorageServiceRejectsExternalOrTraversalAudioKeys()
    {
        using var storageService = CreateStorageService();

        var externalUri = await storageService.CreateAudioReadUrlAsync(
            null,
            "https://example.com/audio.mp3",
            TimeSpan.FromMinutes(30));
        var traversalUri = await storageService.CreateAudioReadUrlAsync(
            null,
            "../audio.mp3",
            TimeSpan.FromMinutes(30));

        Assert.IsNull(externalUri);
        Assert.IsNull(traversalUri);
    }

    private static int CountOccurrences(string value, string search)
    {
        var count = 0;
        var startIndex = 0;
        while (true)
        {
            var index = value.IndexOf(search, startIndex, StringComparison.Ordinal);
            if (index < 0)
            {
                return count;
            }

            count++;
            startIndex = index + search.Length;
        }
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

    private static CloudflareR2StoryMediaStorageService CreateStorageService() =>
        new(
            Options.Create(new CloudflareR2Options
            {
                PublicBaseUrl = "media.prioritybit.co.za",
                AccountId = "0123456789abcdef0123456789abcdef",
                BucketName = "schink-test-media",
                AccessKeyId = "test-access-key",
                SecretAccessKey = "test-secret-key"
            }),
            NullLogger<CloudflareR2StoryMediaStorageService>.Instance);
}
