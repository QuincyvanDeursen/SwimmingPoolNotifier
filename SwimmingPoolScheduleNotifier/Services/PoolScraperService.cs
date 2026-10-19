using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp;
using AngleSharp.Dom;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Playwright;

namespace SwimmingPoolScheduleNotifier.Services;

public class SwimEventModel
{
    public required string Title { get; set; }
    public string? InternalLocation { get; set; }
    public DateTimeOffset? StartDateTime { get; set; }
    public DateTimeOffset? EndDateTime { get; set; }
    public TimeOnly? StartTime { get; set; }
    public TimeOnly? EndTime { get; set; }
    public required string DisplayTime { get; set; }

    public string FullTitle => string.IsNullOrWhiteSpace(InternalLocation)
        ? Title
        : $"{Title} ({InternalLocation})";

    public bool IsBetween(TimeOnly rangeStart, TimeOnly rangeEnd)
    {
        if (StartTime == null) return false;
        if (EndTime != null)
        {
            return StartTime < rangeEnd && EndTime > rangeStart;
        }
        return StartTime >= rangeStart && StartTime <= rangeEnd;
    }
}

public class DayScheduleModel
{
    public required string DayTitle { get; set; }
    public DateOnly? Date { get; set; }
    public List<SwimEventModel> Events { get; set; } = new();
}

public class LocationScheduleModel
{
    public required string LocationName { get; set; }
    public List<DayScheduleModel> Days { get; set; } = new();
}

public class PoolScraperService : IPoolScraperService
{
    private readonly IHttpClientFactory? _httpClientFactory;
    private readonly ILogger<PoolScraperService> _logger;

    public const string EventsContainerSelector = "ul.planning-page__events";
    public const string DaySectionSelector = "section.planning-page-section";
    public const string DayTitleSelector = ".planning-page-section__title";
    public const string EventArticleSelector = "article.planning-event";
    public const string EventTitleSelector = ".planning-event__title";

    public PoolScraperService(IHttpClientFactory? httpClientFactory = null, ILogger<PoolScraperService>? logger = null)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger ?? NullLogger<PoolScraperService>.Instance;
    }

    public async Task<string> ScrapeScheduleAsync(string url, CancellationToken cancellationToken = default)
    {
        var location = await ScrapeLocationScheduleAsync(url, cancellationToken);
        return FormatSingleLocationSchedule(location);
    }

    public async Task<string> ScrapeMultipleLocationsAsync(IEnumerable<string> urls, CancellationToken cancellationToken = default)
    {
        var locationList = new List<LocationScheduleModel>();

        using var playwright = await Playwright.CreateAsync();
        IBrowser? browser = null;

        try
        {
            browser = await LaunchBrowserAsync(playwright);

            foreach (var url in urls.Where(u => !string.IsNullOrWhiteSpace(u)))
            {
                try
                {
                    var loc = await ScrapeLocationWithBrowserAsync(browser, url.Trim(), cancellationToken);
                    locationList.Add(loc);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to scrape schedule for location URL: {url}", url);
                }
            }
        }
        finally
        {
            if (browser != null)
            {
                await browser.CloseAsync();
                await browser.DisposeAsync();
            }
        }

        if (locationList.Count == 0)
        {
            throw new InvalidOperationException("Could not retrieve schedule for any of the configured swimming pool locations.");
        }

        return FormatCombinedLocationsSchedule(locationList);
    }

    public async Task<LocationScheduleModel> ScrapeLocationScheduleAsync(string url, CancellationToken cancellationToken = default)
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await LaunchBrowserAsync(playwright);
        return await ScrapeLocationWithBrowserAsync(browser, url, cancellationToken);
    }

    private static async Task<IBrowser> LaunchBrowserAsync(IPlaywright playwright)
    {
        var launchOptions = new BrowserTypeLaunchOptions
        {
            Headless = true,
            Args = new[]
            {
                "--no-sandbox",
                "--disable-setuid-sandbox",
                "--disable-blink-features=AutomationControlled"
            }
        };

        try
        {
            return await playwright.Chromium.LaunchAsync(launchOptions);
        }
        catch (PlaywrightException)
        {
            // Auto-install chromium if not yet present
            Microsoft.Playwright.Program.Main(new[] { "install", "chromium" });
            return await playwright.Chromium.LaunchAsync(launchOptions);
        }
    }

    private async Task<LocationScheduleModel> ScrapeLocationWithBrowserAsync(IBrowser browser, string url, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting schedule scraping for URL: {url}", url);

        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/129.0.0.0 Safari/537.36",
            Locale = "nl-NL",
            ViewportSize = new ViewportSize { Width = 1280, Height = 800 }
        });

        try
        {
            var page = await context.NewPageAsync();
            string? capturedScheduleJson = null;

            page.Response += async (_, response) =>
            {
                if (response.Url.Contains("upcomingeventsschedule", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var body = await response.TextAsync();
                        if (!string.IsNullOrWhiteSpace(body) && !body.StartsWith("<"))
                        {
                            capturedScheduleJson = body;
                        }
                    }
                    catch
                    {
                        // Ignore response read exceptions during page teardown
                    }
                }
            };

            await page.GotoAsync(url, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = 60000
            });

            // Wait for intercepted API response
            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (capturedScheduleJson == null && DateTime.UtcNow < deadline)
            {
                await Task.Delay(300, cancellationToken);
            }

            string locationName = ExtractLocationName(url);

            if (!string.IsNullOrWhiteSpace(capturedScheduleJson))
            {
                _logger.LogInformation("Successfully intercepted API schedule response for '{location}'.", locationName);
                var days = ParseDaysFromJson(capturedScheduleJson);
                return new LocationScheduleModel { LocationName = locationName, Days = days };
            }

            // Fallback: check if the DOM container was rendered directly
            try
            {
                await page.WaitForSelectorAsync(EventsContainerSelector, new PageWaitForSelectorOptions { Timeout = 5000 });
                var content = await page.ContentAsync();
                var browsingContext = BrowsingContext.New(Configuration.Default);
                var document = await browsingContext.OpenAsync(req => req.Content(content), cancellationToken);
                var domLocationName = ExtractLocationName(url, document);
                var days = ParseDaysFromDom(document);
                return new LocationScheduleModel { LocationName = domLocationName, Days = days };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "DOM container not found for '{location}'.", locationName);
            }

            throw new InvalidOperationException($"Could not retrieve schedule data for {url}. No API response intercepted or DOM container found.");
        }
        finally
        {
            await context.CloseAsync();
            await context.DisposeAsync();
        }
    }

    public List<DayScheduleModel> ParseDaysFromDom(IDocument document)
    {
        var sections = document.QuerySelectorAll(DaySectionSelector);
        var days = new List<DayScheduleModel>();
        int daysCount = 0;

        foreach (var section in sections)
        {
            var dayTitleEl = section.QuerySelector(DayTitleSelector);
            var dayTitle = CleanText(dayTitleEl?.TextContent);
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

                    displayTime = $"{CleanText(times[0].TextContent)} - {CleanText(times[1].TextContent)}";
                }
                else
                {
                    displayTime = CleanText(article.QuerySelector(".planning-event__times")?.TextContent);
                    var match = Regex.Match(displayTime, @"(\d{1,2}:\d{2})\s*-\s*(\d{1,2}:\d{2})");
                    if (match.Success)
                    {
                        if (TimeOnly.TryParse(match.Groups[1].Value, out var t1)) startTime = t1;
                        if (TimeOnly.TryParse(match.Groups[2].Value, out var t2)) endTime = t2;
                    }
                }

                var title = CleanText(article.QuerySelector(EventTitleSelector)?.TextContent);
                if (string.IsNullOrWhiteSpace(title))
                {
                    title = "Banenzwemmen";
                }

                var internalLoc = CleanText(article.QuerySelector(".planning-event__location")?.TextContent);

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

        static bool IsWeekend(string dayTitle, DateOnly? date)
        {
            if (dayTitle.Contains("zaterdag", StringComparison.OrdinalIgnoreCase) ||
                dayTitle.Contains("zondag", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return date.HasValue && (date.Value.DayOfWeek == DayOfWeek.Saturday || date.Value.DayOfWeek == DayOfWeek.Sunday);
        }

        // Limit to 7 days from the earliest available date
        DateOnly? minDate = locations
            .SelectMany(l => l.Days)
            .Where(d => d.Date.HasValue)
            .Select(d => d.Date)
            .Min();

        DateOnly? maxDate = minDate?.AddDays(6);

        static string GetDayAbbrev(string dayTitle, DateOnly? date)
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

        static string FormatDayWithDate(string dayTitle, DateOnly? date)
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

    private static string CleanText(string? text) =>
        string.IsNullOrWhiteSpace(text) ? string.Empty : Regex.Replace(text, @"\s+", " ").Trim();
}
