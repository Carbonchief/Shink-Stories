using Foundation;
using ImageIO;
using ObjCRuntime;
using Shink.Mobile.Services;
using UserNotifications;

namespace Shink.Mobile.NotificationService;

[Register("SchinkNotificationService")]
public class NotificationService : UNNotificationServiceExtension
{
    private Action<UNNotificationContent>? _handler;
    private UNNotificationContent? _original;
    private readonly CancellationTokenSource _lifetime = new();

    protected NotificationService(NativeHandle handle) : base(handle) { }

    public override void DidReceiveNotificationRequest(UNNotificationRequest request, Action<UNNotificationContent> contentHandler)
    {
        _original = request.Content;
        _handler = contentHandler;
        _ = DeliverAsync(request.Content);
    }

    public override void TimeWillExpire()
    {
        _lifetime.Cancel();
        if (_original is { } content) Finish(content);
    }

    private async Task DeliverAsync(UNNotificationContent original)
    {
        UNNotificationContent bestAttempt = original;
        try
        {
            var info = original.UserInfo;
            var bytes = await MobilePushArtworkDownload.TryDownloadAsync(info["notification_type"]?.ToString(),
                info["image_url"]?.ToString(), _lifetime.Token).ConfigureAwait(false);
            if (bytes is null || _lifetime.IsCancellationRequested) return;

            using var data = NSData.FromArray(bytes);
            using var source = CGImageSource.FromData(data);
            using var thumbnail = source?.CreateThumbnail(0, new CGImageThumbnailOptions
            {
                CreateThumbnailFromImageAlways = true, CreateThumbnailWithTransform = true, MaxPixelSize = 512
            });
            if (thumbnail is null) return;

            // PNG preserves transparent portraits and is also supported for covers supplied as WebP.
            using var file = NSUrl.FromFilename(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png"));
            using var destination = CGImageDestination.Create(file, "public.png", 1);
            if (destination is null) return;
            destination.AddImage(thumbnail);
            if (!destination.Close()) return;

            var options = new UNNotificationAttachmentOptions { TypeHint = "public.png", ThumbnailHidden = false };
            var attachment = UNNotificationAttachment.FromIdentifier("artwork", file, options, out _);
            if (attachment is null) return;
            var content = (UNMutableNotificationContent)original.MutableCopy();
            content.Attachments = [attachment];
            bestAttempt = content;
        }
        catch (Exception)
        {
            // Invalid or unavailable artwork must still deliver the original title, body and tap target.
        }
        finally { Finish(bestAttempt); }
    }

    private void Finish(UNNotificationContent content) => Interlocked.Exchange(ref _handler, null)?.Invoke(content);
}
