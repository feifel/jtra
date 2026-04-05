namespace JtraShared.Models;

public class TimeEntry : IEquatable<TimeEntry>
{
    public int Id { get; set; }
    public string Date { get; set; } = string.Empty;
    public string StartTime { get; set; } = string.Empty;
    public TaskType Type { get; set; } = TaskType.Ticket;
    public string? Ticket { get; set; }
    public string? Description { get; set; }
    public string? DayAccumulatedHhmm { get; set; }
    public double? DayAccumulatedDays { get; set; }
    public string DayTargetHhmm { get; set; } = "08:00";
    public string? DayDeviationHhmm { get; set; }
    public double? DayDeviationDays { get; set; }
    public bool PendingForJiraSubmission { get; set; }

    public bool Equals(TimeEntry? other) => other is not null && Id == other.Id;
    public override bool Equals(object? obj) => Equals(obj as TimeEntry);
    public override int GetHashCode() => Id.GetHashCode();
}