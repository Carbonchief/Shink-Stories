using Shink.Mobile.Models;
using Shink.Mobile.Services;

namespace Shink.Mobile.Pages;

internal static class PageHelpers
{
    internal const string StoryPlaceholderFile = "schink_placeholder.png";

    public static string BuildStoryReturnPath(MobileStorySummary story)
    {
        var source = string.Equals(story.Source, "gratis", StringComparison.OrdinalIgnoreCase)
            ? "gratis"
            : "luister";
        return $"/{source}/{Uri.EscapeDataString(story.Slug)}";
    }

    public static Task OpenPlansForStoryAsync(MobileStorySummary story)
    {
        var returnPath = BuildStoryReturnPath(story);
        return Shell.Current.GoToAsync(
            $"{nameof(PlansPage)}?returnUrl={Uri.EscapeDataString(returnPath)}",
            animate: true);
    }

    public static bool TryBuildStoryDetailRoute(string? returnPath, out string route)
    {
        route = string.Empty;
        if (!TryParseStoryReturnPath(returnPath, out var source, out var slug))
        {
            return false;
        }

        route = $"{nameof(StoryDetailPage)}?slug={Uri.EscapeDataString(slug)}&source={source}";
        return true;
    }

    public static bool TryParseStoryReturnPath(string? returnPath, out string source, out string slug)
    {
        source = string.Empty;
        slug = string.Empty;
        if (string.IsNullOrWhiteSpace(returnPath))
        {
            return false;
        }

        string decodedPath;
        try
        {
            decodedPath = Uri.UnescapeDataString(returnPath.Trim());
        }
        catch
        {
            return false;
        }

        var queryIndex = decodedPath.IndexOf('?');
        if (queryIndex >= 0)
        {
            decodedPath = decodedPath[..queryIndex];
        }

        var segments = decodedPath
            .Trim('/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length != 2 ||
            (segments[0] is not "luister" and not "gratis"))
        {
            return false;
        }

        source = segments[0];
        try
        {
            slug = Uri.UnescapeDataString(segments[1]).Trim();
        }
        catch
        {
            slug = segments[1].Trim();
        }

        return !string.IsNullOrWhiteSpace(slug);
    }

    public static View BuildStoryCollection(
        IReadOnlyList<MobileStorySummary> stories,
        MobileApiClient apiClient,
        Func<MobileStorySummary, Task> onTap,
        double width)
    {
        if (!MobileResponsiveLayout.IsWide(width))
        {
            var stack = new VerticalStackLayout { Spacing = 0 };
            foreach (var story in stories)
            {
                stack.Children.Add(BuildStoryCard(story, apiClient, onTap));
            }

            return stack;
        }

        var columns = MobileResponsiveLayout.ResolveStoryGridColumns(width);
        var artworkHeight = MobileResponsiveLayout.ResolveStoryCardArtworkHeight(width, columns);
        var grid = new Grid
        {
            ColumnSpacing = 14,
            RowSpacing = 14
        };
        for (var column = 0; column < columns; column++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        }

        for (var row = 0; row < (stories.Count + columns - 1) / columns; row++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        }

        for (var index = 0; index < stories.Count; index++)
        {
            var card = BuildStoryCard(stories[index], apiClient, onTap, artworkHeight: artworkHeight);
            grid.Children.Add(card);
            Grid.SetColumn(card, index % columns);
            Grid.SetRow(card, index / columns);
        }

        return grid;
    }

    public static View BuildStoryCard(
        MobileStorySummary story,
        MobileApiClient apiClient,
        Func<MobileStorySummary, Task> onTap,
        Func<MobileStorySummary, Task>? onFavoriteTap = null,
        double? artworkHeight = null)
    {
        var resolvedArtworkHeight = artworkHeight ?? 172;
        var artwork = new ProgressiveCachedImage(
            apiClient,
            BuildStoryImageRequest(story, apiClient, "schink_background.jpeg"))
        {
            Aspect = Aspect.AspectFill,
            HeightRequest = resolvedArtworkHeight
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
            HeightRequest = resolvedArtworkHeight,
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
            HeightRequest = resolvedArtworkHeight,
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
            Shadow = DeviceInfo.Current.Platform == DevicePlatform.Android
                ? null!
                : new Shadow
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
        var heart = MobileFavoriteHeart.CreateButton(story.IsFavorite, 24);
        heart.IsVisible = onFavoriteTap is not null;
        heart.WidthRequest = 42;
        heart.HeightRequest = 42;
        heart.BackgroundColor = Color.FromArgb("#F7FFFFFF");
        heart.BorderColor = story.IsFavorite ? Color.FromArgb("#FEE4E2") : Color.FromArgb("#D8DED8");
        heart.BorderWidth = 1;
        heart.CornerRadius = 21;
        heart.Padding = 0;
        heart.Margin = new Thickness(0, 10, 10, 0);
        heart.HorizontalOptions = LayoutOptions.End;
        heart.VerticalOptions = LayoutOptions.Start;
        heart.ZIndex = 20;
        heart.AutomationId = $"favorite-{story.Slug}";
        SemanticProperties.SetDescription(
            heart,
            story.IsFavorite ? "Verwyder gunsteling" : "Voeg by gunsteling");
        heart.Shadow = DeviceInfo.Current.Platform == DevicePlatform.Android
            ? null!
            : new Shadow
            {
                Brush = Brush.Black,
                Offset = new Point(0, 3),
                Radius = 8,
                Opacity = 0.12f
            };

        if (onFavoriteTap is not null)
        {
            heart.Clicked += async (_, _) => await onFavoriteTap(story);
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

    internal static ProgressiveImageRequest BuildStoryImageRequest(
        MobileStorySummary story,
        MobileApiClient apiClient,
        string? fallbackFile = null)
    {
        var fullImageUrl = apiClient.BuildImageUrl(story.ImageUrl);
        var previewImageUrl = IsLegacyWebsiteAsset(story.ThumbnailUrl)
            ? null
            : apiClient.BuildImageUrl(story.ThumbnailUrl);
        return new ProgressiveImageRequest(
            fullImageUrl,
            previewImageUrl,
            ResolveStoryFallbackFile(fallbackFile));
    }

    internal static ProgressiveImageRequest BuildStoryCardImageRequest(
        MobileStorySummary story,
        MobileApiClient apiClient,
        string? fallbackFile = null) =>
        new(
            ResolveStoryCardImageSource(story, apiClient),
            FallbackFile: ResolveStoryFallbackFile(fallbackFile));

    private static string ResolveStoryFallbackFile(string? fallbackFile) =>
        string.IsNullOrWhiteSpace(fallbackFile) ||
        string.Equals(fallbackFile, "schink_background.jpeg", StringComparison.OrdinalIgnoreCase)
            ? StoryPlaceholderFile
            : fallbackFile.Trim();

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
