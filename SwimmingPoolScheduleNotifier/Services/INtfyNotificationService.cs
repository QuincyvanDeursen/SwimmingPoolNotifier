namespace SwimmingPoolScheduleNotifier.Services;

public interface INtfyNotificationService
{
    Task SendNotificationAsync(string topicUrl, string message, string title, string? tags = null, CancellationToken cancellationToken = default);
}
