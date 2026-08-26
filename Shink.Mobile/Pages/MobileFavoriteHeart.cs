namespace Shink.Mobile.Pages;

internal static class MobileFavoriteHeart
{
    public const string Glyph = "\uf004";
    public const string RegularFontFamilyName = "Font Awesome 6 Free Regular";
    public const string SolidFontFamilyName = "Font Awesome 6 Free Solid";
    private const string AndroidRegularFontFamilyName = "FontAwesomeRegular";
    private const string AndroidSolidFontFamilyName = "FontAwesomeSolid";

    public static string ResolveFontFamily(bool isFavorite) =>
        DeviceInfo.Current.Platform == DevicePlatform.Android
            ? isFavorite ? AndroidSolidFontFamilyName : AndroidRegularFontFamilyName
            : isFavorite ? SolidFontFamilyName : RegularFontFamilyName;

    public static Color ResolveColor(bool isFavorite) =>
        isFavorite ? Color.FromArgb("#FFE6EF") : Color.FromArgb("#E6FFFFFF");

    public static Label CreateLabel(bool isFavorite, double fontSize) =>
        new()
        {
            Text = Glyph,
            FontFamily = ResolveFontFamily(isFavorite),
            FontAttributes = FontAttributes.None,
            TextColor = ResolveColor(isFavorite),
            FontSize = fontSize,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center
        };

    public static Button CreateButton(bool isFavorite, double fontSize) =>
        new()
        {
            Text = Glyph,
            FontFamily = ResolveFontFamily(isFavorite),
            FontAttributes = FontAttributes.None,
            TextColor = ResolveColor(isFavorite),
            FontSize = fontSize,
            BackgroundColor = Colors.Transparent,
            BorderWidth = 0,
            Padding = 0,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            InputTransparent = false
        };

    public static void UpdateButton(Button button, bool isFavorite)
    {
        button.Text = Glyph;
        button.FontFamily = ResolveFontFamily(isFavorite);
        button.TextColor = ResolveColor(isFavorite);
    }
}
