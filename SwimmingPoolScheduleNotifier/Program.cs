using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SwimmingPoolScheduleNotifier.Services;

var services = new ServiceCollection();

// Setup console logging
services.AddLogging(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Information);
});

// Configure HttpClient with realistic browser headers to prevent 403 Forbidden blocks
services.AddHttpClient("PoolScraperClient", client =>
{
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/129.0.0.0 Safari/537.36");
    client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8");
    client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("nl-NL,nl;q=0.9,en-US;q=0.8,en;q=0.7");
});

// Configure HttpClient for ntfy.sh push notifications
services.AddHttpClient("NtfyClient");

// Register application services
services.AddSingleton<IPoolScraperService, PoolScraperService>();
services.AddSingleton<INtfyNotificationService, NtfyNotificationService>();

var serviceProvider = services.BuildServiceProvider();
var logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Program");
var scraperService = serviceProvider.GetRequiredService<IPoolScraperService>();
var notificationService = serviceProvider.GetRequiredService<INtfyNotificationService>();

// Read target locations and ntfy topic from environment variables (GitHub Actions Secrets/Env)
string defaultUrls = "https://www.optisport.nl/activiteiten/banenzwemmen/zwembad-de-wisselaar-breda#locatie,https://www.optisport.nl/activiteiten/banenzwemmen/zwembad-sonsbeeck-breda#locatie";
string configuredUrls = Environment.GetEnvironmentVariable("POOL_SCHEDULE_URLS") ?? defaultUrls;
var urls = configuredUrls.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

string ntfyUrl = Environment.GetEnvironmentVariable("NTFY_TOPIC_URL") ?? "https://ntfy.sh/zwem-rooster-breda";

logger.LogInformation("Swimming Pool Schedule Job started at: {time}", DateTimeOffset.Now);

try
{
    logger.LogInformation("Scraping schedule for {count} location(s)...", urls.Length);
    string scheduleText = await scraperService.ScrapeMultipleLocationsAsync(urls);

    if (string.IsNullOrWhiteSpace(scheduleText))
    {
        throw new InvalidOperationException("Scraped schedule content is empty.");
    }

    logger.LogInformation("Scraping successful. Dispatching push notification to: {ntfyUrl}", ntfyUrl);

    await notificationService.SendNotificationAsync(
        topicUrl: ntfyUrl,
        message: scheduleText,
        title: "Zwemrooster Breda",
        tags: null);

    logger.LogInformation("Successfully sent weekly swimming pool schedule to ntfy.sh!");
}
catch (Exception ex)
{
    logger.LogError(ex, "Error occurred while executing swimming schedule job.");

    try
    {
        const string fallbackMessage = "Let op: De scraper voor het zwemrooster is stuk. Controleer de website lay-out of broncode.";

        await notificationService.SendNotificationAsync(
            topicUrl: ntfyUrl,
            message: fallbackMessage,
            title: "Fout: Zwemrooster Scraper",
            tags: null);

        logger.LogInformation("Fallback error alert sent to ntfy.sh.");
    }
    catch (Exception notifyEx)
    {
        logger.LogError(notifyEx, "Failed to send fallback notification to ntfy.sh.");
    }

    Environment.Exit(1);
}
