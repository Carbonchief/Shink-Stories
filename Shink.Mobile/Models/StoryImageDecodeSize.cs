namespace Shink.Mobile.Models;

internal static class StoryImageDecodeSize
{
    // Account for cropping: a landscape cover filling a portrait card needs
    // more pixels along its long edge than the card's longest edge alone.
    public static int Resolve(int sourceWidth, int sourceHeight, int targetWidth, int targetHeight, bool aspectFill)
    {
        var longestEdge = Math.Max(sourceWidth, sourceHeight);
        if (sourceWidth <= 0 || sourceHeight <= 0)
        {
            return 1280;
        }

        if (targetWidth <= 0 || targetHeight <= 0)
        {
            return longestEdge;
        }

        var widthScale = (double)targetWidth / sourceWidth;
        var heightScale = (double)targetHeight / sourceHeight;
        var scale = aspectFill ? Math.Max(widthScale, heightScale) : Math.Min(widthScale, heightScale);
        return Math.Clamp((int)Math.Ceiling(longestEdge * Math.Min(1, scale)), 1, longestEdge);
    }
}
