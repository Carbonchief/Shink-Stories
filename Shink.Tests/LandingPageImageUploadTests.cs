using System.Reflection;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shink.Components.Pages;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Shink.Tests;

[TestClass]
public class LandingPageImageUploadTests
{
    [TestMethod]
    public async Task ImageUpload_UsesDecodedFormatInsteadOfClaimedNameOrMimeType()
    {
        using var image = new Image<Rgba32>(3, 2);
        await using var source = new MemoryStream();
        await image.SaveAsPngAsync(source);
        var result = await ValidateAsync(new TestBrowserFile(source.ToArray(), "pretend.jpg", "image/jpeg"));
        await using var content = (MemoryStream)Property(result, "Content");

        Assert.AreEqual("image/png", Property(result, "ContentType"));
        Assert.AreEqual(".png", Property(result, "Extension"));
        Assert.AreEqual(0L, content.Position);
        using var decoded = await Image.LoadAsync(content);
        Assert.AreEqual(3, decoded.Width);
        Assert.AreEqual(2, decoded.Height);
    }

    [TestMethod]
    public async Task ImageUpload_RejectsUnsupportedRasterFormat()
    {
        using var image = new Image<Rgba32>(2, 2);
        await using var source = new MemoryStream();
        await image.SaveAsGifAsync(source);
        var error = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            ValidateAsync(new TestBrowserFile(source.ToArray(), "pretend.png", "image/png")));
        Assert.AreEqual("unsupported_image_format", error.Message);
    }

    [TestMethod]
    public async Task ImageUpload_RejectsNonImageEvenWhenClaimedAsPng()
    {
        await Assert.ThrowsAsync<Exception>(() => ValidateAsync(new TestBrowserFile(
            System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'><script>alert(1)</script></svg>"),
            "pretend.png", "image/png")));
    }

    [TestMethod]
    public async Task ImageUpload_RejectsHugeDimensionsBeforeDecodingPixels()
    {
        using var image = new Image<Rgba32>(1, 1);
        await using var source = new MemoryStream();
        await image.SaveAsPngAsync(source);
        var bytes = source.ToArray();
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16, 4), 10_000);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(20, 4), 10_000);
        uint crc = uint.MaxValue;
        for (var offset = 12; offset < 29; offset++)
        {
            crc ^= bytes[offset];
            for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0u : 0xedb88320u);
        }
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(29, 4), ~crc);

        var error = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            ValidateAsync(new TestBrowserFile(bytes, "huge.png", "image/png")));
        Assert.AreEqual("image_dimensions_invalid", error.Message);
    }

    [TestMethod]
    public async Task ImageUpload_EnforcesTenMegabyteReadLimit()
    {
        var file = new TestBrowserFile([1, 2, 3], "big.png", "image/png", 10 * 1024 * 1024 + 1L);
        await Assert.ThrowsExactlyAsync<IOException>(() => ValidateAsync(file));
        Assert.AreEqual(10 * 1024 * 1024L, file.RequestedLimit);
    }

    private static object Property(object target, string name) => target.GetType().GetProperty(name)!.GetValue(target)!;

    private static async Task<object> ValidateAsync(IBrowserFile file)
    {
        var method = typeof(AdminLandingPagesPanel).GetMethod("ValidateAndNormalizeImageAsync", BindingFlags.NonPublic | BindingFlags.Static)!;
        var task = (Task)method.Invoke(null, [file, CancellationToken.None])!;
        await task;
        return task.GetType().GetProperty("Result")!.GetValue(task)!;
    }

    private sealed class TestBrowserFile(byte[] bytes, string name, string contentType, long? size = null) : IBrowserFile
    {
        public string Name => name;
        public DateTimeOffset LastModified => DateTimeOffset.UtcNow;
        public long Size => size ?? bytes.Length;
        public string ContentType => contentType;
        public long RequestedLimit { get; private set; }
        public Stream OpenReadStream(long maxAllowedSize = 512_000, CancellationToken cancellationToken = default)
        {
            RequestedLimit = maxAllowedSize;
            if (Size > maxAllowedSize) throw new IOException("File exceeds the allowed size.");
            return new MemoryStream(bytes, writable: false);
        }
    }
}
