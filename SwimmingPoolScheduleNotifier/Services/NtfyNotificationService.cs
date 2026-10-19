using System.Text;
using Microsoft.Extensions.Logging;

namespace SwimmingPoolScheduleNotifier.Services;

public class NtfyNotificationService : INtfyNotificationService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<NtfyNotificationService> _logger;

    public NtfyNotificationService(IHttpClientFactory httpClientFactory, ILogger<NtfyNotificationService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task SendNotificationAsync(
        string topicUrl,
        string message,
        string title,
        string? tags = null,
        CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient("NtfyClient");

        using var request = new HttpRequestMessage(HttpMethod.Post, topicUrl)
        {
            Content = new StringContent(message, Encoding.UTF8, "text/plain")
        };

        if (!string.IsNullOrWhiteSpace(title))
        {
            request.Headers.Add("Title", title);
        }

        if (!string.IsNullOrWhiteSpace(tags))
        {
            request.Headers.Add("Tags", tags);
        }

        _logger.LogInformation("Sending notification to ntfy URL: {url} with title: '{title}'", topicUrl, title);

        var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        _logger.LogInformation("Notification successfully posted to ntfy.sh.");
    }
}
