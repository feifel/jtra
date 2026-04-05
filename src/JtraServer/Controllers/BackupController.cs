using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace JtraServer.Controllers;

public static class BackupControllerExtensions
{
    public static IEndpointRouteBuilder MapBackup(this IEndpointRouteBuilder endpoints, string backupFolderPath)
    {
        var group = endpoints.MapGroup("/api/backup");

        group.MapPost("/", (BackupRequest request) => SaveBackup(request, backupFolderPath));
        group.MapGet("/{email}", (string email) => GetBackup(email, backupFolderPath));

        return endpoints;
    }

    private static async Task<IResult> GetBackup(string email, string backupFolderPath)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return Results.BadRequest("Email is required");
        }

        try
        {
            var safeEmail = string.Join("_", email.Split(Path.GetInvalidFileNameChars()));
            var fileName = $"{safeEmail}.json";
            var filePath = Path.Combine(backupFolderPath, fileName);

            if (!File.Exists(filePath))
            {
                return Results.NotFound($"No backup found for {email}");
            }

            var json = await File.ReadAllTextAsync(filePath);
            var backup = JsonSerializer.Deserialize<BackupRequest>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (backup == null)
            {
                return Results.Problem("Failed to parse backup file");
            }

            var fileInfo = new FileInfo(filePath);
            return Results.Json(new
            {
                backup.Email,
                backup.Settings,
                backup.TimeEntries,
                backup.TaskEntries,
                backup.TicketCache,
                LastModified = fileInfo.LastWriteTimeUtc
            });
        }
        catch (Exception ex)
        {
            return Results.Problem($"Failed to retrieve backup: {ex.Message}");
        }
    }

    private static async Task<IResult> SaveBackup(BackupRequest request, string backupFolderPath)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
        {
            return Results.BadRequest("Email is required for backup");
        }

        try
        {
            if (!Directory.Exists(backupFolderPath))
            {
                Directory.CreateDirectory(backupFolderPath);
            }

            var safeEmail = string.Join("_", request.Email.Split(Path.GetInvalidFileNameChars()));
            var fileName = $"{safeEmail}.json";
            var filePath = Path.Combine(backupFolderPath, fileName);

            var json = JsonSerializer.Serialize(request, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            await File.WriteAllTextAsync(filePath, json);

            return Results.Ok(new { success = true, filePath = fileName });
        }
        catch (Exception ex)
        {
            return Results.Problem($"Failed to save backup: {ex.Message}");
        }
    }
}

public class BackupRequest
{
    public string? Email { get; set; }
    public AppSettingsBackup? Settings { get; set; }
    public List<TimeEntryBackup>? TimeEntries { get; set; }
    public List<TaskEntryBackup>? TaskEntries { get; set; }
    public List<TicketCacheBackup>? TicketCache { get; set; }
}

public class AppSettingsBackup
{
    public string? JiraBaseUrl { get; set; }
    public string? Email { get; set; }
    public string? DefaultTargetHours { get; set; }
    public int NotificationIntervalMinutes { get; set; }
    public int AutoConfirmBreakMinutes { get; set; }
    public bool Use24HourFormat { get; set; }
    public bool CsvExportOldestFirst { get; set; }
    public int BackupIntervalMinutes { get; set; }
}

public class TimeEntryBackup
{
    public int Id { get; set; }
    public string? Date { get; set; }
    public string? StartTime { get; set; }
    public int Type { get; set; }
    public string? Ticket { get; set; }
    public string? Description { get; set; }
    public string? DayTargetHhmm { get; set; }
    public string? DayAccumulatedHhmm { get; set; }
    public double DayAccumulatedDays { get; set; }
    public string? DayDeviationHhmm { get; set; }
    public double DayDeviationDays { get; set; }
    public bool PendingForJiraSubmission { get; set; }
}

public class TaskEntryBackup
{
    public int Id { get; set; }
    public string? Category { get; set; }
    public string? Subcategory { get; set; }
    public string? Ticket { get; set; }
    public string? Description { get; set; }
}

public class TicketCacheBackup
{
    public string? TicketKey { get; set; }
    public string? Summary { get; set; }
    public int UseCount { get; set; }
    public DateTime LastUsedAt { get; set; }
}
