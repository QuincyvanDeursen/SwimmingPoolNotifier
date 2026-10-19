using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using SwimmingPoolScheduleNotifier.Models;
using SwimmingPoolScheduleNotifier.Utilities;

namespace SwimmingPoolScheduleNotifier.Parsers;

public static class ScheduleParser
{
    public const string EventsContainerSelector = "ul.planning-page__events";
    public const string DaySectionSelector = "section.planning-page-section";
    public const string DayTitleSelector = ".planning-page-section__title";
    public const string EventArticleSelector = "article.planning-event";
    public const string EventTitleSelector = ".planning-event__title";

    public static List<DayScheduleModel> ParseDaysFromJson(string json)
    {
        using var jsonDoc = JsonDocument.Parse(json);
        var root = jsonDoc.RootElement;
        if (!root.TryGetProperty("schedule", out var scheduleArr) || scheduleArr.GetArrayLength() == 0)
        {
            throw new InvalidOperationException("Optisport API returned empty schedule array.");
        }

        var days = new List<DayScheduleModel>();
        int daysCount = 0;

        foreach (var dayEl in scheduleArr.EnumerateArray())
        {
            var dayName = dayEl.GetProperty("day").GetString();
            if (string.IsNullOrWhiteSpace(dayName)) continue;

            DateOnly? dayDate = null;
            if (dayEl.TryGetProperty("date", out var dateProp))
            {
                var dateStr = dateProp.GetString();
                if (!string.IsNullOrWhiteSpace(dateStr) &&
                    DateOnly.TryParseExact(dateStr, "dd-MM-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                {
                    dayDate = d;
                }
            }

            var dayModel = new DayScheduleModel { DayTitle = dayName, Date = dayDate };

            if (dayEl.TryGetProperty("events", out var eventsArr))
            {
                foreach (var ev in eventsArr.EnumerateArray())
                {
                    var title = ev.GetProperty("title").GetString() ?? "Banenzwemmen";
                    var internalLoc = ev.TryGetProperty("internalLocation", out var locProp) ? locProp.GetString() : null;
                    var startStr = ev.GetProperty("start").GetString();
                    var endStr = ev.GetProperty("end").GetString();

                    DateTimeOffset? sDto = null;
                    DateTimeOffset? eDto = null;
                    TimeOnly? startTime = null;
                    TimeOnly? endTime = null;
                    string displayTime;

                    if (DateTimeOffset.TryParse(startStr, out var parsedStart))
                    {
                        sDto = parsedStart;
                        var localStart = parsedStart.ToLocalTime();
                        startTime = TimeOnly.FromDateTime(localStart.DateTime);
                        dayModel.Date ??= DateOnly.FromDateTime(localStart.DateTime);

                        if (DateTimeOffset.TryParse(endStr, out var parsedEnd))
                        {
                            eDto = parsedEnd;
                            var localEnd = parsedEnd.ToLocalTime();
                            endTime = TimeOnly.FromDateTime(localEnd.DateTime);
                            displayTime = $"{localStart:HH:mm} - {localEnd:HH:mm}";
                        }
                        else
                        {
                            displayTime = $"{localStart:HH:mm}";
                        }
                    }
                    else
                    {
                        displayTime = "Onbekende tijd";
                    }

                    dayModel.Events.Add(new SwimEventModel
                    {
                        Title = title,
                        InternalLocation = internalLoc,
                        StartDateTime = sDto,
                        EndDateTime = eDto,
                        StartTime = startTime,
                        EndTime = endTime,
                        DisplayTime = displayTime
                    });
                }
            }

            days.Add(dayModel);
            daysCount++;
            if (daysCount >= 7) break;
        }

        return days;
    }

    public static List<DayScheduleModel> ParseDaysFromDom(IDocument document)
    {
        var sections = document.QuerySelectorAll(DaySectionSelector);
        var days = new List<DayScheduleModel>();
        int daysCount = 0;

        foreach (var section in sections)
        {
            var dayTitleEl = section.QuerySelector(DayTitleSelector);
            var dayTitle = StringUtils.CleanText(dayTitleEl?.TextContent);
            if (string.IsNullOrWhiteSpace(dayTitle)) continue;

            DateOnly? dayDate = null;
            var ariaLabel = section.GetAttribute("aria-labelledby");
            if (!string.IsNullOrWhiteSpace(ariaLabel))
            {
                var matchDate = Regex.Match(ariaLabel, @"title-(\d{1,2})-(\d{1,2})-(\d{4})");
                if (matchDate.Success &&
                    int.TryParse(matchDate.Groups[1].Value, out int d) &&
                    int.TryParse(matchDate.Groups[2].Value, out int m) &&
                    int.TryParse(matchDate.Groups[3].Value, out int y))
                {
                    dayDate = new DateOnly(y, m, d);
                }
            }

            var dayModel = new DayScheduleModel { DayTitle = dayTitle, Date = dayDate };

            var articles = section.QuerySelectorAll(EventArticleSelector);
            foreach (var article in articles)
            {
                foreach (var script in article.QuerySelectorAll("script"))
                {
                    script.Remove();
                }

                var times = article.QuerySelectorAll("time");
                DateTimeOffset? sDto = null;
                DateTimeOffset? eDto = null;
                TimeOnly? startTime = null;
                TimeOnly? endTime = null;
                string displayTime;

                if (times.Length >= 2)
                {
                    var startAttr = times[0].GetAttribute("datetime");
                    var endAttr = times[1].GetAttribute("datetime");

                    if (DateTimeOffset.TryParse(startAttr, out var parsedStart))
                    {
                        sDto = parsedStart;
                        startTime = TimeOnly.FromDateTime(parsedStart.ToLocalTime().DateTime);
                        dayModel.Date ??= DateOnly.FromDateTime(parsedStart.ToLocalTime().DateTime);
                    }
                    else if (TimeOnly.TryParse(times[0].TextContent.Trim(), out var t1))
                    {
                        startTime = t1;
                    }

                    if (DateTimeOffset.TryParse(endAttr, out var parsedEnd))
                    {
                        eDto = parsedEnd;
                        endTime = TimeOnly.FromDateTime(parsedEnd.ToLocalTime().DateTime);
                    }
                    else if (TimeOnly.TryParse(times[1].TextContent.Trim(), out var t2))
                    {
                        endTime = t2;
                    }

                    displayTime = $"{StringUtils.CleanText(times[0].TextContent)} - {StringUtils.CleanText(times[1].TextContent)}";
                }
                else
                {
                    displayTime = StringUtils.CleanText(article.QuerySelector(".planning-event__times")?.TextContent);
                    var match = Regex.Match(displayTime, @"(\d{1,2}:\d{2})\s*-\s*(\d{1,2}:\d{2})");
                    if (match.Success)
                    {
                        if (TimeOnly.TryParse(match.Groups[1].Value, out var t1)) startTime = t1;
                        if (TimeOnly.TryParse(match.Groups[2].Value, out var t2)) endTime = t2;
                    }
                }

                var title = StringUtils.CleanText(article.QuerySelector(EventTitleSelector)?.TextContent);
                if (string.IsNullOrWhiteSpace(title))
                {
                    title = "Banenzwemmen";
                }

                var internalLoc = StringUtils.CleanText(article.QuerySelector(".planning-event__location")?.TextContent);

                dayModel.Events.Add(new SwimEventModel
                {
                    Title = title,
                    InternalLocation = internalLoc,
                    StartDateTime = sDto,
                    EndDateTime = eDto,
                    StartTime = startTime,
                    EndTime = endTime,
                    DisplayTime = string.IsNullOrWhiteSpace(displayTime) ? "Onbekende tijd" : displayTime
                });
            }

            days.Add(dayModel);
            daysCount++;
            if (daysCount >= 7) break;
        }

        return days;
    }
}
