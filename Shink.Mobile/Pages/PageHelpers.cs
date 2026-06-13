using Shink.Mobile.Models;
using Shink.Mobile.Services;

namespace Shink.Mobile.Pages;

internal static class PageHelpers
{
    public static View BuildStoryCard(
        MobileStorySummary story,
        MobileApiClient apiClient,
        Func<MobileStorySummary, Task> onTap,
        Func<MobileStorySummary, Task>? onFavoriteTap = null)
    {
        var imageSource = ResolveStoryCardImageSource(story, apiClient);
        var artwork = new Image
        {
            Source = imageSource,
            Aspect = Aspect.AspectFill,
            HeightRequest = 172
        };
        var lockBadge = new Border
        {
            IsVisible = story.IsLocked,
            BackgroundColor = Color.FromArgb("#D9222222"),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 999 },
            Padding = new Thickness(10, 5),
            Margin = new Thickness(12),
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Start,
            Content = new Label
            {
                Text = "Gesluit",
                TextColor = Colors.White,
                FontSize = 11,
                FontAttributes = FontAttributes.Bold
            }
        };
        var favoriteHeart = BuildFavoriteHeart(story, onFavoriteTap);

        var imageLayer = new Grid
        {
            HeightRequest = 172,
            Children =
            {
                artwork,
                lockBadge,
                favoriteHeart
            }
        };

        var imageFrame = new Border
        {
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 24 },
            HeightRequest = 172,
            Content = imageLayer
        };

        var title = new Label
        {
            Text = story.Title,
            FontSize = 16,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#243238"),
            MaxLines = 2,
            LineBreakMode = LineBreakMode.TailTruncation
        };

        var description = new Label
        {
            Text = story.Description,
            FontSize = 12,
            TextColor = Color.FromArgb("#5F5F5F"),
            MaxLines = 2,
            LineBreakMode = LineBreakMode.TailTruncation
        };

        var listenHint = new Label
        {
            Text = story.IsLocked ? "Maak oop" : "Luister nou",
            FontSize = 12,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#146D69")
        };

        var body = new VerticalStackLayout
        {
            Spacing = 8,
            Children =
            {
                imageFrame,
                title,
                description,
                listenHint
            }
        };

        var frame = new Border
        {
            BackgroundColor = Color.FromArgb("#FFFDF7"),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 28 },
            Padding = 10,
            Margin = new Thickness(0, 0, 0, 8),
            Shadow = new Shadow
            {
                Brush = Brush.Black,
                Offset = new Point(0, 8),
                Radius = 18,
                Opacity = 0.08f
            },
            Content = body
        };

        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) => await onTap(story);
        frame.GestureRecognizers.Add(tap);
        return frame;
    }

    private static View BuildFavoriteHeart(
        MobileStorySummary story,
        Func<MobileStorySummary, Task>? onFavoriteTap)
    {
        var heart = new Border
        {
            IsVisible = onFavoriteTap is not null,
            WidthRequest = 42,
            HeightRequest = 42,
            BackgroundColor = Color.FromArgb("#F7FFFFFF"),
            Stroke = story.IsFavorite ? Color.FromArgb("#FEE4E2") : Color.FromArgb("#D8DED8"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 999 },
            Padding = 0,
            Margin = new Thickness(0, 10, 10, 0),
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Start,
            Shadow = new Shadow
            {
                Brush = Brush.Black,
                Offset = new Point(0, 3),
                Radius = 8,
                Opacity = 0.12f
            },
            Content = new Label
            {
                Text = story.IsFavorite ? "♥" : "♡",
                TextColor = story.IsFavorite ? Color.FromArgb("#E11D48") : Color.FromArgb("#8A938D"),
                FontSize = 24,
                FontAttributes = FontAttributes.Bold,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, -2, 0, 0)
            }
        };

        if (onFavoriteTap is not null)
        {
            var favoriteTap = new TapGestureRecognizer();
            favoriteTap.Tapped += async (_, _) => await onFavoriteTap(story);
            heart.GestureRecognizers.Add(favoriteTap);
        }

        return heart;
    }

    public static Label BuildSectionTitle(string title) =>
        new()
        {
            Text = title,
            FontSize = 24,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#222222"),
            Margin = new Thickness(0, 0, 0, 10)
        };

    internal static string ResolveStoryCardImageSource(MobileStorySummary story, MobileApiClient apiClient)
    {
        if (IsLegacyWebsiteAsset(story.ThumbnailUrl) && !string.IsNullOrWhiteSpace(story.ImageUrl))
        {
            return apiClient.BuildImageUrl(story.ImageUrl);
        }

        return apiClient.BuildImageUrl(string.IsNullOrWhiteSpace(story.ThumbnailUrl)
            ? story.ImageUrl
            : story.ThumbnailUrl);
    }

    private static bool IsLegacyWebsiteAsset(string? imageUrl)
    {
        if (string.IsNullOrWhiteSpace(imageUrl))
        {
            return true;
        }

        var normalized = imageUrl.Trim();
        if (normalized.StartsWith("file://", StringComparison.OrdinalIgnoreCase) &&
            Uri.TryCreate(normalized, UriKind.Absolute, out var fileUri))
        {
            normalized = Uri.UnescapeDataString(fileUri.AbsolutePath);
        }

        return normalized.StartsWith("/stories/", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("/branding/", StringComparison.OrdinalIgnoreCase);
    }
}
