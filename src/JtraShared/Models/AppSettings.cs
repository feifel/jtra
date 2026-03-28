namespace JtraShared.Models;

public class AppSettings
{
    public string? JiraBaseUrl { get; set; }
    public string? Pat { get; set; }
    public string DefaultTargetHours { get; set; } = "08:00";
    public int MaxSnoozeHours { get; set; } = 4;
    public int AutoConfirmBreakMinutes { get; set; } = 10;
    public int CacheTtlDays { get; set; } = 7;
    public List<ConfigurableType> ConfigurableTypes { get; set; } = new();
}

public class ConfigurableType
{
    public TaskType Type { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public string? LinkedTicket { get; set; }
}
