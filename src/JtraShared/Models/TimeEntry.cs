namespace JtraShared.Models;

public class TimeEntry
{
    public int Id { get; set; }
    public string Date { get; set; } = string.Empty;
    public string StartTime { get; set; } = string.Empty;
    public TaskType Type { get; set; } = TaskType.Ticket;
    public string? Ticket { get; set; }
    public string? Description { get; set; }
    public string? Duration { get; set; }
    public string? DayAccumulatedHhmm { get; set; }
    public double? DayAccumulatedDays { get; set; }
    public string DayTargetHhmm { get; set; } = "08:00";
    public string? DayDeviationHhmm { get; set; }
    public double? DayDeviationDays { get; set; }
    public bool SubmittedToJira { get; set; }
}
