using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shink.Services;

namespace Shink.Tests;

[TestClass]
public sealed class PublishedStaticAssetValidatorTests
{
    [TestMethod]
    public void ValidatesIdentityFingerprintsAndBothCompressedRepresentations()
    {
        using var fixture = new AssetFixture();
        var identity = Encoding.UTF8.GetBytes("body { color: teal; }" + new string(' ', 7100));
        fixture.AddAsset("app.css", identity);
        fixture.AddEndpoint("app.abc123.css", "app.css", identity);
        fixture.AddAsset("app.css.gz", Compress(identity, brotli: false));
        fixture.AddAsset("app.css.br", Compress(identity, brotli: true));
        fixture.WriteManifest();

        PublishedStaticAssetValidator.Validate(fixture.Root);
    }

    [TestMethod]
    [DataRow("short")]
    [DataRow("a much longer replacement file")]
    [DataRow("same size")]
    public void RejectsChangedBytesEvenWhenLengthIsUnchanged(string replacement)
    {
        using var fixture = new AssetFixture();
        fixture.AddAsset("app.js", Encoding.UTF8.GetBytes("original!"));
        fixture.WriteManifest();
        File.WriteAllText(Path.Combine(fixture.Root, "wwwroot", "app.js"), replacement);

        var error = Assert.ThrowsExactly<InvalidDataException>(() => PublishedStaticAssetValidator.Validate(fixture.Root));
        StringAssert.Contains(error.Message, "app.js");
        StringAssert.Contains(error.Message, "Clean and republish");
    }

    [TestMethod]
    public void RejectsChangedCompressedAsset()
    {
        using var fixture = new AssetFixture();
        fixture.AddAsset("app.js.gz", Compress(Encoding.UTF8.GetBytes("original"), brotli: false));
        fixture.WriteManifest();
        File.WriteAllBytes(Path.Combine(fixture.Root, "wwwroot", "app.js.gz"), [1, 2, 3]);

        Assert.ThrowsExactly<InvalidDataException>(() => PublishedStaticAssetValidator.Validate(fixture.Root));
    }

    [TestMethod]
    public void RejectsMissingAsset()
    {
        using var fixture = new AssetFixture();
        fixture.AddEndpoint("missing.css", "missing.css", []);
        fixture.WriteManifest();

        Assert.ThrowsExactly<FileNotFoundException>(() => PublishedStaticAssetValidator.Validate(fixture.Root));
    }

    [TestMethod]
    public void RejectsBuildManifestAndOutOfRootAsset()
    {
        using var fixture = new AssetFixture();
        fixture.WriteManifest("Build");
        Assert.ThrowsExactly<InvalidDataException>(() => PublishedStaticAssetValidator.Validate(fixture.Root));

        fixture.AddEndpoint("invalid", "../outside.txt", []);
        fixture.WriteManifest();
        Assert.ThrowsExactly<InvalidDataException>(() => PublishedStaticAssetValidator.Validate(fixture.Root));
    }

    [TestMethod]
    public async Task ReproducesManifestLengthCrashAndValidatedRestartRestoresGetHeadAndRanges()
    {
        using var fixture = new AssetFixture();
        // Production logged count=7065 while the physical file was shorter.
        var publishedBytes = Encoding.UTF8.GetBytes(new string('x', 7065));
        fixture.AddAsset("app.css", publishedBytes);
        fixture.WriteManifest();
        File.WriteAllText(Path.Combine(fixture.Root, "wwwroot", "app.css"), "short");

        await using (var mismatchedHost = await StartHostAsync(fixture.Root))
        {
            using var client = new HttpClient { BaseAddress = new Uri(mismatchedHost.Urls.Single()) };
            using var failed = await client.GetAsync("/app.css");
            Assert.AreEqual(System.Net.HttpStatusCode.InternalServerError, failed.StatusCode);
            Assert.ThrowsExactly<InvalidDataException>(() => PublishedStaticAssetValidator.Validate(fixture.Root));
        }

        // Never replace files under an active process; validate a consistent output
        // then restart so the manifest and cached file metadata are loaded together.
        File.WriteAllBytes(Path.Combine(fixture.Root, "wwwroot", "app.css"), publishedBytes);
        PublishedStaticAssetValidator.Validate(fixture.Root);
        await using var repairedHost = await StartHostAsync(fixture.Root);
        using var repairedClient = new HttpClient { BaseAddress = new Uri(repairedHost.Urls.Single()) };
        using var full = await repairedClient.GetAsync("/app.css");
        Assert.AreEqual(System.Net.HttpStatusCode.OK, full.StatusCode);
        CollectionAssert.AreEqual(publishedBytes, await full.Content.ReadAsByteArrayAsync());
        using var head = await repairedClient.SendAsync(new HttpRequestMessage(HttpMethod.Head, "/app.css"));
        Assert.AreEqual(System.Net.HttpStatusCode.OK, head.StatusCode);
        using var rangeRequest = new HttpRequestMessage(HttpMethod.Get, "/app.css");
        rangeRequest.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 9);
        using var range = await repairedClient.SendAsync(rangeRequest);
        Assert.AreEqual(System.Net.HttpStatusCode.PartialContent, range.StatusCode);
        Assert.HasCount(10, await range.Content.ReadAsByteArrayAsync());
    }

    private static async Task<WebApplication> StartHostAsync(string root)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ContentRootPath = root,
            WebRootPath = Path.Combine(root, "wwwroot"),
            EnvironmentName = Environments.Production
        });
        builder.Configuration["ReloadStaticAssetsAtRuntime"] = "false";
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();
        app.UseExceptionHandler(handler => handler.Run(context =>
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            return Task.CompletedTask;
        }));
        // The existing physical fallback is skipped for a selected static endpoint.
        app.UseStaticFiles();
        app.MapStaticAssets(Path.Combine(root, "Shink.staticwebassets.endpoints.json"));
        await app.StartAsync();
        return app;
    }

    private static byte[] Compress(byte[] bytes, bool brotli)
    {
        using var output = new MemoryStream();
        using (Stream compressor = brotli
                   ? new BrotliStream(output, CompressionMode.Compress, leaveOpen: true)
                   : new GZipStream(output, CompressionMode.Compress, leaveOpen: true))
        {
            compressor.Write(bytes);
        }

        return output.ToArray();
    }

    private sealed class AssetFixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), $"shink-static-validation-{Guid.NewGuid():N}");
        private readonly List<object> _endpoints = [];

        public AssetFixture() => Directory.CreateDirectory(Path.Combine(Root, "wwwroot"));

        public void AddAsset(string assetPath, byte[] content)
        {
            File.WriteAllBytes(Path.Combine(Root, "wwwroot", assetPath), content);
            AddEndpoint(assetPath, assetPath, content);
        }

        public void AddEndpoint(string route, string assetPath, byte[] content) => _endpoints.Add(new
        {
            Route = route,
            AssetFile = assetPath,
            Selectors = Array.Empty<object>(),
            ResponseHeaders = new[]
            {
                new { Name = "Content-Length", Value = content.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                new { Name = "ETag", Value = $"\"{Convert.ToBase64String(SHA256.HashData(content))}\"" }
            },
            EndpointProperties = Array.Empty<object>()
        });

        public void WriteManifest(string manifestType = "Publish") => File.WriteAllText(
            Path.Combine(Root, "Shink.staticwebassets.endpoints.json"),
            JsonSerializer.Serialize(new { Version = 1, ManifestType = manifestType, Endpoints = _endpoints }));

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
