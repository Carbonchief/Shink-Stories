namespace Shink.Mobile.Pages;

internal sealed class GameCelebrationOverlay : Grid
{
    private static readonly Color[] ConfettiColors =
    [
        Color.FromArgb("#F8C854"),
        Color.FromArgb("#70D6FF"),
        Color.FromArgb("#FF70A6"),
        Color.FromArgb("#B8F7D4"),
        Color.FromArgb("#F7A072"),
        Color.FromArgb("#CDB4DB")
    ];

    private static readonly ConfettiMotion[] ConfettiMotions =
    [
        new(-0.48, -0.32, -220, 0), new(-0.37, -0.43, 175, 22),
        new(-0.26, -0.28, 250, 44), new(-0.14, -0.46, -165, 14),
        new(-0.04, -0.34, 215, 54), new(0.12, -0.44, -230, 20),
        new(0.27, -0.3, 205, 36), new(0.4, -0.4, -180, 60),
        new(0.49, -0.22, 265, 28), new(-0.5, 0.06, -255, 78),
        new(-0.39, 0.2, 195, 98), new(-0.27, 0.32, -185, 66),
        new(-0.13, 0.26, 270, 110), new(0.04, 0.36, -220, 90),
        new(0.19, 0.28, 185, 116), new(0.34, 0.18, -265, 74),
        new(0.49, 0.05, 235, 106), new(-0.45, -0.1, 170, 132),
        new(-0.32, -0.02, -215, 146), new(-0.18, 0.1, 240, 124),
        new(0.16, 0.08, -175, 138), new(0.3, -0.08, 220, 158),
        new(0.44, -0.15, -240, 150), new(0.02, -0.5, 280, 68)
    ];

    private readonly IReadOnlyList<Border> _confetti;
    private readonly Border _messageCard;
    private readonly Label _titleLabel;
    private readonly Label _messageLabel;
    private int _animationGeneration;

    public GameCelebrationOverlay()
    {
        IsVisible = false;
        InputTransparent = true;
        AutomationId = "perfect-score-celebration";

        var particles = new List<Border>(ConfettiMotions.Length);
        for (var index = 0; index < ConfettiMotions.Length; index++)
        {
            var particle = new Border
            {
                WidthRequest = index % 3 == 0 ? 13 : 8,
                HeightRequest = index % 3 == 0 ? 13 : 20,
                BackgroundColor = ConfettiColors[index % ConfettiColors.Length],
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = 3 },
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
                Opacity = 0,
                InputTransparent = true
            };
            particles.Add(particle);
            Children.Add(particle);
        }

        _confetti = particles;
        _titleLabel = new Label
        {
            Text = "VOLPUNTE!",
            FontSize = 30,
            FontAttributes = FontAttributes.Bold,
            CharacterSpacing = 1.2,
            TextColor = Color.FromArgb("#166476"),
            HorizontalTextAlignment = TextAlignment.Center
        };
        _messageLabel = new Label
        {
            FontSize = 17,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#27313A"),
            HorizontalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.WordWrap,
            MaxLines = 3
        };
        _messageCard = new Border
        {
            BackgroundColor = Color.FromArgb("#FFF7E8"),
            Stroke = Color.FromArgb("#F8C854"),
            StrokeThickness = 3,
            StrokeShape = new RoundRectangle { CornerRadius = 28 },
            Padding = new Thickness(24, 18, 24, 22),
            Margin = new Thickness(28),
            MaximumWidthRequest = 370,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Opacity = 0,
            Scale = 0.68,
            Shadow = new Shadow
            {
                Brush = Brush.Black,
                Offset = new Point(0, 10),
                Radius = 24,
                Opacity = 0.22f
            },
            Content = new VerticalStackLayout
            {
                Spacing = 7,
                Children =
                {
                    new Image
                    {
                        Source = "oortjies_01.png",
                        HeightRequest = 76,
                        Aspect = Aspect.AspectFit,
                        HorizontalOptions = LayoutOptions.Center
                    },
                    _titleLabel,
                    _messageLabel
                }
            }
        };
        Children.Add(_messageCard);
    }

    public async Task CelebrateAsync(string title, string message)
    {
        Hide();
        var generation = ++_animationGeneration;
        _titleLabel.Text = title;
        _messageLabel.Text = message;
        Opacity = 1;
        IsVisible = true;
        ResetAnimationState();

        try
        {
            if (ShouldReduceMotion())
            {
                _messageCard.Opacity = 1;
                _messageCard.Scale = 1;
                _messageCard.Rotation = 0;
                await Task.Delay(1400);
                if (generation == _animationGeneration)
                {
                    await this.FadeToAsync(0, 180, Easing.Linear);
                    IsVisible = false;
                    Opacity = 1;
                }

                return;
            }

            var width = Width > 0 ? Width : 390;
            var height = Height > 0 ? Height : 700;
            var particleAnimations = _confetti
                .Select((particle, index) => AnimateConfettiAsync(
                    particle,
                    ConfettiMotions[index],
                    width,
                    height,
                    generation))
                .ToArray();

            await Task.WhenAll(
                AnimateMessageCardAsync(generation),
                Task.WhenAll(particleAnimations));
            if (generation != _animationGeneration)
            {
                return;
            }

            await Task.Delay(850);
            if (generation != _animationGeneration)
            {
                return;
            }

            await this.FadeToAsync(0, 260, Easing.CubicIn);
            if (generation == _animationGeneration)
            {
                IsVisible = false;
                Opacity = 1;
            }
        }
        catch
        {
            if (generation == _animationGeneration)
            {
                IsVisible = false;
                Opacity = 1;
            }
        }
    }

    public void Hide()
    {
        _animationGeneration++;
        this.CancelAnimations();
        _messageCard.CancelAnimations();
        foreach (var particle in _confetti)
        {
            particle.CancelAnimations();
        }

        IsVisible = false;
        Opacity = 1;
    }

    private void ResetAnimationState()
    {
        _messageCard.Opacity = 0;
        _messageCard.Scale = 0.68;
        _messageCard.Rotation = -5;
        foreach (var particle in _confetti)
        {
            particle.Opacity = 0;
            particle.Scale = 0.5;
            particle.Rotation = 0;
            particle.TranslationX = 0;
            particle.TranslationY = 0;
        }
    }

    private async Task AnimateMessageCardAsync(int generation)
    {
        await Task.WhenAll(
            _messageCard.FadeToAsync(1, 180, Easing.CubicOut),
            _messageCard.ScaleToAsync(1.06, 390, Easing.SpringOut),
            _messageCard.RotateToAsync(2.5, 390, Easing.CubicOut));
        if (generation != _animationGeneration)
        {
            return;
        }

        await Task.WhenAll(
            _messageCard.ScaleToAsync(1, 130, Easing.CubicInOut),
            _messageCard.RotateToAsync(-2, 110, Easing.CubicInOut));
        await _messageCard.RotateToAsync(0, 120, Easing.CubicInOut);
    }

    private async Task AnimateConfettiAsync(
        Border particle,
        ConfettiMotion motion,
        double width,
        double height,
        int generation)
    {
        await Task.Delay(motion.DelayMilliseconds);
        if (generation != _animationGeneration)
        {
            return;
        }

        particle.Opacity = 1;
        await Task.WhenAll(
            particle.TranslateToAsync(
                motion.HorizontalFactor * width,
                motion.VerticalFactor * height,
                1120,
                Easing.CubicOut),
            particle.RotateToAsync(motion.Rotation, 1120, Easing.Linear),
            particle.ScaleToAsync(1, 240, Easing.CubicOut),
            particle.FadeToAsync(0, 1120, Easing.CubicIn));
    }

    private static bool ShouldReduceMotion()
    {
#if IOS || MACCATALYST
        return UIKit.UIAccessibility.IsReduceMotionEnabled;
#else
        return false;
#endif
    }

    private sealed record ConfettiMotion(
        double HorizontalFactor,
        double VerticalFactor,
        double Rotation,
        int DelayMilliseconds);
}
