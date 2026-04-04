using JtraShared.Models;
using Microsoft.JSInterop;

namespace JtraClient.Services;

public class AppState
{
    private readonly IndexedDbService _indexedDb;
    private readonly JiraTicketService _jiraTicketService;
    private readonly ILogger<AppState> _logger;

    public event Action? OnChange;

    public List<TimeEntry> TodayEntries { get; private set; } = new();
    public List<TimeEntry> AllEntries { get; private set; } = new();
    public List<TaskEntry> AllTaskEntries { get; private set; } = new();
    public AppSettings Settings { get; private set; } = new();
    
    public TimeEntry? CurrentTask { get; private set; }
    public DateTime? NextCheckInTime { get; private set; }
    public DateTime? SnoozedUntil { get; private set; }
    public DateTime? LastSelectedEstimatedEnd { get; private set; }
    public bool ShowCheckInPopup { get; private set; }
    public bool IsConnectedToServer { get; private set; }
    public bool IsTimerTriggeredPopup { get; private set; }
    public string? LastServerTickTime { get; private set; }
    public DateTime? LastServerTickReceivedAt { get; private set; }
    public DateTime? LastNotificationAttemptAt { get; private set; }
    public string? LastNotificationStatus { get; private set; }
    
    public TimeSpan TodayAccumulated { get; private set; }
    public TimeSpan TodayTarget { get; private set; } = TimeSpan.FromHours(8);
    public TimeSpan TodayDeviation => TodayAccumulated - TodayTarget;

    public IReadOnlyList<TimeEntry> TodayTimeEntries => TodayEntries;

    private static readonly IReadOnlyList<TaskEntry> SeedTaskEntries = new[]
    {
        new TaskEntry { Category = "Default"},
        new TaskEntry { Category = "Break" },
        new TaskEntry { Category = "Holiday", Subcategory = "Public", Description = "Public Holiday" },
        new TaskEntry { Category = "Holiday", Subcategory = "Personal", Ticket="TIME-156", Description = "Personal Holiday" },
    };

    public AppState(IndexedDbService indexedDb, JiraTicketService jiraTicketService, ILogger<AppState> logger)
    {
        _indexedDb = indexedDb;
        _jiraTicketService = jiraTicketService;
        _logger = logger;
    }

    public void WireUpTimerHub(TimerHubClient timerHubClient)
    {
        timerHubClient.OnTimerTick += TriggerCheckIn;
        timerHubClient.OnConnectionChanged += SetServerConnected;
    }

    public void WireUpFallbackTimer(FallbackTimerService fallbackTimer)
    {
        fallbackTimer.OnTimerTick += TriggerCheckIn;
    }

    public async Task InitializeAsync()
    {
        await _indexedDb.InitializeAsync();

        Settings = await _indexedDb.GetSettingsAsync();
        AllEntries = await _indexedDb.GetTimeEntriesAsync();
        try
        {
            AllTaskEntries = await _indexedDb.GetTaskEntriesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Task entries could not be loaded during initialization.");
            AllTaskEntries = new List<TaskEntry>();
        }

        await SeedDefaultTaskEntriesAsync();

        var today = DateTime.Today.ToString("yyyy-MM-dd");
        TodayEntries = AllEntries.Where(e => e.Date == today).ToList();

        var connectionState = await _indexedDb.GetConnectionStateAsync();
        if (connectionState != null)
        {
            CurrentTask = new TimeEntry
            {
                Type = Enum.Parse<TaskType>(connectionState.LastTaskType ?? "Ticket"),
                Ticket = connectionState.LastTicket,
                Description = connectionState.LastDescription,
                StartTime = SafeParseTime(connectionState.LastStartTime)
            };
        }

        if (!TodayEntries.Any())
        {
            ShowCheckInPopup = true;
        }

        CalculateNextCheckIn();
        NotifyStateChanged();
    }

    private async Task SeedDefaultTaskEntriesAsync()
    {
        foreach (var seed in SeedTaskEntries)
        {
            var exists = AllTaskEntries.Any(e =>
                string.Equals(e.Category, seed.Category, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(e.Subcategory ?? string.Empty, seed.Subcategory ?? string.Empty, StringComparison.OrdinalIgnoreCase));

            if (!exists)
            {
                var entry = new TaskEntry
                {
                    Category = seed.Category,
                    Subcategory = seed.Subcategory,
                    Ticket = seed.Ticket,
                    Description = seed.Description,
                };
                entry.Id = await _indexedDb.AddTaskEntryAsync(entry);
                AllTaskEntries.Add(entry);
            }
        }
    }

    private static string SafeParseTime(string? time)
    {
        if (string.IsNullOrEmpty(time)) return DateTime.Now.ToString("HH:mm");
        
        var parts = time.Split(':');
        if (parts.Length >= 2)
        {
            if (int.TryParse(parts[0], out var hours) && int.TryParse(parts[1], out var minutes))
            {
                hours = Math.Clamp(hours, 0, 23);
                minutes = Math.Clamp(minutes, 0, 59);
                return $"{hours:D2}:{minutes:D2}";
            }
        }
        return DateTime.Now.ToString("HH:mm");
    }

    public async Task ConfirmCheckIn(TaskType type, string? ticket, string? description)
    {
        var now = DateTime.Now;
        var startTime = RoundToNearest5Minutes(now).ToString("HH:mm");
        var today = now.ToString("yyyy-MM-dd");

        var newEntry = new TimeEntry
        {
            Date = today,
            StartTime = startTime,
            Type = type,
            Ticket = type == TaskType.Ticket ? ticket : type == TaskType.Break ? null : GetLinkedTicket(type),
            Description = description,
            DayTargetHhmm = Settings.DefaultTargetHours,
            PendingForJiraSubmission = true
        };

        newEntry.Id = await _indexedDb.AddTimeEntryAsync(newEntry);
        CurrentTask = newEntry;
        TodayEntries.Add(newEntry);
        AllEntries.Add(newEntry);

        if (!string.IsNullOrEmpty(newEntry.Ticket))
        {
            await UpdateTicketCacheAsync(newEntry.Ticket);
        }

        await SaveConnectionState();
        
        ShowCheckInPopup = false;
        IsTimerTriggeredPopup = false;
        SnoozedUntil = null;
        CalculateNextCheckIn();
        await RecalculateAllEntriesAsync();
        NotifyStateChanged();
    }

    public async Task ConfirmCheckIn(string startTime, TaskType type, string? ticket, string? description)
    {
        await ConfirmCheckIn(null, startTime, type, ticket, description);
    }

    public async Task ConfirmCheckIn(string? selectedDate, string startTime, TaskType type, string? ticket, string? description)
    {
        var now = DateTime.Now;
        DateTime entryDate;

        if (!string.IsNullOrWhiteSpace(selectedDate))
        {
            var parts = selectedDate.Split('-');
            if (parts.Length == 3
                && int.TryParse(parts[0], out var year)
                && int.TryParse(parts[1], out var month)
                && int.TryParse(parts[2], out var day))
            {
                try
                {
                    entryDate = new DateTime(year, month, day);
                }
                catch
                {
                    entryDate = now.Date;
                }
            }
            else
            {
                entryDate = now.Date;
            }
        }
        else
        {
            entryDate = now.Date;

            if (TimeSpan.TryParse(startTime, out var entryTime))
            {
                var entryDateTime = entryDate.Add(entryTime);
                if (entryDateTime.TimeOfDay < now.TimeOfDay - TimeSpan.FromMinutes(5))
                {
                    entryDate = entryDate.AddDays(1);
                }
            }
        }

        var newEntry = new TimeEntry
        {
            Date = entryDate.ToString("yyyy-MM-dd"),
            StartTime = startTime,
            Type = type,
            Ticket = type == TaskType.Ticket ? ticket : type == TaskType.Break ? null : GetLinkedTicket(type),
            Description = description,
            DayTargetHhmm = Settings.DefaultTargetHours,
            PendingForJiraSubmission = true
        };

        newEntry.Id = await _indexedDb.AddTimeEntryAsync(newEntry);
        CurrentTask = newEntry;
        if (newEntry.Date == DateTime.Today.ToString("yyyy-MM-dd"))
        {
            TodayEntries.Add(newEntry);
        }
        AllEntries.Add(newEntry);

        if (!string.IsNullOrEmpty(newEntry.Ticket))
        {
            await UpdateTicketCacheAsync(newEntry.Ticket);
        }

        await SaveConnectionState();
        
        ShowCheckInPopup = false;
        IsTimerTriggeredPopup = false;
        SnoozedUntil = null;
        CalculateNextCheckIn();
        await RecalculateAllEntriesAsync();
        NotifyStateChanged();
    }

    private string? GetLinkedTicket(TaskType type)
    {
        var configType = Settings.ConfigurableTypes.FirstOrDefault(c => c.Type == type);
        return configType?.LinkedTicket;
    }

    private async Task FinalizeCurrentEntry(string endTime)
    {
        if (CurrentTask == null || string.IsNullOrEmpty(CurrentTask.StartTime)) return;

        RecalculateTodayStats();
        
        await _indexedDb.UpdateTimeEntryAsync(CurrentTask);
    }

    public async Task SetSnooze(DateTime snoozeUntil)
    {
        SnoozedUntil = snoozeUntil;
        LastSelectedEstimatedEnd = snoozeUntil;
        ShowCheckInPopup = false;
        NextCheckInTime = snoozeUntil;
        NotifyStateChanged();
        await Task.CompletedTask;
    }

    public void TriggerCheckIn()
    {
        var now = DateTime.Now;

        if (SnoozedUntil.HasValue && now < SnoozedUntil.Value)
        {
            return;
        }

        if (!SnoozedUntil.HasValue && !IsRelevantCheckInTick(now))
        {
            CalculateNextCheckIn();
            NotifyStateChanged();
            return;
        }

        ShowCheckInPopup = true;
        IsTimerTriggeredPopup = true;
        SnoozedUntil = null;
        CalculateNextCheckIn();
        NotifyStateChanged();
    }

    public void RecordServerTick(string time)
    {
        LastServerTickTime = time;
        LastServerTickReceivedAt = DateTime.Now;
        NotifyStateChanged();
    }

    public void RecordNotificationAttempt(string status)
    {
        LastNotificationAttemptAt = DateTime.Now;
        LastNotificationStatus = status;
        NotifyStateChanged();
    }

    public void ShowAddEntryPopup()
    {
        ShowCheckInPopup = true;
        IsTimerTriggeredPopup = false;
        NotifyStateChanged();
    }

    public void HideCheckInPopup()
    {
        ShowCheckInPopup = false;
        IsTimerTriggeredPopup = false;
        NotifyStateChanged();
    }

    public event Action<bool>? OnServerConnectionChanged;

    public void SetServerConnected(bool connected)
    {
        IsConnectedToServer = connected;
        OnServerConnectionChanged?.Invoke(connected);
        NotifyStateChanged();
    }

    public async Task UpdateEntryAsync(TimeEntry entry)
    {
        await _indexedDb.UpdateTimeEntryAsync(entry);
        
        var index = AllEntries.FindIndex(e => e.Id == entry.Id);
        if (index >= 0)
        {
            AllEntries[index] = entry;
        }

        if (!string.IsNullOrWhiteSpace(entry.Ticket))
        {
            await UpdateTicketCacheAsync(entry.Ticket);
        }

        RebuildTodayEntries();

        await RecalculateAllEntriesAsync();
        NotifyStateChanged();
    }

    public async Task AddNewEntryAsync(TimeEntry entry)
    {
        entry.Id = await _indexedDb.AddTimeEntryAsync(entry);
        AllEntries.Add(entry);

        if (!string.IsNullOrWhiteSpace(entry.Ticket))
        {
            await UpdateTicketCacheAsync(entry.Ticket);
        }

        RebuildTodayEntries();
        await RecalculateAllEntriesAsync();
        NotifyStateChanged();
    }

    public async Task<bool> TryAddEntryWithoutDuplicates(TimeEntry entry, int toleranceMinutes = 5)
    {
        if (IsDuplicateEntry(entry, toleranceMinutes))
        {
            return false;
        }

        await AddNewEntryAsync(entry);
        return true;
    }

    public bool IsDuplicateEntry(TimeEntry entry, int toleranceMinutes = 5)
    {
        if (!TimeSpan.TryParse(entry.StartTime, out var newStart))
        {
            return false;
        }

        return AllEntries.Any(existing =>
            existing.Date == entry.Date &&
            existing.Type == entry.Type &&
            string.Equals(existing.Ticket ?? string.Empty, entry.Ticket ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
            TimeSpan.TryParse(existing.StartTime, out var existingStart) &&
            Math.Abs((newStart - existingStart).TotalMinutes) <= toleranceMinutes);
    }

    public async Task DeleteEntryAsync(int id)
    {
        if (CurrentTask?.Id == id)
        {
            CurrentTask = null;
        }
        
        await _indexedDb.DeleteTimeEntryAsync(id);
        AllEntries.RemoveAll(e => e.Id == id);
        RebuildTodayEntries();
        await RecalculateAllEntriesAsync();
        NotifyStateChanged();
    }

    public async Task SaveSettingsAsync(AppSettings settings)
    {
        Settings = settings;
        await _indexedDb.SaveSettingsAsync(settings);

        if (!SnoozedUntil.HasValue || SnoozedUntil.Value <= DateTime.Now)
        {
            CalculateNextCheckIn();
        }

        NotifyStateChanged();
    }

    public async Task UpdateTaskEntryAsync(TaskEntry entry)
    {
        await _indexedDb.UpdateTaskEntryAsync(entry);

        var index = AllTaskEntries.FindIndex(e => e.Id == entry.Id);
        if (index >= 0)
        {
            AllTaskEntries[index] = entry;
        }

        NotifyStateChanged();
    }

    public async Task AddNewTaskEntryAsync(TaskEntry entry)
    {
        entry.Id = await _indexedDb.AddTaskEntryAsync(entry);
        AllTaskEntries.Add(entry);
        NotifyStateChanged();
    }

    public async Task DeleteTaskEntryAsync(int id)
    {
        await _indexedDb.DeleteTaskEntryAsync(id);
        AllTaskEntries.RemoveAll(e => e.Id == id);
        NotifyStateChanged();
    }

    public async Task RefreshEntriesAsync()
    {
        AllEntries = await _indexedDb.GetTimeEntriesAsync();
        RebuildTodayEntries();
        RecalculateTodayStats();
        NotifyStateChanged();
    }

    private void RebuildTodayEntries()
    {
        var today = DateTime.Today.ToString("yyyy-MM-dd");
        TodayEntries = AllEntries.Where(e => e.Date == today).ToList();
    }

    private void CalculateNextCheckIn()
    {
        var now = DateTime.Now;
        var interval = GetNotificationIntervalMinutes();
        var minutesToAdd = interval - (now.Minute % interval);

        if (minutesToAdd == 0)
        {
            minutesToAdd = interval;
        }

        var nextCheckIn = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0)
            .AddMinutes(minutesToAdd);

        NextCheckInTime = nextCheckIn;
    }

    private bool IsRelevantCheckInTick(DateTime now)
    {
        var interval = GetNotificationIntervalMinutes();
        return now.Minute % interval == 0;
    }

    private int GetNotificationIntervalMinutes()
    {
        var interval = Settings.NotificationIntervalMinutes;

        if (interval <= 0 || interval > 60)
        {
            return 15;
        }

        return interval;
    }

    private void RecalculateTodayStats()
    {
        TodayAccumulated = TimeSpan.Zero;

        var orderedTodayEntries = TodayEntries
            .OrderBy(e => DurationCalculator.GetStartMinutesOrMax(e.StartTime))
            .ThenBy(e => e.StartTime)
            .ThenBy(e => e.Id)
            .ToList();

        var accumulatedMinutes = 0;

        for (int i = 0; i < orderedTodayEntries.Count; i++)
        {
            var entry = orderedTodayEntries[i];
            if (entry.Type == TaskType.Break)
            {
                continue;
            }

            accumulatedMinutes += DurationCalculator.CalculateDurationMinutes(orderedTodayEntries, i);
        }

        if (TimeSpan.TryParse($"{accumulatedMinutes / 60:D2}:{accumulatedMinutes % 60:D2}", out var accumulatedSpan))
        {
            TodayAccumulated = accumulatedSpan;
        }
        else
        {
            TodayAccumulated = TimeSpan.Zero;
        }

        if (TimeSpan.TryParse(Settings.DefaultTargetHours, out var target))
        {
            TodayTarget = target;
        }
    }

    private async Task RecalculateAllEntriesAsync()
    {
        var groupedByDate = AllEntries.GroupBy(e => e.Date).OrderBy(g => g.Key);
        var updatedEntries = new List<TimeEntry>();
        
        foreach (var group in groupedByDate)
        {
            var entries = group
                .OrderBy(e => DurationCalculator.GetStartMinutesOrMax(e.StartTime))
                .ThenBy(e => e.StartTime)
                .ThenBy(e => e.Id)
                .ToList();
            var accumulatedMinutes = 0;
            
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];

                if (entry.Type != TaskType.Break)
                {
                    accumulatedMinutes += DurationCalculator.CalculateDurationMinutes(entries, i);
                }

                var newAccumulatedHhmm = FormatDurationFromMinutes(accumulatedMinutes);
                var newAccumulatedDays = accumulatedMinutes / 480.0;

                var changed = !string.Equals(entry.DayAccumulatedHhmm, newAccumulatedHhmm, StringComparison.Ordinal)
                    || entry.DayAccumulatedDays != newAccumulatedDays;

                entry.DayAccumulatedHhmm = newAccumulatedHhmm;
                entry.DayAccumulatedDays = newAccumulatedDays;

                if (TimeSpan.TryParse(entry.DayTargetHhmm, out var target))
                {
                    var deviationMinutes = accumulatedMinutes - (int)target.TotalMinutes;
                    var newDeviationHhmm = FormatDurationFromMinutes(deviationMinutes);
                    var newDeviationDays = deviationMinutes / 480.0;

                    if (!string.Equals(entry.DayDeviationHhmm, newDeviationHhmm, StringComparison.Ordinal)
                        || entry.DayDeviationDays != newDeviationDays)
                    {
                        changed = true;
                    }

                    entry.DayDeviationHhmm = newDeviationHhmm;
                    entry.DayDeviationDays = newDeviationDays;
                }

                if (changed)
                {
                    updatedEntries.Add(entry);
                }
            }
        }

        foreach (var entry in updatedEntries)
        {
            await _indexedDb.UpdateTimeEntryAsync(entry);
        }

        RecalculateTodayStats();
    }

    private static DateTime RoundToNearest5Minutes(DateTime dt)
    {
        int roundedMinutes = ((int)Math.Round(dt.Minute / 5.0) * 5) % 60;
        int hour = dt.Hour;

        if (roundedMinutes == 0 && dt.Minute >= 58)
        {
            hour = (hour + 1) % 24;
        }

        return new DateTime(dt.Year, dt.Month, dt.Day, hour, roundedMinutes, 0);
    }

    private static string FormatDuration(TimeSpan duration)
    {
        return $"{(int)Math.Floor(duration.TotalHours):D2}:{duration.Minutes:D2}";
    }

    private static string FormatDurationFromMinutes(int totalMinutes)
    {
        var sign = totalMinutes < 0 ? "-" : string.Empty;
        var absoluteMinutes = Math.Abs(totalMinutes);
        return $"{sign}{absoluteMinutes / 60:D2}:{absoluteMinutes % 60:D2}";
    }

    private async Task UpdateTicketCacheAsync(string ticketKey)
    {
        var normalizedTicketKey = NormalizeTicketKey(ticketKey);
        if (string.IsNullOrWhiteSpace(normalizedTicketKey))
        {
            return;
        }

        var allTickets = await _indexedDb.GetAllCachedTicketsAsync();
        var existing = allTickets.FirstOrDefault(t =>
            string.Equals(t.TicketKey, normalizedTicketKey, StringComparison.OrdinalIgnoreCase));
        var summary = await TryGetTicketSummaryAsync(normalizedTicketKey);
        
        if (existing != null)
        {
            var originalTicketKey = existing.TicketKey;
            existing.TicketKey = normalizedTicketKey;
            existing.UseCount++;
            existing.LastUsedAt = DateTime.Now;

            if (!string.IsNullOrWhiteSpace(summary))
            {
                existing.Summary = summary;
            }

            if (!string.Equals(originalTicketKey, normalizedTicketKey, StringComparison.Ordinal))
            {
                await _indexedDb.DeleteCachedTicketAsync(originalTicketKey);
            }

            await _indexedDb.UpdateTicketCacheAsync(existing);
        }
        else
        {
            var newTicket = new TicketCache
            {
                TicketKey = normalizedTicketKey,
                Summary = summary ?? string.Empty,
                UseCount = 1,
                LastUsedAt = DateTime.Now
            };
            await _indexedDb.AddTicketToCacheAsync(newTicket);
        }
    }

    private async Task<string?> TryGetTicketSummaryAsync(string ticketKey)
    {
        try
        {
            return await _jiraTicketService.GetTicketSummaryAsync(Settings, ticketKey);
        }
        catch (Exception ex)
        {
            _logger.LogInformation(ex, "JIRA summary lookup failed for ticket {TicketKey}", ticketKey);
            return null;
        }
    }

    private static string NormalizeTicketKey(string? ticketKey)
    {
        return (ticketKey ?? string.Empty).Trim().ToUpper();
    }

    private async Task SaveConnectionState()
    {
        if (CurrentTask == null) return;
        
        var state = new ConnectionState
        {
            LastTaskType = CurrentTask.Type.ToString(),
            LastTicket = CurrentTask.Ticket,
            LastDescription = CurrentTask.Description,
            LastStartTime = CurrentTask.StartTime
        };
        
        await _indexedDb.SaveConnectionStateAsync(state);
    }

    private void NotifyStateChanged()
    {
        OnChange?.Invoke();
    }
}
