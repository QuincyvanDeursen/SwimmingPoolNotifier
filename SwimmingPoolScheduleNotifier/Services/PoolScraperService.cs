using AngleSharp;
using AngleSharp.Dom;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Playwright;
using SwimmingPoolScheduleNotifier.Formatters;
using SwimmingPoolScheduleNotifier.Models;
using SwimmingPoolScheduleNotifier.Parsers;
using SwimmingPoolScheduleNotifier.Utilities;

namespace SwimmingPoolScheduleNotifier.Services;

public class PoolScraperService : IPoolScraperService
{
    private readonly IHttpClientFactory? _httpClientFactory;
    private readonly ILogger<PoolScraperService> _logger;

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

        return ScheduleFormatter.FormatCombinedLocationsSchedule(locationList);
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
                        // Ignore response read exceptions during teardown
                    }
                }
            };

            await page.GotoAsync(url, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = 60000
            });

            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (capturedScheduleJson == null && DateTime.UtcNow < deadline)
            {
                await Task.Delay(300, cancellationToken);
            }

            string locationName = LocationExtractor.ExtractLocationName(url);

            if (!string.IsNullOrWhiteSpace(capturedScheduleJson))
            {
                _logger.LogInformation("Successfully intercepted API schedule response for '{location}'.", locationName);
                var days = ScheduleParser.ParseDaysFromJson(capturedScheduleJson);
                return new LocationScheduleModel { LocationName = locationName, Days = days };
            }

            // Fallback: check if the DOM container was rendered directly
            try
            {
                await page.WaitForSelectorAsync(ScheduleParser.EventsContainerSelector, new PageWaitForSelectorOptions { Timeout = 5000 });
                var content = await page.ContentAsync();
                var browsingContext = BrowsingContext.New(Configuration.Default);
                var document = await browsingContext.OpenAsync(req => req.Content(content), cancellationToken);
                var domLocationName = LocationExtractor.ExtractLocationName(url, document);
                var days = ScheduleParser.ParseDaysFromDom(document);
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

    // Forwarder methods for backward-compatibility with tests & callers
    public List<DayScheduleModel> ParseDaysFromDom(IDocument document) =>
        ScheduleParser.ParseDaysFromDom(document);

    public static string FormatSingleLocationSchedule(LocationScheduleModel location) =>
        ScheduleFormatter.FormatSingleLocationSchedule(location);

    public static string FormatCombinedLocationsSchedule(IReadOnlyList<LocationScheduleModel> locations) =>
        ScheduleFormatter.FormatCombinedLocationsSchedule(locations);
}
