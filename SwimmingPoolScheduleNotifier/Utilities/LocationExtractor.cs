using System.Globalization;
using System.Text.RegularExpressions;
using AngleSharp.Dom;

namespace SwimmingPoolScheduleNotifier.Utilities;

public static class LocationExtractor
{
    public static string ExtractLocationName(string url, IDocument? document = null)
    {
        if (url.Contains("zwembad-de-wisselaar", StringComparison.OrdinalIgnoreCase))
            return "Zwembad De Wisselaar";
        if (url.Contains("zwembad-sonsbeeck", StringComparison.OrdinalIgnoreCase))
            return "Zwembad Sonsbeeck";

        var heading = document?.QuerySelector(".hero__title, .location-name")?.TextContent?.Trim();
        if (!string.IsNullOrWhiteSpace(heading) && !heading.Contains("Banenzwemmen", StringComparison.OrdinalIgnoreCase))
        {
            return heading;
        }

        var match = Regex.Match(url, @"/activiteiten/[^/]+/([^/#]+)");
        if (match.Success)
        {
            var slug = match.Groups[1].Value.Replace('-', ' ');
            return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(slug);
        }

        return "Zwembad Breda";
    }
}
