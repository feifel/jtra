namespace JtraShared.Models;

public class AppSettings
{
    public string? JiraBaseUrl { get; set; }
    public string? Pat { get; set; }
    public string DefaultTargetHours { get; set; } = "08:00";
    public int NotificationIntervalMinutes { get; set; } = 15;
    public bool Use24HourFormat { get; set; } = true;
    public string? Email { get; set; }
    public int BackupIntervalMinutes { get; set; } = 30;
    public int AutoConfirmBreakMinutes { get; set; } = 10;
    public bool CsvExportOldestFirst { get; set; } = true;
    public List<ConfigurableType> ConfigurableTypes { get; set; } = new();
}

public class ConfigurableType
{
    public TaskType Type { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public string? LinkedTicket { get; set; }
    public string? Description { get; set; }
}
