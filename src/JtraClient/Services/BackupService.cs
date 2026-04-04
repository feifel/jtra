using System.Net.Http.Json;
using System.Text.Json;
using JtraShared.Models;
using Microsoft.JSInterop;

namespace JtraClient.Services;

public class BackupService
{
    private readonly HttpClient _httpClient;
    private readonly IndexedDbService _indexedDb;
    private readonly AppState _appState;
    private readonly ILogger<BackupService> _logger;

    public BackupService(HttpClient httpClient, IndexedDbService indexedDb, AppState appState, ILogger<BackupService> logger)
    {
        _httpClient = httpClient;
        _indexedDb = indexedDb;
        _appState = appState;
        _logger = logger;
    }

    public async Task<BackupPayload> CreateBackupPayloadAsync()
    {
        var settings = _appState.Settings;
        var timeEntries = await _indexedDb.GetTimeEntriesAsync();
        var taskEntries = await _indexedDb.GetTaskEntriesAsync();
        var ticketCache = await _indexedDb.GetAllCachedTicketsAsync();

        return new BackupPayload
        {
            Email = settings.Email,
            Settings = new SettingsBackup
            {
                JiraBaseUrl = settings.JiraBaseUrl,
                Email = settings.Email,
                DefaultTargetHours = settings.DefaultTargetHours,
                NotificationIntervalMinutes = settings.NotificationIntervalMinutes,
                AutoConfirmBreakMinutes = settings.AutoConfirmBreakMinutes,
                Use24HourFormat = settings.Use24HourFormat,
                CsvExportOldestFirst = settings.CsvExportOldestFirst,
                BackupIntervalMinutes = settings.BackupIntervalMinutes
            },
            TimeEntries = timeEntries.Select(e => new TimeEntryBackup
            {
                Id = e.Id,
                Date = e.Date,
                StartTime = e.StartTime,
                Type = (int)e.Type,
                Ticket = e.Ticket,
                Description = e.Description,
                DayTargetHhmm = e.DayTargetHhmm,
                DayAccumulatedHhmm = e.DayAccumulatedHhmm,
                DayAccumulatedDays = e.DayAccumulatedDays ?? 0,
                DayDeviationHhmm = e.DayDeviationHhmm,
                DayDeviationDays = e.DayDeviationDays ?? 0,
                PendingForJiraSubmission = e.PendingForJiraSubmission
            }).ToList(),
            TaskEntries = taskEntries.Select(e => new TaskEntryBackup
            {
                Id = e.Id,
                Category = e.Category,
                Subcategory = e.Subcategory,
                Ticket = e.Ticket,
                Description = e.Description
            }).ToList(),
            TicketCache = ticketCache.Select(t => new TicketCacheBackup
            {
                TicketKey = t.TicketKey,
                Summary = t.Summary,
                UseCount = t.UseCount,
                LastUsedAt = t.LastUsedAt
            }).ToList()
        };
    }

    public async Task<bool> SendBackupAsync()
    {
        try
        {
            var payload = await CreateBackupPayloadAsync();

            if (string.IsNullOrWhiteSpace(payload.Email))
            {
                _logger.LogWarning("Backup skipped: no email configured");
                return false;
            }

            var response = await _httpClient.PostAsJsonAsync("/api/backup", payload);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Backup completed successfully");
                return true;
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync();
                _logger.LogError("Backup failed: {StatusCode} {Error}", response.StatusCode, error);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Backup failed with exception");
            return false;
        }
    }

    public async Task<BackupPayload?> FetchBackupAsync()
    {
        var email = _appState.Settings.Email;

        if (string.IsNullOrWhiteSpace(email))
        {
            _logger.LogWarning("Cannot fetch backup: no email configured");
            return null;
        }

        try
        {
            var safeEmail = Uri.EscapeDataString(email);
            var response = await _httpClient.GetAsync($"/api/backup/{safeEmail}");

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogInformation("No backup found for email: {Email}", email);
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Failed to fetch backup: {StatusCode}", response.StatusCode);
                return null;
            }

            var payload = await response.Content.ReadFromJsonAsync<BackupPayload>();
            return payload;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch backup");
            return null;
        }
    }

    public async Task<bool> RestoreReplaceAsync(BackupPayload payload)
    {
        try
        {
            await _indexedDb.ClearAllDataAsync();

            if (payload.Settings != null)
            {
                var settings = new AppSettings
                {
                    JiraBaseUrl = payload.Settings.JiraBaseUrl,
                    Email = payload.Settings.Email,
                    DefaultTargetHours = payload.Settings.DefaultTargetHours ?? "08:00",
                    NotificationIntervalMinutes = payload.Settings.NotificationIntervalMinutes,
                    AutoConfirmBreakMinutes = payload.Settings.AutoConfirmBreakMinutes,
                    Use24HourFormat = payload.Settings.Use24HourFormat,
                    CsvExportOldestFirst = payload.Settings.CsvExportOldestFirst,
                    BackupIntervalMinutes = payload.Settings.BackupIntervalMinutes
                };
                await _indexedDb.SaveSettingsAsync(settings);
            }

            if (payload.TimeEntries != null)
            {
                foreach (var entry in payload.TimeEntries)
                {
                    var timeEntry = new TimeEntry
                    {
                        Date = entry.Date ?? string.Empty,
                        StartTime = entry.StartTime ?? string.Empty,
                        Type = (TaskType)entry.Type,
                        Ticket = entry.Ticket,
                        Description = entry.Description,
                        DayTargetHhmm = entry.DayTargetHhmm ?? "08:00",
                        DayAccumulatedHhmm = entry.DayAccumulatedHhmm,
                        DayAccumulatedDays = entry.DayAccumulatedDays,
                        DayDeviationHhmm = entry.DayDeviationHhmm,
                        DayDeviationDays = entry.DayDeviationDays,
                        PendingForJiraSubmission = entry.PendingForJiraSubmission
                    };
                    await _indexedDb.AddTimeEntryAsync(timeEntry);
                }
            }

            if (payload.TaskEntries != null)
            {
                foreach (var entry in payload.TaskEntries)
                {
                    var taskEntry = new TaskEntry
                    {
                        Category = entry.Category ?? string.Empty,
                        Subcategory = entry.Subcategory,
                        Ticket = entry.Ticket,
                        Description = entry.Description
                    };
                    await _indexedDb.AddTaskEntryAsync(taskEntry);
                }
            }

            if (payload.TicketCache != null)
            {
                foreach (var ticket in payload.TicketCache)
                {
                    var cache = new TicketCache
                    {
                        TicketKey = ticket.TicketKey ?? string.Empty,
                        Summary = ticket.Summary ?? string.Empty,
                        UseCount = ticket.UseCount,
                        LastUsedAt = ticket.LastUsedAt
                    };
                    await _indexedDb.AddTicketToCacheAsync(cache);
                }
            }

            _logger.LogInformation("Restore (replace) completed successfully");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Restore (replace) failed");
            return false;
        }
    }

    public async Task<bool> RestoreMergeAsync(BackupPayload payload)
    {
        try
        {
            if (payload.TimeEntries != null)
            {
                foreach (var entry in payload.TimeEntries)
                {
                    var timeEntry = new TimeEntry
                    {
                        Date = entry.Date ?? string.Empty,
                        StartTime = entry.StartTime ?? string.Empty,
                        Type = (TaskType)entry.Type,
                        Ticket = entry.Ticket,
                        Description = entry.Description,
                        DayTargetHhmm = entry.DayTargetHhmm ?? "08:00",
                        DayAccumulatedHhmm = entry.DayAccumulatedHhmm,
                        DayAccumulatedDays = entry.DayAccumulatedDays,
                        DayDeviationHhmm = entry.DayDeviationHhmm,
                        DayDeviationDays = entry.DayDeviationDays,
                        PendingForJiraSubmission = entry.PendingForJiraSubmission
                    };
                    await _indexedDb.AddTimeEntryAsync(timeEntry);
                }
            }

            if (payload.TaskEntries != null)
            {
                foreach (var entry in payload.TaskEntries)
                {
                    var taskEntry = new TaskEntry
                    {
                        Category = entry.Category ?? string.Empty,
                        Subcategory = entry.Subcategory,
                        Ticket = entry.Ticket,
                        Description = entry.Description
                    };
                    await _indexedDb.AddTaskEntryAsync(taskEntry);
                }
            }

            if (payload.TicketCache != null)
            {
                var existingTickets = await _indexedDb.GetAllCachedTicketsAsync();
                foreach (var ticket in payload.TicketCache)
                {
                    var existing = existingTickets.FirstOrDefault(t =>
                        string.Equals(t.TicketKey, ticket.TicketKey, StringComparison.OrdinalIgnoreCase));

                    if (existing != null)
                    {
                        existing.UseCount += ticket.UseCount;
                        if (ticket.LastUsedAt > existing.LastUsedAt)
                        {
                            existing.LastUsedAt = ticket.LastUsedAt;
                        }
                        if (!string.IsNullOrWhiteSpace(ticket.Summary) && string.IsNullOrWhiteSpace(existing.Summary))
                        {
                            existing.Summary = ticket.Summary;
                        }
                        await _indexedDb.UpdateTicketCacheAsync(existing);
                    }
                    else
                    {
                        var cache = new TicketCache
                        {
                            TicketKey = ticket.TicketKey ?? string.Empty,
                            Summary = ticket.Summary ?? string.Empty,
                            UseCount = ticket.UseCount,
                            LastUsedAt = ticket.LastUsedAt
                        };
                        await _indexedDb.AddTicketToCacheAsync(cache);
                    }
                }
            }

            _logger.LogInformation("Restore (merge) completed successfully");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Restore (merge) failed");
            return false;
        }
    }
}

public class BackupPayload
{
    public string? Email { get; set; }
    public SettingsBackup? Settings { get; set; }
    public List<TimeEntryBackup>? TimeEntries { get; set; }
    public List<TaskEntryBackup>? TaskEntries { get; set; }
    public List<TicketCacheBackup>? TicketCache { get; set; }
    public DateTime LastModified { get; set; }
}

public class SettingsBackup
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
