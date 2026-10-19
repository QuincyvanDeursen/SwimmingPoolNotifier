namespace SwimmingPoolScheduleNotifier.Models;

public class LocationScheduleModel
{
    public required string LocationName { get; set; }
    public List<DayScheduleModel> Days { get; set; } = new();
}
