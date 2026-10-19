namespace SwimmingPoolScheduleNotifier.Models;

public class DayScheduleModel
{
    public required string DayTitle { get; set; }
    public DateOnly? Date { get; set; }
    public List<SwimEventModel> Events { get; set; } = new();
}
