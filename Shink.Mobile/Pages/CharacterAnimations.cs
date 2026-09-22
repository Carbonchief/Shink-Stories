namespace Shink.Mobile.Pages;

// The same timings and poses as the website's character profile animations.
internal static class CharacterAnimations
{
    private const string AnimationName = "character-motion";

    public static bool ReduceMotion
    {
        get
        {
#if IOS
            return UIKit.UIAccessibility.IsReduceMotionEnabled;
#elif ANDROID
            return OperatingSystem.IsAndroidVersionAtLeast(26) && !Android.Animation.ValueAnimator.AreAnimatorsEnabled();
#else
            return false;
#endif
        }
    }

    public static void Stop(VisualElement view)
    {
        view.AbortAnimation(AnimationName);
        view.Scale = 1;
        view.Rotation = view.TranslationX = view.TranslationY = 0;
    }

    public static void Idle(VisualElement image) => Animate(image, 5000, true,
        (0, 0, 0, 0, 1), (.84, 0, 0, 0, 1), (.88, 0, 0, -2, 1),
        (.92, 0, 0, 2.2, 1), (.96, 0, 0, -1.6, 1), (.98, 0, 0, 1, 1), (1, 0, 0, 0, 1));

    public static void Pop(VisualElement image) => Animate(image, 420, false,
        (0, 0, 0, 0, .72), (.65, 0, 0, 0, 1.06), (1, 0, 0, 0, 1));

    public static void Celebrate(VisualElement image) => Animate(image, 920, false,
        (0, 0, 0, 0, 1), (.18, 0, -1, -1.4, 1.01), (.38, 0, -2, 1.8, 1.015),
        (.58, 0, -1, -1.3, 1.012), (.78, 0, 0, .9, 1.006), (1, 0, 0, 0, 1));

    public static void FriendBop(VisualElement friend) => Animate(friend, 900, false,
        (0, 0, 0, 0, 1), (.20, -6, -3, -7, 1.07), (.45, 7, -10, 8, 1.14),
        (.68, -5, -4, -5, 1.06), (1, 0, 0, 0, 1));

    public static void Speaker(VisualElement button, VisualElement icon)
    {
        Animate(button, 680, true, (0, 0, 0, 0, 1), (.5, 0, -2, 0, 1.08), (1, 0, 0, 0, 1));
        Animate(icon, 560, true, (0, 0, 0, -9, 1), (.5, 0, 0, 9, 1), (1, 0, 0, -9, 1));
    }

    private static void Animate(VisualElement view, uint duration, bool repeat,
        params (double Time, double X, double Y, double Rotation, double Scale)[] frames)
    {
        Stop(view);
        if (ReduceMotion) return;
        var animation = new Animation();
        for (var i = 1; i < frames.Length; i++)
        {
            var from = frames[i - 1];
            var to = frames[i];
            animation.Add(from.Time, to.Time, new Animation(t =>
            {
                view.TranslationX = from.X + (to.X - from.X) * t;
                view.TranslationY = from.Y + (to.Y - from.Y) * t;
                view.Rotation = from.Rotation + (to.Rotation - from.Rotation) * t;
                view.Scale = from.Scale + (to.Scale - from.Scale) * t;
            }, easing: Easing.SinInOut));
        }
        animation.Commit(view, AnimationName, length: duration, repeat: () => repeat);
    }
}
