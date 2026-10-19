namespace SwimmingPoolScheduleNotifier.Models;

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
