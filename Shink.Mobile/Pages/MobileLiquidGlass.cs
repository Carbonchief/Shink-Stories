#if IOS
using CoreAnimation;
using CoreGraphics;
using Foundation;
using UIKit;
#endif
#if ANDROID
using Microsoft.Maui.Platform;
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
        Microsoft.Maui.Graphics.Color? tint = null,
        Microsoft.Maui.Controls.View? captureExclusion = null)
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

        AttachAndroid(
            view,
            tint,
            captureExclusion,
            fadeFromTop: false,
            fadeFromBottom: true);
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

        AttachAndroid(
            view,
            tint,
            captureExclusion: null,
            fadeFromTop: true,
            fadeFromBottom: false);
#endif
    }

#if ANDROID
    private static bool IsAndroidBarBlurSupported =>
        DeviceInfo.Idiom == DeviceIdiom.Phone || DeviceInfo.Idiom == DeviceIdiom.Tablet;

    private static void AttachAndroid(
        Microsoft.Maui.Controls.View view,
        Microsoft.Maui.Graphics.Color? tint,
        Microsoft.Maui.Controls.View? captureExclusion,
        bool fadeFromTop,
        bool fadeFromBottom)
    {
        view.HandlerChanged += (_, _) => ConfigureAndroid(
            view,
            tint,
            captureExclusion,
            fadeFromTop,
            fadeFromBottom);
        view.Loaded += (_, _) => ConfigureAndroid(
            view,
            tint,
            captureExclusion,
            fadeFromTop,
            fadeFromBottom);
        ConfigureAndroid(
            view,
            tint,
            captureExclusion,
            fadeFromTop,
            fadeFromBottom);
    }

    private static void ConfigureAndroid(
        Microsoft.Maui.Controls.View view,
        Microsoft.Maui.Graphics.Color? tint,
        Microsoft.Maui.Controls.View? captureExclusion,
        bool fadeFromTop,
        bool fadeFromBottom)
    {
        if (!IsAndroidBarBlurSupported ||
            view.Handler?.PlatformView is not Android.Views.ViewGroup nativeView)
        {
            return;
        }

        nativeView.Post(() => EnsureAndroidGlass(
            nativeView,
            tint,
            captureExclusion,
            fadeFromTop,
            fadeFromBottom));
    }

    private static void EnsureAndroidGlass(
        Android.Views.ViewGroup nativeView,
        Microsoft.Maui.Graphics.Color? tint,
        Microsoft.Maui.Controls.View? captureExclusion,
        bool fadeFromTop,
        bool fadeFromBottom)
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

        var blurRoot = nativeView.RootView?.FindViewById(Android.Resource.Id.Content) as Android.Views.ViewGroup
            ?? nativeView.RootView as Android.Views.ViewGroup;
        if (blurRoot is null)
        {
            return;
        }

        var glassTint = (tint ?? Colors.Transparent).ToPlatform();
        var glassView = new AndroidBarGlassView(
            nativeView.Context!,
            blurRoot,
            nativeView,
            captureExclusion,
            glassTint,
            fadeFromTop,
            fadeFromBottom)
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
        Microsoft.Maui.Controls.View? captureExclusion,
        Android.Graphics.Color tint,
        bool fadeFromTop,
        bool fadeFromBottom)
        : Android.Views.View(context),
          Android.Views.ViewTreeObserver.IOnScrollChangedListener
    {
        // Three radius-one passes at this scale keep the existing soft spread
        // with very few pixels, so the material can follow the scrolling content.
        private const int BackdropDownsample = 20;
        private const int BackdropBlurRadius = 1;
        private const int BackdropBlurPasses = 3;
        private const long BackdropFrameMilliseconds = 32;

        private readonly Android.Graphics.Paint _bitmapPaint =
            new(Android.Graphics.PaintFlags.FilterBitmap | Android.Graphics.PaintFlags.Dither);
        private readonly Android.Graphics.Paint _tintPaint =
            new(Android.Graphics.PaintFlags.AntiAlias)
            {
                Color = tint
            };
        private readonly Android.Graphics.Paint _fadePaint =
            CreateFadePaint();
        private Android.Graphics.Bitmap? _blurredBackdrop;
        private Android.Graphics.Bitmap? _captureBitmap;
        private Android.Graphics.Bitmap? _spareBitmap;
        private Android.Graphics.Canvas? _captureCanvas;
        private Android.Views.View? _liveBackdropSource;
        private Android.Views.View? _backgroundSource;
        private readonly List<Android.Views.View> _backgroundAncestors = [];
        private readonly int[] _selfLocation = new int[2];
        private readonly int[] _rootLocation = new int[2];
        private Android.Graphics.Rect? _sourceVisibleBounds;
        private Android.Graphics.Rect? _barVisibleBounds;
        private Android.Graphics.LinearGradient? _fadeShader;
        private int[]? _pixelBuffer;
        private int[]? _blurBuffer;
        private bool _scrollObserverAttached;
        private bool _isDetached;
        private bool _isCapturingBackdrop;
        private bool _scrollCapturePosted;
        private long _lastBackdropCaptureUptime;
        private Java.Lang.Runnable? _scrollCaptureRunnable;
        private Java.Lang.Runnable? _scrollFrameRunnable;

        private Java.Lang.Runnable ScrollCaptureRunnable =>
            _scrollCaptureRunnable ??= new(RefreshBackdropForScroll);

        private Java.Lang.Runnable ScrollFrameRunnable =>
            _scrollFrameRunnable ??= new(() =>
            {
                // A Handler message runs after this frame's layout/draw phases.
                if (!_isDetached) Post(ScrollCaptureRunnable);
            });

        private static Android.Graphics.Paint CreateFadePaint()
        {
            var paint = new Android.Graphics.Paint(
                Android.Graphics.PaintFlags.AntiAlias |
                Android.Graphics.PaintFlags.Dither);
            paint.SetXfermode(new Android.Graphics.PorterDuffXfermode(
                Android.Graphics.PorterDuff.Mode.DstIn!));
            return paint;
        }

        protected override void OnAttachedToWindow()
        {
            _isDetached = false;
            base.OnAttachedToWindow();
            SetWillNotDraw(false);
            // Cache the composed bitmap/tint/fade between live captures. Without
            // a layer Android replays both offscreen alpha/mask passes whenever
            // the list scrolls, even if this bar's bitmap has not changed.
            SetLayerType(Android.Views.LayerType.Hardware, null);
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
            _scrollCapturePosted = false;
            if (_scrollCaptureRunnable is not null)
            {
                RemoveCallbacks(_scrollCaptureRunnable);
            }
            if (_scrollFrameRunnable is not null)
            {
                RemoveCallbacks(_scrollFrameRunnable);
            }
            _scrollObserverAttached = false;
            _liveBackdropSource = null;
            _backgroundSource = null;
            _backgroundAncestors.Clear();
            _sourceVisibleBounds?.Dispose();
            _sourceVisibleBounds = null;
            _barVisibleBounds?.Dispose();
            _barVisibleBounds = null;
            _captureCanvas?.Dispose();
            _captureCanvas = null;
            _blurredBackdrop?.Dispose();
            _blurredBackdrop = null;
            _captureBitmap?.Dispose();
            _captureBitmap = null;
            _spareBitmap?.Dispose();
            _spareBitmap = null;
            _fadeShader?.Dispose();
            _fadeShader = null;
            base.OnDetachedFromWindow();
        }

        protected override void OnSizeChanged(int width, int height, int oldWidth, int oldHeight)
        {
            base.OnSizeChanged(width, height, oldWidth, oldHeight);
            _fadeShader?.Dispose();
            _fadeShader = CreateFadeShader(Math.Max(height, 1));
            ScheduleBackdropCapture(16);
        }

        public void OnScrollChanged()
        {
            if (_isDetached || _isCapturingBackdrop || !IsBackdropVisible() || _scrollCapturePosted)
            {
                return;
            }

            // Keep refreshing throughout a drag/fling, at a bounded cadence.
            // New scroll events do not postpone an already scheduled refresh.
            _scrollCapturePosted = true;
            var delay = Math.Max(0, BackdropFrameMilliseconds -
                (Android.OS.SystemClock.UptimeMillis() - _lastBackdropCaptureUptime));
            // Use the animation callback only to enqueue a Handler message. The
            // snapshot then runs after this frame's layout/draw work is submitted.
            PostOnAnimationDelayed(ScrollFrameRunnable, delay);
        }

        private void RefreshBackdropForScroll()
        {
            _scrollCapturePosted = false;
            CaptureBackdrop();
        }

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
            if ((!fadeFromTop && !fadeFromBottom) || Width <= 0 || Height <= 0)
            {
                DrawBackdropAndTint(canvas);
                return;
            }

            _fadeShader ??= CreateFadeShader(Height);
            _fadePaint.SetShader(_fadeShader);

            var layerCheckpoint = canvas.SaveLayer(0, 0, Width, Height, null);
            DrawBackdropAndTint(canvas);
            canvas.DrawRect(0, 0, Width, Height, _fadePaint);
            canvas.RestoreToCount(layerCheckpoint);
        }

        private void DrawBackdropAndTint(Android.Graphics.Canvas canvas)
        {
            if (_blurredBackdrop is not null && !_blurredBackdrop.IsRecycled)
            {
                using var destination = new Android.Graphics.RectF(0, 0, Width, Height);
                canvas.DrawBitmap(_blurredBackdrop, null, destination, _bitmapPaint);
            }

            canvas.DrawRect(0, 0, Width, Height, _tintPaint);
        }

        private void CaptureBackdrop()
        {
            if (_isDetached ||
                _isCapturingBackdrop ||
                !IsBackdropVisible() ||
                Width <= 0 ||
                Height <= 0 ||
                !blurRoot.IsAttachedToWindow)
            {
                return;
            }

            // Initial/resize captures share the pending animation-frame refresh.
            if (_scrollCapturePosted)
            {
                return;
            }

            _isCapturingBackdrop = true;
            _lastBackdropCaptureUptime = Android.OS.SystemClock.UptimeMillis();
            var liveSource = FindLiveBackdropSource();
            var bitmapWidth = Math.Max(1, (int)Math.Ceiling(Width / (double)BackdropDownsample));
            var bitmapHeight = Math.Max(1, (int)Math.Ceiling(Height / (double)BackdropDownsample));
            EnsureBackdropBitmap(bitmapWidth, bitmapHeight);
            var bitmap = _captureBitmap;
            if (bitmap is null)
            {
                _isCapturingBackdrop = false;
                return;
            }

            bitmap.EraseColor(Android.Graphics.Color.Transparent);
            GetLocationInWindow(_selfLocation);
            blurRoot.GetLocationInWindow(_rootLocation);

            var exclusionView = liveSource is null ? FindCaptureExclusionView() : null;
            var previousVisibility = exclusionView?.Visibility;
            var additionalExclusion = liveSource is null
                ? captureExclusion?.Handler?.PlatformView as Android.Views.View
                : null;
            var previousAdditionalVisibility = additionalExclusion?.Visibility;
            try
            {
                if (exclusionView is not null)
                    exclusionView.Visibility = Android.Views.ViewStates.Invisible;
                if (additionalExclusion is not null && additionalExclusion != exclusionView)
                {
                    additionalExclusion.Visibility = Android.Views.ViewStates.Invisible;
                }
                var captureCanvas = _captureCanvas!;
                var checkpoint = captureCanvas.Save();
                try
                {
                    captureCanvas.Scale(
                        1f / BackdropDownsample,
                        1f / BackdropDownsample);
                    captureCanvas.Translate(-_selfLocation[0], -_selfLocation[1]);
                    if (liveSource is not null)
                    {
                        // Snapshot only the scrolling viewport beneath the bar.
                        // Keep bar overlays visible and leave the MAUI window's
                        // other pages, toolbars and layout state untouched.
                        DrawScrollingContent(captureCanvas, liveSource);
                    }
                    else
                    {
                        captureCanvas.Translate(_rootLocation[0], _rootLocation[1]);
                        blurRoot.Draw(captureCanvas);
                    }
                }
                finally
                {
                    captureCanvas.RestoreToCount(checkpoint);
                }

                ApplyBoxBlur(bitmap, BackdropBlurRadius, BackdropBlurPasses);
                // Keep the displayed bitmap immutable while RenderThread may
                // still be reading it. Reuse three small buffers in rotation.
                var previousDisplay = _blurredBackdrop;
                _blurredBackdrop = bitmap;
                _captureBitmap = _spareBitmap;
                _spareBitmap = previousDisplay;
                _captureCanvas!.SetBitmap(_captureBitmap);
            }
            finally
            {
                if (additionalExclusion is not null &&
                    additionalExclusion != exclusionView &&
                    previousAdditionalVisibility is not null)
                {
                    additionalExclusion.Visibility = previousAdditionalVisibility.Value;
                }
                if (exclusionView is not null && previousVisibility is not null)
                    exclusionView.Visibility = previousVisibility.Value;
                _isCapturingBackdrop = false;
            }

            PostInvalidateOnAnimation();
        }

        private void DrawScrollingContent(Android.Graphics.Canvas canvas, Android.Views.View source)
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
                if (candidate.FindViewWithTag(AndroidGlassViewTag) is null &&
                    candidate is (AndroidX.RecyclerView.Widget.RecyclerView or
                        AndroidX.Core.Widget.NestedScrollView or Android.Widget.ScrollView))
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

        private Android.Views.View FindCaptureExclusionView()
        {
            Android.Views.View candidate = glassHost;
            var maximumOverlayHeight = Math.Max(Height, 1) * 2;

            while (candidate.Parent is Android.Views.View parent &&
                   parent != blurRoot &&
                   parent.Height > 0 &&
                   parent.Height <= maximumOverlayHeight)
            {
                candidate = parent;
            }

            return candidate;
        }

        private void EnsureBackdropBitmap(int width, int height)
        {
            if (_captureBitmap is not null &&
                !_captureBitmap.IsRecycled &&
                _captureBitmap.Width == width &&
                _captureBitmap.Height == height)
            {
                return;
            }

            _captureCanvas?.Dispose();
            _blurredBackdrop?.Dispose();
            _captureBitmap?.Dispose();
            _spareBitmap?.Dispose();
            _blurredBackdrop = Android.Graphics.Bitmap.CreateBitmap(
                width,
                height,
                Android.Graphics.Bitmap.Config.Argb8888!);
            _captureBitmap = Android.Graphics.Bitmap.CreateBitmap(
                width, height, Android.Graphics.Bitmap.Config.Argb8888!);
            _spareBitmap = Android.Graphics.Bitmap.CreateBitmap(
                width, height, Android.Graphics.Bitmap.Config.Argb8888!);
            _captureCanvas = new Android.Graphics.Canvas(_captureBitmap!);
            _pixelBuffer = new int[width * height];
            _blurBuffer = new int[width * height];
        }

        private void ApplyBoxBlur(Android.Graphics.Bitmap bitmap, int radius, int passCount)
        {
            var width = bitmap.Width;
            var height = bitmap.Height;
            var pixelCount = width * height;
            if (pixelCount <= 0 || radius <= 0)
            {
                return;
            }

            _pixelBuffer ??= new int[pixelCount];
            _blurBuffer ??= new int[pixelCount];
            if (_pixelBuffer.Length != pixelCount || _blurBuffer.Length != pixelCount)
            {
                _pixelBuffer = new int[pixelCount];
                _blurBuffer = new int[pixelCount];
            }

            bitmap.GetPixels(_pixelBuffer, 0, width, 0, 0, width, height);
            for (var pass = 0; pass < passCount; pass++)
            {
                BoxBlurHorizontal(_pixelBuffer, _blurBuffer, width, height, radius);
                BoxBlurVertical(_blurBuffer, _pixelBuffer, width, height, radius);
            }

            bitmap.SetPixels(_pixelBuffer, 0, width, 0, 0, width, height);
        }

        private static void BoxBlurHorizontal(
            int[] source,
            int[] destination,
            int width,
            int height,
            int radius)
        {
            var windowSize = radius * 2 + 1;
            for (var y = 0; y < height; y++)
            {
                var rowOffset = y * width;
                var alpha = 0;
                var red = 0;
                var green = 0;
                var blue = 0;
                for (var sample = -radius; sample <= radius; sample++)
                {
                    AddColor(source[rowOffset + Math.Clamp(sample, 0, width - 1)], ref alpha, ref red, ref green, ref blue);
                }

                for (var x = 0; x < width; x++)
                {
                    destination[rowOffset + x] = ComposeColor(alpha, red, green, blue, windowSize);
                    RemoveColor(source[rowOffset + Math.Clamp(x - radius, 0, width - 1)], ref alpha, ref red, ref green, ref blue);
                    AddColor(source[rowOffset + Math.Clamp(x + radius + 1, 0, width - 1)], ref alpha, ref red, ref green, ref blue);
                }
            }
        }

        private static void BoxBlurVertical(
            int[] source,
            int[] destination,
            int width,
            int height,
            int radius)
        {
            var windowSize = radius * 2 + 1;
            for (var x = 0; x < width; x++)
            {
                var alpha = 0;
                var red = 0;
                var green = 0;
                var blue = 0;
                for (var sample = -radius; sample <= radius; sample++)
                {
                    AddColor(source[Math.Clamp(sample, 0, height - 1) * width + x], ref alpha, ref red, ref green, ref blue);
                }

                for (var y = 0; y < height; y++)
                {
                    var index = y * width + x;
                    destination[index] = ComposeColor(alpha, red, green, blue, windowSize);
                    RemoveColor(source[Math.Clamp(y - radius, 0, height - 1) * width + x], ref alpha, ref red, ref green, ref blue);
                    AddColor(source[Math.Clamp(y + radius + 1, 0, height - 1) * width + x], ref alpha, ref red, ref green, ref blue);
                }
            }
        }

        private static void AddColor(
            int color,
            ref int alpha,
            ref int red,
            ref int green,
            ref int blue)
        {
            alpha += (color >>> 24) & 0xff;
            red += (color >>> 16) & 0xff;
            green += (color >>> 8) & 0xff;
            blue += color & 0xff;
        }

        private static void RemoveColor(
            int color,
            ref int alpha,
            ref int red,
            ref int green,
            ref int blue)
        {
            alpha -= (color >>> 24) & 0xff;
            red -= (color >>> 16) & 0xff;
            green -= (color >>> 8) & 0xff;
            blue -= color & 0xff;
        }

        private static int ComposeColor(
            int alpha,
            int red,
            int green,
            int blue,
            int divisor) =>
            ((alpha / divisor) << 24) |
            ((red / divisor) << 16) |
            ((green / divisor) << 8) |
            (blue / divisor);

        private Android.Graphics.LinearGradient CreateFadeShader(int height)
        {
            var alphaStops = fadeFromTop
                ? new[] { 0f, 0.24f, 0.68f, 0.94f, 1f, 1f }
                : new[] { 1f, 1f, 0.78f, 0.32f, 0f };
            var positions = fadeFromTop
                ? new[] { 0f, 0.09f, 0.19f, 0.31f, 0.42f, 1f }
                : new[] { 0f, 0.58f, 0.78f, 0.92f, 1f };
            var colors = alphaStops
                .Select(alpha => Android.Graphics.Color.Argb(
                    (int)Math.Round(alpha * 255),
                    0,
                    0,
                    0).ToArgb())
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
        // Android blurs the artwork without a dark material underneath the
        // shared tint. Light keeps the backdrop's approximate brightness on
        // iPhone and iPad, including when the system is in dark mode.
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
