namespace Shink.Services;

// A marker contains no payload, token or subscriber details. All worker
// instances for a queue must use the same persistent directory.
internal sealed class MobilePushReceiptJournal(string directory)
{
    public static MobilePushReceiptJournal Open(MobilePushOptions settings)
    {
        var path = settings.ReceiptDirectory;
        if (string.IsNullOrWhiteSpace(path))
        {
            var data = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME")))
            {
                var home = OperatingSystem.IsWindows() ? Environment.GetEnvironmentVariable("HOME") : "/home";
                if (string.IsNullOrWhiteSpace(home)) throw new IOException("App Service content directory is unavailable.");
                data = Path.Combine(home, "data");
            }
            path = Path.Combine(data, "Schink", "push-receipts");
        }
        Directory.CreateDirectory(path);
        return new(path);
    }

    public bool HasReceipt(MobilePushDelivery delivery)
    {
        // File.Exists also returns false on I/O errors. Only a missing marker
        // permits sending. An unreadable journal must leave the claim for retry.
        try { File.GetAttributes(PathFor(delivery)); return true; }
        catch (FileNotFoundException) { return false; }
    }

    public void Record(MobilePushDelivery delivery)
    {
        using var marker = new FileStream(PathFor(delivery), FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite);
        marker.Flush(flushToDisk: true);
    }

    public void Remove(MobilePushDelivery delivery) => File.Delete(PathFor(delivery));

    public void Prune()
    {
        var cutoff = DateTime.UtcNow.AddDays(-2); // Dispatch expires after 24 hours.
        foreach (var path in Directory.EnumerateFiles(directory, "*.accepted"))
            if (File.GetLastWriteTimeUtc(path) < cutoff) File.Delete(path);
    }

    private string PathFor(MobilePushDelivery delivery) =>
        Path.Combine(directory, $"{delivery.DeliveryId:N}.{delivery.RegistrationId:N}.accepted");
}
