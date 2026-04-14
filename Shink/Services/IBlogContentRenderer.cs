namespace Shink.Services;

public interface IBlogContentRenderer
{
    string RenderHtml(string? markdown);

    string ConvertToPlainText(string? markdown);

    string BuildExcerpt(string? markdown, int maxLength = 220);

    int EstimateReadingTimeMinutes(string? markdown);
}
