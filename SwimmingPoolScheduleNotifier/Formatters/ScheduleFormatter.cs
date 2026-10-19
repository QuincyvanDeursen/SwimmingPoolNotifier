using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using SwimmingPoolScheduleNotifier.Models;

namespace SwimmingPoolScheduleNotifier.Formatters;

public static class ScheduleFormatter
{
    public static string FormatSingleLocationSchedule(LocationScheduleModel location)
    {
        return FormatCombinedLocationsSchedule(new[] { location });
    }

    /// <summary>
    /// Formats a concise push notification containing strictly:
    /// 1. Ochtend (07:00 - 09:00) per zwembad
    /// 2. Middag (15:00 - 18:00) per zwembad
    /// 3. Weekend (alle tijden) per zwembad
    /// No emoticons, minimal verbosity.
    /// </summary>
    public static string FormatCombinedLocationsSchedule(IReadOnlyList<LocationScheduleModel> locations)
    {
        var morningStart = new TimeOnly(7, 0);
        var morningEnd = new TimeOnly(9, 0);

        var afternoonStart = new TimeOnly(15, 0);
        var afternoonEnd = new TimeOnly(18, 0);

        DateOnly? minDate = locations
            .SelectMany(l => l.Days)
            .Where(d => d.Date.HasValue)
            .Select(d => d.Date)
            .Min();

        DateOnly? maxDate = minDate?.AddDays(6);

        var sb = new StringBuilder();
        string headerTitle = locations.Count > 1
            ? "Zwemrooster Breda"
            : $"Zwemrooster {locations[0].LocationName}";

        sb.AppendLine(headerTitle);
        sb.AppendLine(new string('=', headerTitle.Length));

        void AppendSection(string sectionTitle, Func<SwimEventModel, bool> filter, bool weekendOnly, bool weekdayOnly)
        {
            sb.AppendLine();
            sb.AppendLine(sectionTitle);

            bool anyFound = false;

            foreach (var loc in locations)
            {
                var matchingDays = new List<(DateOnly? Date, string DayFormatted, List<string> Times)>();

                foreach (var day in loc.Days)
                {
                    if (maxDate.HasValue && day.Date.HasValue && day.Date.Value > maxDate.Value)
                        continue;

                    bool isWk = IsWeekend(day.DayTitle, day.Date);
                    if (weekendOnly && !isWk) continue;
                    if (weekdayOnly && isWk) continue;

                    var matchingEvents = day.Events.Where(filter).ToList();
                    if (matchingEvents.Count == 0) continue;

                    var times = matchingEvents.Select(e => e.DisplayTime).Distinct().ToList();
                    string dayFormatted = FormatDayWithDate(day.DayTitle, day.Date);
                    matchingDays.Add((day.Date, dayFormatted, times));
                }

                if (matchingDays.Count > 0)
                {
                    anyFound = true;
                    sb.AppendLine($"{loc.LocationName}:");
                    var sortedDays = matchingDays.OrderBy(d => d.Date ?? DateOnly.MaxValue);
                    foreach (var dayItem in sortedDays)
                    {
                        sb.AppendLine($"- {dayItem.DayFormatted}: {string.Join(", ", dayItem.Times)}");
                    }
                }
            }

            if (!anyFound)
            {
                sb.AppendLine("Geen");
            }
        }

        // 1. Ochtend
        AppendSection(
            "OCHTEND (07:00 - 09:00)",
            e => e.IsBetween(morningStart, morningEnd),
            weekendOnly: false,
            weekdayOnly: true);

        // 2. Middag
        AppendSection(
            "MIDDAG (15:00 - 18:00)",
            e => e.IsBetween(afternoonStart, afternoonEnd),
            weekendOnly: false,
            weekdayOnly: true);

        // 3. Weekend
        AppendSection(
            "WEEKEND",
            _ => true,
            weekendOnly: true,
            weekdayOnly: false);

        return sb.ToString().Trim();
    }

    private static bool IsWeekend(string dayTitle, DateOnly? date)
    {
        if (dayTitle.Contains("zaterdag", StringComparison.OrdinalIgnoreCase) ||
            dayTitle.Contains("zondag", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return date.HasValue && (date.Value.DayOfWeek == DayOfWeek.Saturday || date.Value.DayOfWeek == DayOfWeek.Sunday);
    }

    private static string GetDayAbbrev(string dayTitle, DateOnly? date)
    {
        var lower = dayTitle.ToLowerInvariant();
        if (lower.Contains("maandag")) return "Ma";
        if (lower.Contains("dinsdag")) return "Di";
        if (lower.Contains("woensdag")) return "Wo";
        if (lower.Contains("donderdag")) return "Do";
        if (lower.Contains("vrijdag")) return "Vr";
        if (lower.Contains("zaterdag")) return "Za";
        if (lower.Contains("zondag")) return "Zo";

        if (date.HasValue)
        {
            return date.Value.DayOfWeek switch
            {
                DayOfWeek.Monday => "Ma",
                DayOfWeek.Tuesday => "Di",
                DayOfWeek.Wednesday => "Wo",
                DayOfWeek.Thursday => "Do",
                DayOfWeek.Friday => "Vr",
                DayOfWeek.Saturday => "Za",
                DayOfWeek.Sunday => "Zo",
                _ => date.Value.ToString("ddd", new CultureInfo("nl-NL"))
            };
        }

        var words = dayTitle.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length > 0 ? words[0] : dayTitle;
    }

    private static string FormatDayWithDate(string dayTitle, DateOnly? date)
    {
        string abbrev = GetDayAbbrev(dayTitle, date);
        if (date.HasValue)
        {
            return $"{abbrev} {date.Value.Day:00}/{date.Value.Month:00}";
        }

        var match = Regex.Match(dayTitle, @"(\d{1,2})\s+([a-zA-Z]+)");
        if (match.Success && int.TryParse(match.Groups[1].Value, out int dayNum))
        {
            int monthNum = match.Groups[2].Value.ToLowerInvariant() switch
            {
                var m when m.StartsWith("jan") => 1,
                var m when m.StartsWith("feb") => 2,
                var m when m.StartsWith("mrt") || m.StartsWith("maa") => 3,
                var m when m.StartsWith("apr") => 4,
                var m when m.StartsWith("mei") => 5,
                var m when m.StartsWith("jun") => 6,
                var m when m.StartsWith("jul") => 7,
                var m when m.StartsWith("aug") => 8,
                var m when m.StartsWith("sep") => 9,
                var m when m.StartsWith("okt") => 10,
                var m when m.StartsWith("nov") => 11,
                var m when m.StartsWith("dec") => 12,
                _ => 0
            };
            if (monthNum > 0)
            {
                return $"{abbrev} {dayNum:00}/{monthNum:00}";
            }
        }

        return abbrev;
    }
}
