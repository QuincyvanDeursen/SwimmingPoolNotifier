using SwimmingPoolScheduleNotifier.Models;

namespace SwimmingPoolScheduleNotifier.Services;

public interface IPoolScraperService
{
    Task<string> ScrapeScheduleAsync(string url, CancellationToken cancellationToken = default);
    Task<LocationScheduleModel> ScrapeLocationScheduleAsync(string url, CancellationToken cancellationToken = default);
    Task<string> ScrapeMultipleLocationsAsync(IEnumerable<string> urls, CancellationToken cancellationToken = default);
}
