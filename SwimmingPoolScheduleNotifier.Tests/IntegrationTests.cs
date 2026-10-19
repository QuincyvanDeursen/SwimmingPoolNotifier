using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SwimmingPoolScheduleNotifier.Services;
using Xunit;
using Xunit.Abstractions;

namespace SwimmingPoolScheduleNotifier.Tests;

public class IntegrationTests
{
    private readonly ITestOutputHelper _output;

    public IntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task SendLiveNotificationToNtfy_BothLocations_ShouldSucceed()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddConsole());
        services.AddHttpClient("PoolScraperClient", client =>
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/129.0.0.0 Safari/537.36");
            client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8");
            client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("nl-NL,nl;q=0.9,en-US;q=0.8,en;q=0.7");
        });
        services.AddHttpClient("NtfyClient");
        services.AddSingleton<IPoolScraperService, PoolScraperService>();
        services.AddSingleton<INtfyNotificationService, NtfyNotificationService>();

        var provider = services.BuildServiceProvider();
        var scraper = provider.GetRequiredService<IPoolScraperService>();
        var notifier = provider.GetRequiredService<INtfyNotificationService>();

        var urls = new[]
        {
            "https://www.optisport.nl/activiteiten/banenzwemmen/zwembad-de-wisselaar-breda#locatie",
            "https://www.optisport.nl/activiteiten/banenzwemmen/zwembad-sonsbeeck-breda#locatie"
        };
        const string ntfyUrl = "https://ntfy.sh/zwem-rooster-breda";

        _output.WriteLine("Scraping schedules for Wisselaar and Sonsbeeck...");
        string schedule = await scraper.ScrapeMultipleLocationsAsync(urls);
        _output.WriteLine("Scraped combined schedule:\n" + schedule);

        Assert.NotEmpty(schedule);
        Assert.Contains("Zwemrooster Breda", schedule);
        Assert.Contains("OCHTEND (07:00 - 09:00)", schedule);
        Assert.Contains("MIDDAG (15:00 - 18:00)", schedule);
        Assert.Contains("WEEKEND", schedule);
        Assert.Contains("Zwembad De Wisselaar:", schedule);
        Assert.Contains("Zwembad Sonsbeeck:", schedule);

        Assert.DoesNotContain("🏊", schedule);
        Assert.DoesNotContain("🌅", schedule);
        Assert.DoesNotContain("☀️", schedule);
        Assert.DoesNotContain("🏖️", schedule);

        _output.WriteLine("Sending live notification to: " + ntfyUrl);
        await notifier.SendNotificationAsync(
            topicUrl: ntfyUrl,
            message: schedule,
            title: "Zwemrooster Breda",
            tags: null);

        _output.WriteLine("Live notification dispatched successfully!");
    }
}
