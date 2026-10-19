using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp;
using AngleSharp.Dom;
using Microsoft.Extensions.Logging;

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
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<PoolScraperService> _logger;

    public const string EventsContainerSelector = "ul.planning-page__events";
    public const string DaySectionSelector = "section.planning-page-section";
    public const string DayTitleSelector = ".planning-page-section__title";
    public const string EventArticleSelector = "article.planning-event";
    public const string EventTitleSelector = ".planning-event__title";

    public PoolScraperService(IHttpClientFactory httpClientFactory, ILogger<PoolScraperService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<string> ScrapeScheduleAsync(string url, CancellationToken cancellationToken = default)
    {
        var location = await ScrapeLocationScheduleAsync(url, cancellationToken);
        return FormatSingleLocationSchedule(location);
    }

    public async Task<string> ScrapeMultipleLocationsAsync(IEnumerable<string> urls, CancellationToken cancellationToken = default)
    {
        var locationList = new List<LocationScheduleModel>();

        foreach (var url in urls.Where(u => !string.IsNullOrWhiteSpace(u)))
        {
            try
            {
                var loc = await ScrapeLocationScheduleAsync(url.Trim(), cancellationToken);
                locationList.Add(loc);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to scrape schedule for location URL: {url}", url);
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
        _logger.LogInformation("Starting schedule scraping for URL: {url}", url);

        string html = await FetchHtmlAsync(url, cancellationToken);
        if (string.IsNullOrWhiteSpace(html))
        {
            throw new HttpRequestException($"Received empty HTML response from: {url}");
        }

        var browsingContext = BrowsingContext.New(Configuration.Default);
        var document = await browsingContext.OpenAsync(req => req.Content(html), cancellationToken);

        string locationName = ExtractLocationName(url, document);

        var eventsContainer = document.QuerySelector(EventsContainerSelector);
        if (eventsContainer != null)
        {
            _logger.LogInformation("Found rendered events container for '{location}'. Parsing DOM...", locationName);
            var days = ParseDaysFromDom(document);
            return new LocationScheduleModel { LocationName = locationName, Days = days };
        }

        var planningComponent = document.QuerySelector("#planning-component");
        if (planningComponent != null)
        {
            _logger.LogInformation("Detected client-side Vue component for '{location}'. Querying API...", locationName);
            var locationId = planningComponent.GetAttribute("data-location-id") ?? "2296";
            var activityId = planningComponent.GetAttribute("data-activity-id") ?? "30";
            var days = await FetchDaysFromApiAsync(locationId, activityId, cancellationToken);
            return new LocationScheduleModel { LocationName = locationName, Days = days };
        }

        throw new InvalidOperationException(
            $"Could not find schedule container ('{EventsContainerSelector}') or '#planning-component' in webpage HTML for {url}.");
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

    private static string ExtractLocationName(string url, IDocument? document = null)
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

    private async Task<List<DayScheduleModel>> FetchDaysFromApiAsync(string locationId, string activityId, CancellationToken cancellationToken)
    {
        var cookieContainer = new CookieContainer();
        using var handler = new SocketsHttpHandler
        {
            CookieContainer = cookieContainer,
            UseCookies = true,
            AutomaticDecompression = DecompressionMethods.All
        };

        using var client = new HttpClient(handler);
        client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/129.0.0.0 Safari/537.36");
        client.DefaultRequestHeaders.Add("Accept", "application/json, text/plain, */*");
        client.DefaultRequestHeaders.Add("Referer", "https://www.optisport.nl/");
        client.DefaultRequestHeaders.Add("Origin", "https://www.optisport.nl");
        client.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");

        string token = string.Empty;
        try
        {
            var tokenResp = await client.GetAsync("https://www.optisport.nl/api/optisport/token", cancellationToken);
            if (tokenResp.IsSuccessStatusCode)
            {
                var potentialToken = (await tokenResp.Content.ReadAsStringAsync(cancellationToken)).Trim();
                if (!potentialToken.StartsWith("<") && !potentialToken.Contains("<html") && potentialToken.Length < 100)
                {
                    token = potentialToken;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not fetch CSRF token via HttpClient. Attempting curl fallback...");
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            return await FetchDaysViaCurlFallbackAsync(locationId, activityId);
        }

        var payload = new
        {
            page = 0,
            locationId = int.Parse(locationId),
            activityId = int.Parse(activityId),
            amount = "medium"
        };

        using var postReq = new HttpRequestMessage(HttpMethod.Post, "https://www.optisport.nl/api/optisport/v1/upcomingeventsschedule")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        postReq.Headers.Add("X-CSRF-Token", token);
        postReq.Headers.Add("Referer", "https://www.optisport.nl/");
        postReq.Headers.Add("Origin", "https://www.optisport.nl");
        postReq.Headers.Add("X-Requested-With", "XMLHttpRequest");

        var apiResp = await client.SendAsync(postReq, cancellationToken);
        if (!apiResp.IsSuccessStatusCode)
        {
            return await FetchDaysViaCurlFallbackAsync(locationId, activityId);
        }

        var json = await apiResp.Content.ReadAsStringAsync(cancellationToken);
        if (json.StartsWith("<") || json.Contains("<html"))
        {
            return await FetchDaysViaCurlFallbackAsync(locationId, activityId);
        }

        return ParseDaysFromJson(json);
    }

    private static string CurlExecutable => OperatingSystem.IsWindows() ? "curl.exe" : "curl";

    private static async Task<List<DayScheduleModel>> FetchDaysViaCurlFallbackAsync(string locationId, string activityId)
    {
        string cookieFile = Path.Combine(Path.GetTempPath(), $"optisport_cookies_{Guid.NewGuid():N}.txt");
        try
        {
            var tokenPsi = new ProcessStartInfo
            {
                FileName = CurlExecutable,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            tokenPsi.ArgumentList.Add("-s");
            tokenPsi.ArgumentList.Add("-c");
            tokenPsi.ArgumentList.Add(cookieFile);
            tokenPsi.ArgumentList.Add("-b");
            tokenPsi.ArgumentList.Add(cookieFile);
            tokenPsi.ArgumentList.Add("-H");
            tokenPsi.ArgumentList.Add("User-Agent: Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/129.0.0.0 Safari/537.36");
            tokenPsi.ArgumentList.Add("-H");
            tokenPsi.ArgumentList.Add("Accept: application/json, text/plain, */*");
            tokenPsi.ArgumentList.Add("-H");
            tokenPsi.ArgumentList.Add("Referer: https://www.optisport.nl/");
            tokenPsi.ArgumentList.Add("-H");
            tokenPsi.ArgumentList.Add("Origin: https://www.optisport.nl");
            tokenPsi.ArgumentList.Add("-H");
            tokenPsi.ArgumentList.Add("X-Requested-With: XMLHttpRequest");
            tokenPsi.ArgumentList.Add("https://www.optisport.nl/api/optisport/token");

            using var tokenProc = Process.Start(tokenPsi) ?? throw new InvalidOperationException("Failed to run curl.");
            string token = (await tokenProc.StandardOutput.ReadToEndAsync()).Trim();
            await tokenProc.WaitForExitAsync();

            if (token.StartsWith("<") || token.Contains("<html") || string.IsNullOrWhiteSpace(token))
            {
                throw new InvalidOperationException($"Optisport token endpoint did not return a valid CSRF token. Output was: {(token.Length > 150 ? token.Substring(0, 150) + "..." : token)}");
            }

            string jsonPayload = $"{{\"page\":0,\"locationId\":{locationId},\"activityId\":{activityId},\"amount\":\"medium\"}}";
            var apiPsi = new ProcessStartInfo
            {
                FileName = CurlExecutable,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            apiPsi.ArgumentList.Add("-s");
            apiPsi.ArgumentList.Add("-c");
            apiPsi.ArgumentList.Add(cookieFile);
            apiPsi.ArgumentList.Add("-b");
            apiPsi.ArgumentList.Add(cookieFile);
            apiPsi.ArgumentList.Add("-X");
            apiPsi.ArgumentList.Add("POST");
            apiPsi.ArgumentList.Add("https://www.optisport.nl/api/optisport/v1/upcomingeventsschedule");
            apiPsi.ArgumentList.Add("-H");
            apiPsi.ArgumentList.Add("User-Agent: Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/129.0.0.0 Safari/537.36");
            apiPsi.ArgumentList.Add("-H");
            apiPsi.ArgumentList.Add("Accept: application/json, text/plain, */*");
            apiPsi.ArgumentList.Add("-H");
            apiPsi.ArgumentList.Add("Referer: https://www.optisport.nl/");
            apiPsi.ArgumentList.Add("-H");
            apiPsi.ArgumentList.Add("Origin: https://www.optisport.nl");
            apiPsi.ArgumentList.Add("-H");
            apiPsi.ArgumentList.Add("X-Requested-With: XMLHttpRequest");
            apiPsi.ArgumentList.Add("-H");
            apiPsi.ArgumentList.Add("Content-Type: application/json");
            apiPsi.ArgumentList.Add("-H");
            apiPsi.ArgumentList.Add($"X-CSRF-Token: {token}");
            apiPsi.ArgumentList.Add("--data-raw");
            apiPsi.ArgumentList.Add(jsonPayload);

            using var apiProc = Process.Start(apiPsi) ?? throw new InvalidOperationException("Failed to run curl API call.");
            string json = await apiProc.StandardOutput.ReadToEndAsync();
            await apiProc.WaitForExitAsync();

            if (json.StartsWith("<") || json.Contains("<html") || string.IsNullOrWhiteSpace(json))
            {
                throw new InvalidOperationException($"Optisport upcomingeventsschedule API returned non-JSON/HTML output: {(json.Length > 150 ? json.Substring(0, 150) + "..." : json)}");
            }

            return ParseDaysFromJson(json);
        }
        finally
        {
            if (File.Exists(cookieFile))
            {
                try { File.Delete(cookieFile); } catch { /* ignore */ }
            }
        }
    }

    private static List<DayScheduleModel> ParseDaysFromJson(string json)
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

    private async Task<string> FetchHtmlAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("PoolScraperClient");
            var response = await client.GetAsync(url, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadAsStringAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "HttpClient GET to {url} failed. Trying curl process fallback...", url);
        }

        var psi = new ProcessStartInfo
        {
            FileName = CurlExecutable,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("-s");
        psi.ArgumentList.Add("-L");
        psi.ArgumentList.Add("-A");
        psi.ArgumentList.Add("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/129.0.0.0 Safari/537.36");
        psi.ArgumentList.Add(url);

        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Failed to launch curl process.");
        string html = await proc.StandardOutput.ReadToEndAsync();
        await proc.WaitForExitAsync(cancellationToken);
        return html;
    }

    private static string CleanText(string? text) =>
        string.IsNullOrWhiteSpace(text) ? string.Empty : Regex.Replace(text, @"\s+", " ").Trim();

    private static string CleanRawText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(l => l.Trim())
                        .Where(l => !string.IsNullOrEmpty(l));
        return string.Join(Environment.NewLine, lines);
    }
}
