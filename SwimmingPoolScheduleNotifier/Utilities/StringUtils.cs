using System.Text.RegularExpressions;

namespace SwimmingPoolScheduleNotifier.Utilities;

public static class StringUtils
{
    public static string CleanText(string? text) =>
        string.IsNullOrWhiteSpace(text) ? string.Empty : Regex.Replace(text, @"\s+", " ").Trim();
}
