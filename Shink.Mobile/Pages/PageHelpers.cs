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
        var image = new Image
        {
            Source = apiClient.BuildImageUrl(story.ThumbnailUrl),
            Aspect = Aspect.AspectFill,
            HeightRequest = 180
        };

        var title = new Label
        {
            Text = story.Title,
            FontSize = 18,
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.Black
        };

        var description = new Label
        {
            Text = story.Description,
            FontSize = 13,
            TextColor = Color.FromArgb("#5F5F5F"),
            MaxLines = 3,
            LineBreakMode = LineBreakMode.TailTruncation
        };

        var lockBadge = new Border
        {
            IsVisible = story.IsLocked,
            BackgroundColor = Color.FromArgb("#222222"),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 999 },
            Padding = new Thickness(10, 4),
            HorizontalOptions = LayoutOptions.Start,
            Content = new Label
            {
                Text = "Gesluit",
                TextColor = Colors.White,
                FontSize = 12,
                FontAttributes = FontAttributes.Bold
            }
        };

        var favoriteButton = new Button
        {
            Text = story.IsFavorite ? "Hartjie af" : "Hartjie",
            FontSize = 12,
            BackgroundColor = Color.FromArgb("#FFF0EC"),
            TextColor = Color.FromArgb("#B42318"),
            CornerRadius = 14,
            Padding = new Thickness(10, 6),
            IsVisible = onFavoriteTap is not null
        };
        favoriteButton.Clicked += async (_, _) =>
        {
            if (onFavoriteTap is not null)
            {
                await onFavoriteTap(story);
            }
        };

        var openButton = new Button
        {
            Text = story.IsLocked ? "Maak oop" : "Luister nou",
            BackgroundColor = Color.FromArgb("#0F766E"),
            TextColor = Colors.White,
            CornerRadius = 16,
            Padding = new Thickness(14, 8)
        };
        openButton.Clicked += async (_, _) => await onTap(story);

        var body = new VerticalStackLayout
        {
            Spacing = 10,
            Children =
            {
                image,
                lockBadge,
                title,
                description,
                new HorizontalStackLayout
                {
                    Spacing = 8,
                    Children = { openButton, favoriteButton }
                }
            }
        };

        var frame = new Border
        {
            BackgroundColor = Colors.White,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 24 },
            Padding = 14,
            Margin = new Thickness(0, 0, 0, 16),
            Content = body
        };

        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) => await onTap(story);
        frame.GestureRecognizers.Add(tap);
        return frame;
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
}
