using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace Shink.Services;

/// <summary>
/// Checks the bytes MapStaticAssets will send against its publish-time metadata.
/// Run after publishing, before copying or activating an application package.
/// </summary>
public static class PublishedStaticAssetValidator
{
    public static void Validate(string publishDirectory, string applicationName = "Shink")
    {
        var root = Path.GetFullPath(publishDirectory);
        var webRoot = Path.Combine(root, "wwwroot") + Path.DirectorySeparatorChar;
        var manifestPath = Path.Combine(root, $"{applicationName}.staticwebassets.endpoints.json");
        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        if (manifest.RootElement.GetProperty("ManifestType").GetString() != "Publish")
        {
            throw new InvalidDataException("Static asset verification requires a publish manifest.");
        }

        var actualFiles = new Dictionary<string, (long Length, string Hash)>(StringComparer.Ordinal);
        foreach (var endpoint in manifest.RootElement.GetProperty("Endpoints").EnumerateArray())
        {
            var assetPath = endpoint.GetProperty("AssetFile").GetString()
                ?? throw new InvalidDataException("A static asset has no file path.");
            var physicalPath = Path.GetFullPath(Path.Combine(webRoot, assetPath));
            if (!physicalPath.StartsWith(webRoot, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Static asset is outside wwwroot: {assetPath}");
            }

            if (!actualFiles.TryGetValue(physicalPath, out var actual))
            {
                using var stream = File.OpenRead(physicalPath);
                actual = (stream.Length, Convert.ToBase64String(SHA256.HashData(stream)));
                actualFiles.Add(physicalPath, actual);
            }

            string? contentLength = null;
            string? strongEtag = null;
            foreach (var header in endpoint.GetProperty("ResponseHeaders").EnumerateArray())
            {
                var name = header.GetProperty("Name").GetString();
                var value = header.GetProperty("Value").GetString();
                if (string.Equals(name, "Content-Length", StringComparison.OrdinalIgnoreCase))
                {
                    contentLength = value;
                }
                else if (string.Equals(name, "ETag", StringComparison.OrdinalIgnoreCase) &&
                         value is not null && !value.StartsWith("W/", StringComparison.OrdinalIgnoreCase))
                {
                    strongEtag = value;
                }
            }

            if (!long.TryParse(contentLength, NumberStyles.None, CultureInfo.InvariantCulture, out var expectedLength) ||
                expectedLength != actual.Length || strongEtag != $"\"{actual.Hash}\"")
            {
                throw new InvalidDataException(
                    $"Published static asset does not match its manifest: {assetPath}. " +
                    "Clean and republish the application; do not deploy mixed build output.");
            }
        }
    }
}
