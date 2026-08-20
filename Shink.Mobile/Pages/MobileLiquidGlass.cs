#if IOS
using CoreAnimation;
using UIKit;
#endif

namespace Shink.Mobile.Pages;

internal static class MobileLiquidGlass
{
    private const nint GlassViewTag = 26081426;

    public static bool IsEnabled
    {
        get
        {
#if IOS
            return DeviceInfo.Idiom == DeviceIdiom.Phone && OperatingSystem.IsIOSVersionAtLeast(26);
#else
            return false;
#endif
        }
    }

    public static void Apply(View view, double cornerRadius, Color? tint = null, bool topCornersOnly = false)
    {
#if IOS
        if (!IsEnabled)
        {
            return;
        }

        view.HandlerChanged += (_, _) => Configure(view, cornerRadius, tint, topCornersOnly);
        Configure(view, cornerRadius, tint, topCornersOnly);
#endif
    }

#if IOS
    private static void Configure(View view, double cornerRadius, Color? tint, bool topCornersOnly)
    {
        if (!IsEnabled || view.Handler?.PlatformView is not UIView nativeView)
        {
            return;
        }

        var glassView = nativeView.Subviews
            .OfType<UIVisualEffectView>()
            .FirstOrDefault(candidate => candidate.Tag == GlassViewTag);

        if (glassView is null)
        {
            glassView = new UIVisualEffectView(
                UIBlurEffect.FromStyle(UIBlurEffectStyle.SystemUltraThinMaterialDark));
            glassView.Tag = GlassViewTag;
            glassView.UserInteractionEnabled = false;
            nativeView.InsertSubview(glassView, 0);
        }
        else
        {
            glassView.Effect = UIBlurEffect.FromStyle(UIBlurEffectStyle.SystemUltraThinMaterialDark);
        }

        glassView.Frame = nativeView.Bounds;
        glassView.AutoresizingMask = UIViewAutoresizing.FlexibleWidth | UIViewAutoresizing.FlexibleHeight;
        glassView.Layer.CornerRadius = (nfloat)cornerRadius;
        glassView.Layer.MaskedCorners = topCornersOnly
            ? CACornerMask.MinXMinYCorner | CACornerMask.MaxXMinYCorner
            : CACornerMask.MinXMinYCorner |
              CACornerMask.MaxXMinYCorner |
              CACornerMask.MinXMaxYCorner |
              CACornerMask.MaxXMaxYCorner;
        glassView.Layer.MasksToBounds = true;
        glassView.ContentView.BackgroundColor = tint is null
            ? UIColor.Clear
            : UIColor.FromRGBA(
                (nfloat)tint.Red,
                (nfloat)tint.Green,
                (nfloat)tint.Blue,
                (nfloat)tint.Alpha);
        nativeView.BackgroundColor = UIColor.Clear;
    }
#endif
}
