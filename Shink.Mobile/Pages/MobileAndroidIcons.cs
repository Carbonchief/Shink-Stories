namespace Shink.Mobile.Pages;

internal enum MobileAndroidIcon
{
    Menu,
    Back,
    Bell,
    Home,
    Search,
    Download,
    Profile,
    CaretDown
}

internal static class MobileAndroidChromePalette
{
    // Match the website home navbar: a 3%-to-6% warm-white surface over the
    // same colour-preserving live blur on iOS and Android.
    public static readonly Color BarSurfaceTint = Color.FromArgb("#10FFFEFA");
    public static readonly Color BarFeatherSoftTint = Color.FromArgb("#02FFFEFA");
    public static readonly Color BarFeatherMidTint = Color.FromArgb("#08FFFEFA");
    // A clearer low-opacity teal wash offsets the material's neutral-grey cast
    // without hiding the artwork or turning the bar into a solid colour.
    public static readonly Color BarNativeBlurTint = Color.FromArgb("#14005E68");
    public static readonly Color PrimaryIcon = Colors.White;
    public static readonly Color SecondaryIcon = Colors.White;
    public static readonly Color SelectedBackground = Colors.Transparent;
    public static readonly Color TopBarBackground = Colors.Transparent;
    public static readonly Color TopBarSurfaceStartTint = Color.FromArgb("#08FFFEFA");
    public static readonly Color TopBarSurfaceEndTint = Color.FromArgb("#10FFFEFA");
    public static readonly Color TopBarNativeBlurTint = Color.FromArgb("#14005E68");
    public static readonly Color TopBarIcon = Colors.White;
    public static readonly Color ProfileBackground = Color.FromArgb("#4D7FBE");
}

internal sealed class MobileAndroidIconDrawable(MobileAndroidIcon icon, Color color) : IDrawable
{
    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        canvas.StrokeColor = color;
        canvas.FillColor = color;
        canvas.StrokeSize = MathF.Max(2.1f, dirtyRect.Width * 0.085f);
        canvas.StrokeLineCap = LineCap.Round;
        canvas.StrokeLineJoin = LineJoin.Round;

        switch (icon)
        {
            case MobileAndroidIcon.Menu:
                DrawMenu(canvas, dirtyRect);
                break;
            case MobileAndroidIcon.Back:
                DrawBack(canvas, dirtyRect);
                break;
            case MobileAndroidIcon.Bell:
                DrawBell(canvas, dirtyRect);
                break;
            case MobileAndroidIcon.Home:
                DrawHome(canvas, dirtyRect);
                break;
            case MobileAndroidIcon.Search:
                DrawSearch(canvas, dirtyRect);
                break;
            case MobileAndroidIcon.Download:
                DrawDownload(canvas, dirtyRect);
                break;
            case MobileAndroidIcon.Profile:
                DrawProfile(canvas, dirtyRect);
                break;
            case MobileAndroidIcon.CaretDown:
                DrawCaretDown(canvas, dirtyRect);
                break;
        }
    }

    private static void DrawMenu(ICanvas canvas, RectF rect)
    {
        var left = rect.Width * 0.16f;
        var right = rect.Width * 0.84f;
        for (var index = 0; index < 3; index++)
        {
            var y = rect.Height * (0.28f + index * 0.22f);
            canvas.DrawLine(left, y, right, y);
        }
    }

    private static void DrawBack(ICanvas canvas, RectF rect)
    {
        var centerY = rect.Height * 0.5f;
        var left = rect.Width * 0.23f;
        var right = rect.Width * 0.76f;
        canvas.DrawLine(left, centerY, right, centerY);
        canvas.DrawLine(left, centerY, rect.Width * 0.48f, rect.Height * 0.25f);
        canvas.DrawLine(left, centerY, rect.Width * 0.48f, rect.Height * 0.75f);
    }

    private static void DrawBell(ICanvas canvas, RectF rect)
    {
        var centerX = rect.Width * 0.5f;
        var path = new PathF();
        path.MoveTo(centerX - rect.Width * 0.30f, rect.Height * 0.70f);
        path.LineTo(centerX - rect.Width * 0.23f, rect.Height * 0.60f);
        path.LineTo(centerX - rect.Width * 0.20f, rect.Height * 0.37f);
        path.LineTo(centerX - rect.Width * 0.10f, rect.Height * 0.22f);
        path.LineTo(centerX + rect.Width * 0.10f, rect.Height * 0.22f);
        path.LineTo(centerX + rect.Width * 0.20f, rect.Height * 0.37f);
        path.LineTo(centerX + rect.Width * 0.23f, rect.Height * 0.60f);
        path.LineTo(centerX + rect.Width * 0.30f, rect.Height * 0.70f);
        path.Close();
        canvas.DrawPath(path);
        canvas.DrawLine(
            centerX - rect.Width * 0.30f,
            rect.Height * 0.70f,
            centerX + rect.Width * 0.30f,
            rect.Height * 0.70f);
        canvas.DrawLine(
            centerX - rect.Width * 0.08f,
            rect.Height * 0.82f,
            centerX + rect.Width * 0.08f,
            rect.Height * 0.82f);
    }

    private static void DrawHome(ICanvas canvas, RectF rect)
    {
        var roof = new PathF();
        roof.MoveTo(rect.Width * 0.16f, rect.Height * 0.46f);
        roof.LineTo(rect.Width * 0.50f, rect.Height * 0.18f);
        roof.LineTo(rect.Width * 0.84f, rect.Height * 0.46f);
        canvas.DrawPath(roof);
        canvas.DrawLine(rect.Width * 0.24f, rect.Height * 0.40f, rect.Width * 0.24f, rect.Height * 0.82f);
        canvas.DrawLine(rect.Width * 0.76f, rect.Height * 0.40f, rect.Width * 0.76f, rect.Height * 0.82f);
        canvas.DrawLine(rect.Width * 0.24f, rect.Height * 0.82f, rect.Width * 0.76f, rect.Height * 0.82f);
        canvas.DrawLine(rect.Width * 0.44f, rect.Height * 0.82f, rect.Width * 0.44f, rect.Height * 0.59f);
        canvas.DrawLine(rect.Width * 0.56f, rect.Height * 0.82f, rect.Width * 0.56f, rect.Height * 0.59f);
        canvas.DrawLine(rect.Width * 0.44f, rect.Height * 0.59f, rect.Width * 0.56f, rect.Height * 0.59f);
    }

    private static void DrawSearch(ICanvas canvas, RectF rect)
    {
        var radius = rect.Width * 0.25f;
        var centerX = rect.Width * 0.43f;
        var centerY = rect.Height * 0.43f;
        canvas.DrawCircle(centerX, centerY, radius);
        canvas.DrawLine(
            centerX + radius * 0.70f,
            centerY + radius * 0.70f,
            rect.Width * 0.80f,
            rect.Height * 0.80f);
    }

    private static void DrawDownload(ICanvas canvas, RectF rect)
    {
        var centerX = rect.Width * 0.50f;
        var arrowBottom = rect.Height * 0.68f;
        canvas.DrawLine(centerX, rect.Height * 0.16f, centerX, arrowBottom);
        canvas.DrawLine(centerX, arrowBottom, rect.Width * 0.30f, rect.Height * 0.49f);
        canvas.DrawLine(centerX, arrowBottom, rect.Width * 0.70f, rect.Height * 0.49f);
        canvas.DrawLine(rect.Width * 0.20f, rect.Height * 0.82f, rect.Width * 0.80f, rect.Height * 0.82f);
        canvas.DrawLine(rect.Width * 0.20f, rect.Height * 0.82f, rect.Width * 0.20f, rect.Height * 0.70f);
        canvas.DrawLine(rect.Width * 0.80f, rect.Height * 0.82f, rect.Width * 0.80f, rect.Height * 0.70f);
    }

    private static void DrawProfile(ICanvas canvas, RectF rect)
    {
        var centerX = rect.Width * 0.50f;
        canvas.DrawCircle(centerX, rect.Height * 0.34f, rect.Width * 0.12f);
        var shoulders = new PathF();
        shoulders.MoveTo(rect.Width * 0.24f, rect.Height * 0.82f);
        shoulders.LineTo(rect.Width * 0.31f, rect.Height * 0.65f);
        shoulders.LineTo(rect.Width * 0.40f, rect.Height * 0.57f);
        shoulders.LineTo(rect.Width * 0.60f, rect.Height * 0.57f);
        shoulders.LineTo(rect.Width * 0.69f, rect.Height * 0.65f);
        shoulders.LineTo(rect.Width * 0.76f, rect.Height * 0.82f);
        canvas.DrawPath(shoulders);
    }

    private static void DrawCaretDown(ICanvas canvas, RectF rect)
    {
        var centerX = rect.Width * 0.5f;
        var centerY = rect.Height * 0.5f;
        var halfWidth = rect.Width * 0.20f;
        var halfHeight = rect.Height * 0.12f;
        canvas.DrawLine(centerX - halfWidth, centerY - halfHeight, centerX, centerY + halfHeight);
        canvas.DrawLine(centerX, centerY + halfHeight, centerX + halfWidth, centerY - halfHeight);
    }
}
