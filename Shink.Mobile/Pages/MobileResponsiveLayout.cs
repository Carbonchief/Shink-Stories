namespace Shink.Mobile.Pages;

internal static class MobileResponsiveLayout
{
    private const double WideLayoutBreakpoint = 600;
    private const double ThreeColumnStoryBreakpoint = 960;

    public static double ResolveWidth(double width)
    {
        if (width > 0)
        {
            return width;
        }

        var display = DeviceDisplay.MainDisplayInfo;
        return display.Density <= 0 ? 0 : display.Width / display.Density;
    }

    public static bool IsWide(double width) => ResolveWidth(width) >= WideLayoutBreakpoint;

    public static int ResolveStoryGridColumns(double width)
    {
        var availableWidth = ResolveWidth(width);
        if (!IsWide(availableWidth))
        {
            return 1;
        }

        return availableWidth >= ThreeColumnStoryBreakpoint ? 3 : 2;
    }

    public static double ResolveStoryCardArtworkHeight(double width, int columns)
    {
        if (!IsWide(width))
        {
            return 172;
        }

        var availableWidth = Math.Max(320, ResolveWidth(width) - 40);
        var columnWidth = (availableWidth - (Math.Max(1, columns) - 1) * 14) / Math.Max(1, columns);
        return Math.Clamp(columnWidth * 0.68, 184, 228);
    }

    public static double ResolveHomePreviewCardWidth(double width) =>
        IsWide(width)
            ? Math.Clamp((ResolveWidth(width) - 56) / 3.8, 220, 260)
            : 180;

    public static double ResolveHomePreviewImageHeight(double width) =>
        IsWide(width)
            ? Math.Clamp(ResolveHomePreviewCardWidth(width) * 0.66, 145, 172)
            : 120;

    public static int ResolveCharacterGridSpan(double width)
    {
        var availableWidth = Math.Max(320, ResolveWidth(width) - 20);
        return availableWidth switch
        {
            >= 1160 => 5,
            >= 820 => 4,
            >= 560 => 3,
            _ => 2
        };
    }

    public static double ResolveCharacterMediaSize(double width, int span)
    {
        var availableWidth = Math.Max(320, ResolveWidth(width) - 20);
        var cardWidth = (availableWidth - (Math.Max(1, span) - 1) * 10) / Math.Max(1, span);
        return Math.Clamp(cardWidth - 20, 132, IsWide(availableWidth) ? 220 : 190);
    }

    public static void ApplyCenteredContent(View view, double width, double maximumWidth)
    {
        var availableWidth = ResolveWidth(width);
        if (IsWide(availableWidth))
        {
            view.MaximumWidthRequest = Math.Min(maximumWidth, Math.Max(320, availableWidth - 48));
            view.HorizontalOptions = LayoutOptions.Center;
            return;
        }

        view.MaximumWidthRequest = double.PositiveInfinity;
        view.HorizontalOptions = LayoutOptions.Fill;
    }

    public static double ResolveStoryCarouselCardWidth(double width, bool isAndroid)
    {
        var availableWidth = Math.Max(280, ResolveWidth(width) - 36);
        if (!IsWide(availableWidth))
        {
            const double visibleCards = 7d / 3d;
            const double itemSpacing = 14d;
            var targetWidth = (availableWidth - (itemSpacing * 2)) / visibleCards;
            return Math.Clamp(targetWidth, isAndroid ? 126 : 132, isAndroid ? 148 : 168);
        }

        var wideTargetWidth = (availableWidth - (14 * 3)) / 3.6;
        return Math.Clamp(wideTargetWidth, 176, 230);
    }

    public static double ResolvePlaylistCarouselCardWidth(double width, bool isAndroid)
    {
        if (!IsWide(width))
        {
            return isAndroid ? 226 : 246;
        }

        var availableWidth = Math.Max(320, ResolveWidth(width) - 48);
        return Math.Clamp((availableWidth - 56) / 3.25, 230, 300);
    }

    public static double ResolveDownloadedCardWidth(double width, bool isAndroid) =>
        IsWide(width)
            ? Math.Clamp((Math.Max(320, ResolveWidth(width) - 48)) / 3.8, 180, 240)
            : isAndroid ? 148 : 168;
}
