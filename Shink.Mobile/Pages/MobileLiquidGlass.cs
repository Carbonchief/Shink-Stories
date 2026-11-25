#if IOS
using CoreAnimation;
using CoreGraphics;
using Foundation;
using UIKit;
#endif
#if ANDROID
using Microsoft.Maui.Platform;
using Shink.Mobile.Platforms.Android;
#endif

namespace Shink.Mobile.Pages;

internal static class MobileLiquidGlass
{
    private const nint GlassContainerTag = 26081425;
    private const nint GlassViewTag = 26081426;
    private const float NativeBarBlurOpacity = 0.88f;

    public static bool IsEnabled
    {
        get
        {
#if IOS
            return (DeviceInfo.Idiom == DeviceIdiom.Phone || DeviceInfo.Idiom == DeviceIdiom.Tablet) &&
                OperatingSystem.IsIOSVersionAtLeast(26);
#else
            return false;
#endif
        }
    }

    public static void Apply(
        Microsoft.Maui.Controls.View view,
        double cornerRadius,
        Microsoft.Maui.Graphics.Color? tint = null,
        bool topCornersOnly = false)
    {
#if IOS
        if (!IsEnabled)
        {
            return;
        }

        Attach(
            view,
            cornerRadius,
            tint,
            topCornersOnly,
            fadeFromTop: false,
            fadeFromBottom: false);
#endif
    }

    public static void ApplyTopBar(
        Microsoft.Maui.Controls.View view,
        Microsoft.Maui.Graphics.Color? tint = null)
    {
#if IOS
        if (!IsNativeBarBlurSupported)
        {
            return;
        }

        Attach(
            view,
            cornerRadius: 0,
            tint,
            topCornersOnly: false,
            fadeFromTop: false,
            fadeFromBottom: true);
#elif ANDROID
        if (!IsAndroidBarBlurSupported)
        {
            return;
        }

        AttachAndroid(view, tint, fadeFromTop: false);
#endif
    }

    public static void ApplyBottomBar(
        Microsoft.Maui.Controls.View view,
        Microsoft.Maui.Graphics.Color? tint = null)
    {
#if IOS
        if (!IsNativeBarBlurSupported)
        {
            return;
        }

        Attach(
            view,
            cornerRadius: 0,
            tint,
            topCornersOnly: false,
            fadeFromTop: true,
            fadeFromBottom: false);
#elif ANDROID
        if (!IsAndroidBarBlurSupported)
        {
            return;
        }

        AttachAndroid(view, tint, fadeFromTop: true);
#endif
    }

#if ANDROID
    private static bool IsAndroidBarBlurSupported =>
        DeviceInfo.Idiom == DeviceIdiom.Phone || DeviceInfo.Idiom == DeviceIdiom.Tablet;

    private static void AttachAndroid(
        Microsoft.Maui.Controls.View view,
        Microsoft.Maui.Graphics.Color? tint,
        bool fadeFromTop)
    {
        view.HandlerChanged += (_, _) => ConfigureAndroid(
            view,
            tint,
            fadeFromTop);
        view.Loaded += (_, _) => ConfigureAndroid(
            view,
            tint,
            fadeFromTop);
        ConfigureAndroid(
            view,
            tint,
            fadeFromTop);
    }

    private static void ConfigureAndroid(
        Microsoft.Maui.Controls.View view,
        Microsoft.Maui.Graphics.Color? tint,
        bool fadeFromTop)
    {
        if (!IsAndroidBarBlurSupported ||
            view.Handler?.PlatformView is not Android.Views.ViewGroup nativeView)
        {
            return;
        }

        nativeView.Post(() =>
        {
            // Shell can keep several pages attached. Only the page that owns
            // this bar can supply its artwork, including during tab changes.
            for (Element? element = view; element is not null; element = element.Parent)
            {
                if (element is Page && element.Handler?.PlatformView is Android.Views.ViewGroup pageRoot)
                {
                    EnsureAndroidGlass(nativeView, pageRoot, tint, fadeFromTop);
                    return;
                }
            }
        });
    }

    private static void EnsureAndroidGlass(
        Android.Views.ViewGroup nativeView,
        Android.Views.ViewGroup blurRoot,
        Microsoft.Maui.Graphics.Color? tint,
        bool fadeFromTop)
    {
        if (!nativeView.IsAttachedToWindow)
        {
            return;
        }

        var existingGlass = nativeView.FindViewWithTag(AndroidGlassViewTag);
        if (existingGlass is AndroidBarGlassView)
        {
            return;
        }

        var glassTint = (tint ?? Colors.Transparent).ToPlatform();
        var glassView = new AndroidBarGlassView(
            nativeView.Context!,
            blurRoot,
            nativeView,
            glassTint,
            fadeFromTop)
        {
            Tag = AndroidGlassViewTag,
            Alpha = NativeBarBlurOpacity,
            ImportantForAccessibility = Android.Views.ImportantForAccessibility.NoHideDescendants,
            Clickable = false,
            Focusable = false
        };

        nativeView.AddView(
            glassView,
            0,
            new Android.Views.ViewGroup.LayoutParams(
                Android.Views.ViewGroup.LayoutParams.MatchParent,
                Android.Views.ViewGroup.LayoutParams.MatchParent));

        void SizeGlassToHost()
        {
            if (nativeView.Width <= 0 || nativeView.Height <= 0)
            {
                return;
            }

            glassView.Measure(
                Android.Views.View.MeasureSpec.MakeMeasureSpec(
                    nativeView.Width,
                    Android.Views.MeasureSpecMode.Exactly),
                Android.Views.View.MeasureSpec.MakeMeasureSpec(
                    nativeView.Height,
                    Android.Views.MeasureSpecMode.Exactly));
            glassView.Layout(0, 0, nativeView.Width, nativeView.Height);
            glassView.ScheduleBackdropCapture(16);
        }

        nativeView.LayoutChange += (_, _) => SizeGlassToHost();
        nativeView.Post(SizeGlassToHost);
    }

    private const string AndroidGlassViewTag = "schink-mobile-bar-glass";

    private sealed class AndroidBarGlassView(
        Android.Content.Context context,
        Android.Views.ViewGroup blurRoot,
        Android.Views.ViewGroup glassHost,
        Android.Graphics.Color tint,
        bool fadeFromTop)
        : Android.Views.View(context),
          Android.Views.ViewTreeObserver.IOnScrollChangedListener
    {
        private const int BackdropDownsample = 20;
        private readonly Android.Graphics.Paint _tintPaint = new() { Color = tint };
        private readonly Android.Graphics.Paint _fallbackPaint = new(Android.Graphics.PaintFlags.Dither);
        private AndroidBarBitmapBackdrop? _bitmapBackdrop;
        private Android.Graphics.Paint? _bitmapFade;
        private Android.Graphics.RenderNode? _backdropNode;
        private Android.Graphics.RenderNode? _surfaceNode;
        private Android.Views.View? _liveBackdropSource;
        private Android.Views.View? _backgroundSource;
        private readonly List<Android.Views.View> _backgroundAncestors = [];
        private readonly int[] _selfLocation = new int[2];
        private readonly int[] _rootLocation = new int[2];
        private Android.Graphics.Rect? _sourceVisibleBounds;
        private Android.Graphics.Rect? _barVisibleBounds;
        private bool _scrollObserverAttached;
        private bool _isDetached;
        private bool _isCapturingBackdrop;

        protected override void OnAttachedToWindow()
        {
            _isDetached = false;
            base.OnAttachedToWindow();
            SetWillNotDraw(false);
            if (!_scrollObserverAttached && ViewTreeObserver?.IsAlive == true)
            {
                ViewTreeObserver.AddOnScrollChangedListener(this);
                _scrollObserverAttached = true;
            }

            ScheduleBackdropCapture(16);
            ScheduleBackdropCapture(650);
        }

        protected override void OnDetachedFromWindow()
        {
            if (_scrollObserverAttached && ViewTreeObserver?.IsAlive == true)
            {
                ViewTreeObserver.RemoveOnScrollChangedListener(this);
            }

            _isDetached = true;
            _scrollObserverAttached = false;
            _liveBackdropSource = null;
            _backgroundSource = null;
            _backgroundAncestors.Clear();
            _sourceVisibleBounds?.Dispose();
            _sourceVisibleBounds = null;
            _barVisibleBounds?.Dispose();
            _barVisibleBounds = null;
            _bitmapBackdrop?.Dispose();
            _bitmapBackdrop = null;
            _bitmapFade?.Dispose();
            _bitmapFade = null;
            if (OperatingSystem.IsAndroidVersionAtLeast(31))
            {
                _surfaceNode?.DiscardDisplayList();
                _surfaceNode?.Dispose();
                _surfaceNode = null;
                _backdropNode?.DiscardDisplayList();
                _backdropNode?.Dispose();
                _backdropNode = null;
            }

            base.OnDetachedFromWindow();
        }

        protected override void OnSizeChanged(int width, int height, int oldWidth, int oldHeight)
        {
            base.OnSizeChanged(width, height, oldWidth, oldHeight);
            using var fallback = CreateFadeShader(Math.Max(height, 1), tint);
            _fallbackPaint.SetShader(fallback);
            ScheduleBackdropCapture(16);
        }

        // Android dispatches this once during a draw traversal, after scroll
        // positions have settled for the frame. Posting the capture from here
        // lets an old backdrop appear over a newer position in the feed.
        public void OnScrollChanged() => CaptureBackdrop();

        private bool IsBackdropVisible()
        {
            // Shell can retain attached, Visibility.Visible pages outside the
            // screen. IsShown alone would keep their glass captures running too.
            _barVisibleBounds ??= new Android.Graphics.Rect();
            return IsShown && GetGlobalVisibleRect(_barVisibleBounds);
        }

        internal void ScheduleBackdropCapture(long delayMilliseconds)
        {
            PostDelayed(() =>
            {
                if (_isDetached)
                {
                    return;
                }

                CaptureBackdrop();
            }, delayMilliseconds);
        }

        protected override void OnDraw(Android.Graphics.Canvas canvas)
        {
            base.OnDraw(canvas);
            if (OperatingSystem.IsAndroidVersionAtLeast(31) && canvas.IsHardwareAccelerated &&
                _surfaceNode is { HasDisplayList: true } surface)
            {
                var checkpoint = canvas.Save();
                canvas.Scale(Width / (float)surface.Width, Height / (float)surface.Height);
                canvas.DrawRenderNode(surface);
                canvas.RestoreToCount(checkpoint);
            }
            else if (_bitmapBackdrop is not null)
            {
                _bitmapBackdrop.Draw(canvas, Width, Height);
            }
            else
            {
                canvas.DrawRect(0, 0, Width, Height, _fallbackPaint);
            }
        }

        private void CaptureBackdrop()
        {
            if (_isDetached || _isCapturingBackdrop ||
                !IsBackdropVisible() ||
                Width <= 0 ||
                Height <= 0 ||
                !blurRoot.IsAttachedToWindow)
            {
                return;
            }

            _isCapturingBackdrop = true;
            try
            {
                var source = FindLiveBackdropSource() ?? glassHost;
                var width = (Width + BackdropDownsample - 1) / BackdropDownsample;
                var height = (Height + BackdropDownsample - 1) / BackdropDownsample;
                GetLocationInWindow(_selfLocation);
                if (OperatingSystem.IsAndroidVersionAtLeast(31) && IsHardwareAccelerated)
                {
                    EnsureBackdropSurface(width, height);
                    var recording = _backdropNode!.BeginRecording();
                    try
                    {
                        DrawSampledContent(recording, source, width, height);
                    }
                    finally
                    {
                        _backdropNode.EndRecording();
                    }
                }
                else
                {
                    CaptureBitmapBackdrop(source, (width + 1) / 2, (height + 1) / 2);
                }
            }
            finally
            {
                _isCapturingBackdrop = false;
            }

            Invalidate();
        }

        private void CaptureBitmapBackdrop(Android.Views.View source, int width, int height)
        {
            if (_bitmapBackdrop is null || _bitmapBackdrop.Width != width || _bitmapBackdrop.Height != height)
            {
                _bitmapBackdrop?.Dispose();
                _bitmapBackdrop = new AndroidBarBitmapBackdrop(width, height);
                _bitmapFade?.Dispose();
                _bitmapFade = new Android.Graphics.Paint(Android.Graphics.PaintFlags.Dither);
                using var shader = CreateFadeShader(height, Android.Graphics.Color.Black);
                using var blend = new Android.Graphics.PorterDuffXfermode(Android.Graphics.PorterDuff.Mode.DstIn!);
                _bitmapFade.SetShader(shader);
                _bitmapFade.SetXfermode(blend);
            }

            var capture = _bitmapBackdrop.BeginCapture();
            DrawSampledContent(capture, source, capture.Width, capture.Height);
            _bitmapBackdrop.FinishCapture(_tintPaint, _bitmapFade!);
        }

        private void DrawSampledContent(Android.Graphics.Canvas canvas, Android.Views.View source, int width, int height)
        {
            var checkpoint = canvas.Save();
            try
            {
                canvas.Scale(width / (float)Width, height / (float)Height);
                canvas.Translate(-_selfLocation[0], -_selfLocation[1]);
                DrawBackdropContent(canvas, source);
            }
            finally
            {
                canvas.RestoreToCount(checkpoint);
            }
        }

        [System.Runtime.Versioning.SupportedOSPlatform("android31.0")]
        private void EnsureBackdropSurface(int width, int height)
        {
            if (_surfaceNode is { HasDisplayList: true } surface &&
                surface.Width == width && surface.Height == height)
            {
                return;
            }

            if (_backdropNode is null)
            {
                _backdropNode = new Android.Graphics.RenderNode("Schink bar artwork");
                using var blur = Android.Graphics.RenderEffect.CreateBlurEffect(
                    1.5f, 1.5f, Android.Graphics.Shader.TileMode.Clamp!);
                _backdropNode.SetRenderEffect(blur);
            }
            _backdropNode.SetPosition(0, 0, width, height);

            // Composite the tint and edge fade once at the sampling resolution.
            // The display list references the artwork node as its contents change.
            _surfaceNode ??= new Android.Graphics.RenderNode("Schink bar surface");
            _surfaceNode.SetUseCompositingLayer(true, null);
            _surfaceNode.SetPosition(0, 0, width, height);
            var recording = _surfaceNode.BeginRecording();
            try
            {
                recording.DrawRenderNode(_backdropNode);
                recording.DrawRect(0, 0, width, height, _tintPaint);
                using var shader = CreateFadeShader(height, Android.Graphics.Color.Black);
                using var blend = new Android.Graphics.PorterDuffXfermode(Android.Graphics.PorterDuff.Mode.DstIn!);
                using var mask = new Android.Graphics.Paint(Android.Graphics.PaintFlags.Dither);
                mask.SetShader(shader);
                mask.SetXfermode(blend);
                recording.DrawRect(0, 0, width, height, mask);
            }
            finally
            {
                _surfaceNode.EndRecording();
            }
        }

        private void DrawBackdropContent(Android.Graphics.Canvas canvas, Android.Views.View source)
        {
            if (!ReferenceEquals(_backgroundSource, source))
            {
                _backgroundSource = source;
                _backgroundAncestors.Clear();
                for (var parent = source.Parent as Android.Views.View; parent is not null;
                    parent = parent.Parent as Android.Views.View)
                {
                    _backgroundAncestors.Add(parent);
                    if (parent == blurRoot) break;
                }
            }

            // Transparent lists still need the page's background (which may be
            // a gradient), painted in its original window coordinates.
            for (var index = _backgroundAncestors.Count - 1; index >= 0; index--)
            {
                var ancestor = _backgroundAncestors[index];
                if (ancestor.Background is { } background)
                {
                    ancestor.GetLocationInWindow(_rootLocation);
                    var checkpoint = canvas.Save();
                    canvas.Translate(_rootLocation[0], _rootLocation[1]);
                    background.Draw(canvas);
                    canvas.RestoreToCount(checkpoint);
                }
                DrawUnderlaySiblings(canvas, ancestor,
                    index == 0 ? source : _backgroundAncestors[index - 1]);
            }

            // An embedded toolbar can be inside the only scrolling view. In
            // that case its ancestor backgrounds and underlays are the backdrop.
            // Drawing the host itself would feed the glass and icons back in.
            if (source == glassHost)
            {
                return;
            }

            source.GetLocationInWindow(_rootLocation);
            canvas.ClipRect(_rootLocation[0], _rootLocation[1],
                _rootLocation[0] + source.Width, _rootLocation[1] + source.Height);
            canvas.Translate(_rootLocation[0] - source.ScrollX, _rootLocation[1] - source.ScrollY);
            source.Draw(canvas);
        }

        private void DrawUnderlaySiblings(
            Android.Graphics.Canvas canvas, Android.Views.View ancestor, Android.Views.View branch)
        {
            if (ancestor is not Android.Views.ViewGroup group) return;
            for (var index = 0; index < group.ChildCount; index++)
            {
                var child = group.GetChildAt(index);
                if (child == branch) break;
                // Include static image/tint layers below a transparent list,
                // such as Search's background, without drawing other page trees.
                if (child is null or Android.Views.ViewGroup || !child.IsShown || child.Alpha <= 0 ||
                    child.FindViewWithTag(AndroidGlassViewTag) is not null) continue;
                child.GetLocationInWindow(_rootLocation);
                var checkpoint = canvas.Save();
                canvas.Translate(_rootLocation[0] - child.ScrollX, _rootLocation[1] - child.ScrollY);
                if (child.Alpha < 1)
                    canvas.SaveLayerAlpha(0, 0, child.Width, child.Height,
                        (int)Math.Round(child.Alpha * 255));
                child.Draw(canvas);
                canvas.RestoreToCount(checkpoint);
            }
        }

        private Android.Views.View? FindLiveBackdropSource()
        {
            _sourceVisibleBounds ??= new Android.Graphics.Rect();
            if (_liveBackdropSource is { IsAttachedToWindow: true, IsShown: true } &&
                _liveBackdropSource.GetGlobalVisibleRect(_sourceVisibleBounds))
            {
                return _liveBackdropSource;
            }

            Android.Views.View? largest = null;
            long largestArea = 0;
            FindScrollingContent(blurRoot);
            return _liveBackdropSource = largest;

            void FindScrollingContent(Android.Views.View candidate)
            {
                if (!candidate.IsShown || candidate.Width <= 0 || candidate.Height <= 0 ||
                    !candidate.GetGlobalVisibleRect(_sourceVisibleBounds))
                    return;

                // Shell's pager also contains a RecyclerView. Select page content
                // rather than navigation containers that include glass overlays.
                if (candidate is (AndroidX.RecyclerView.Widget.RecyclerView or
                        AndroidX.Core.Widget.NestedScrollView or Android.Widget.ScrollView) &&
                    candidate.FindViewWithTag(AndroidGlassViewTag) is null)
                {
                    var area = (long)candidate.Width * candidate.Height;
                    // Prefer the page viewport over its nested story carousels.
                    if (area > largestArea)
                    {
                        largest = candidate;
                        largestArea = area;
                    }
                    return;
                }

                if (candidate is Android.Views.ViewGroup group)
                    for (var index = 0; index < group.ChildCount; index++)
                        if (group.GetChildAt(index) is { } child)
                            FindScrollingContent(child);
            }
        }

        private Android.Graphics.LinearGradient CreateFadeShader(int height, Android.Graphics.Color color)
        {
            var alphaStops = fadeFromTop
                ? new[] { 0f, 0.24f, 0.68f, 0.94f, 1f, 1f }
                : new[] { 1f, 1f, 0.78f, 0.32f, 0f };
            var positions = fadeFromTop
                ? new[] { 0f, 0.09f, 0.19f, 0.31f, 0.42f, 1f }
                : new[] { 0f, 0.58f, 0.78f, 0.92f, 1f };
            var colors = alphaStops
                .Select(alpha => Android.Graphics.Color.Argb(
                    (int)Math.Round(alpha * color.A), color.R, color.G, color.B).ToArgb())
                .ToArray();

            return new Android.Graphics.LinearGradient(
                0,
                0,
                0,
                height,
                colors,
                positions,
                Android.Graphics.Shader.TileMode.Clamp!);
        }
    }
#endif

#if IOS
    private static bool IsNativeBarBlurSupported =>
        (DeviceInfo.Idiom == DeviceIdiom.Phone || DeviceInfo.Idiom == DeviceIdiom.Tablet) &&
        OperatingSystem.IsIOSVersionAtLeast(15);

    private static void Attach(
        View view,
        double cornerRadius,
        Color? tint,
        bool topCornersOnly,
        bool fadeFromTop,
        bool fadeFromBottom)
    {
        view.HandlerChanged += (_, _) => Configure(
            view,
            cornerRadius,
            tint,
            topCornersOnly,
            fadeFromTop,
            fadeFromBottom);
        view.SizeChanged += (_, _) => Configure(
            view,
            cornerRadius,
            tint,
            topCornersOnly,
            fadeFromTop,
            fadeFromBottom);
        Configure(
            view,
            cornerRadius,
            tint,
            topCornersOnly,
            fadeFromTop,
            fadeFromBottom);
    }

    private static void Configure(
        View view,
        double cornerRadius,
        Color? tint,
        bool topCornersOnly,
        bool fadeFromTop,
        bool fadeFromBottom)
    {
        if (view.Handler?.PlatformView is not UIView nativeView)
        {
            return;
        }

        var glassContainer = nativeView.Subviews
            .FirstOrDefault(candidate => candidate.Tag == GlassContainerTag);

        if (glassContainer is null)
        {
            glassContainer = new GlassContainerView
            {
                Tag = GlassContainerTag,
                UserInteractionEnabled = false,
                BackgroundColor = UIColor.Clear,
                Opaque = false
            };
            nativeView.InsertSubview(glassContainer, 0);
        }

        var glassView = glassContainer.Subviews
            .OfType<UIVisualEffectView>()
            .FirstOrDefault(candidate => candidate.Tag == GlassViewTag);

        var materialEffect = CreateMaterialEffect(isNavigationBar: fadeFromTop || fadeFromBottom);

        if (glassView is null)
        {
            glassView = new UIVisualEffectView(materialEffect);
            glassView.Tag = GlassViewTag;
            glassView.UserInteractionEnabled = false;
            glassContainer.AddSubview(glassView);
        }
        else
        {
            glassView.Effect = materialEffect;
        }

        glassContainer.Frame = nativeView.Bounds;
        glassContainer.AutoresizingMask = UIViewAutoresizing.FlexibleWidth | UIViewAutoresizing.FlexibleHeight;
        glassContainer.Layer.CornerRadius = (nfloat)cornerRadius;
        glassContainer.Layer.MaskedCorners = topCornersOnly
            ? CACornerMask.MinXMinYCorner | CACornerMask.MaxXMinYCorner
            : CACornerMask.MinXMinYCorner |
              CACornerMask.MaxXMinYCorner |
              CACornerMask.MinXMaxYCorner |
              CACornerMask.MaxXMaxYCorner;
        glassContainer.Layer.MasksToBounds = true;
        ApplyEdgeFadeMask(glassContainer, fadeFromTop, fadeFromBottom);

        glassView.Frame = glassContainer.Bounds;
        glassView.AutoresizingMask = UIViewAutoresizing.FlexibleWidth | UIViewAutoresizing.FlexibleHeight;
        glassView.Alpha = fadeFromTop || fadeFromBottom
            ? NativeBarBlurOpacity
            : 1;
        glassView.Opaque = false;
        glassView.ContentView.Opaque = false;
        glassView.Layer.Mask = null;
        glassView.ContentView.BackgroundColor = tint is null
            ? UIColor.Clear
            : UIColor.FromRGBA(
                (nfloat)tint.Red,
                (nfloat)tint.Green,
                (nfloat)tint.Blue,
                (nfloat)tint.Alpha);
        nativeView.BackgroundColor = UIColor.Clear;
        nativeView.Opaque = false;
    }

    private static UIVisualEffect CreateMaterialEffect(bool isNavigationBar)
    {
        // Use a consistent blur in either appearance. The toolbar palette
        // compensates for the light material's brightening with its teal tint.
        return UIBlurEffect.FromStyle(isNavigationBar
            ? UIBlurEffectStyle.Light
            : UIBlurEffectStyle.SystemUltraThinMaterialDark);
    }

    private static void ApplyEdgeFadeMask(
        UIView glassContainer,
        bool fadeFromTop,
        bool fadeFromBottom)
    {
        if (!fadeFromTop && !fadeFromBottom)
        {
            glassContainer.Layer.Mask = null;
            return;
        }

        var fadeMask = glassContainer.Layer.Mask as CAGradientLayer ?? new CAGradientLayer();
        fadeMask.Frame = glassContainer.Bounds;
        fadeMask.StartPoint = new CGPoint(0.5, 0);
        fadeMask.EndPoint = new CGPoint(0.5, 1);
        if (fadeFromTop)
        {
            fadeMask.Colors =
            [
                UIColor.Clear.CGColor,
                UIColor.Black.ColorWithAlpha(0.24f).CGColor,
                UIColor.Black.ColorWithAlpha(0.68f).CGColor,
                UIColor.Black.ColorWithAlpha(0.94f).CGColor,
                UIColor.Black.CGColor,
                UIColor.Black.CGColor
            ];
            fadeMask.Locations =
            [
                NSNumber.FromDouble(0),
                NSNumber.FromDouble(0.09),
                NSNumber.FromDouble(0.19),
                NSNumber.FromDouble(0.31),
                NSNumber.FromDouble(0.42),
                NSNumber.FromDouble(1)
            ];
        }
        else
        {
            fadeMask.Colors =
            [
                UIColor.Black.CGColor,
                UIColor.Black.CGColor,
                UIColor.Black.ColorWithAlpha(0.78f).CGColor,
                UIColor.Black.ColorWithAlpha(0.32f).CGColor,
                UIColor.Clear.CGColor
            ];
            fadeMask.Locations =
            [
                NSNumber.FromDouble(0),
                NSNumber.FromDouble(0.58),
                NSNumber.FromDouble(0.78),
                NSNumber.FromDouble(0.92),
                NSNumber.FromDouble(1)
            ];
        }
        glassContainer.Layer.Mask = fadeMask;
    }

    private sealed class GlassContainerView : UIView
    {
        public override void LayoutSubviews()
        {
            base.LayoutSubviews();

            foreach (var subview in Subviews)
            {
                subview.Frame = Bounds;
            }

            if (Layer.Mask is CAGradientLayer fadeMask)
            {
                fadeMask.Frame = Bounds;
            }
        }
    }
#endif
}
