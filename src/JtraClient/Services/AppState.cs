using JtraShared.Models;
using Microsoft.JSInterop;

namespace JtraClient.Services;

public class AppState
{
    private readonly IndexedDbService _indexedDb;
    private readonly ILogger<AppState> _logger;

    public event Action? OnChange;

    public List<TimeEntry> TodayEntries { get; private set; } = new();
    public List<TimeEntry> AllEntries { get; private set; } = new();
    public List<TaskEntry> AllTaskEntries { get; private set; } = new();
    public AppSettings Settings { get; private set; } = new();
    
    public TimeEntry? CurrentTask { get; private set; }
    public DateTime? NextCheckInTime { get; private set; }
    public DateTime? SnoozedUntil { get; private set; }
    public bool ShowCheckInPopup { get; private set; }
    public bool IsConnectedToServer { get; private set; }
    public bool IsTimerTriggeredPopup { get; private set; }
    
    public TimeSpan TodayAccumulated { get; private set; }
    public TimeSpan TodayTarget { get; private set; } = TimeSpan.FromHours(8);
    public TimeSpan TodayDeviation => TodayAccumulated - TodayTarget;

    public AppState(IndexedDbService indexedDb, ILogger<AppState> logger)
    {
        _indexedDb = indexedDb;
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
        var startTime = RoundToNearest15Minutes(now).ToString("HH:mm");
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
        var now = DateTime.Now;
        var entryDate = now.Date;

        if (TimeSpan.TryParse(startTime, out var entryTime))
        {
            var entryDateTime = entryDate.Add(entryTime);
            if (entryDateTime.TimeOfDay < now.TimeOfDay - TimeSpan.FromMinutes(15))
            {
                entryDate = entryDate.AddDays(1);
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

        var startTime = DateTime.ParseExact(CurrentTask.StartTime, "HH:mm", null);
        var end = DateTime.ParseExact(endTime, "HH:mm", null);
        
        if (end < startTime)
        {
            end = end.AddDays(1);
        }

        var duration = end - startTime;
        CurrentTask.Duration = FormatDuration(duration);

        RecalculateTodayStats();
        
        await _indexedDb.UpdateTimeEntryAsync(CurrentTask);
    }

    public async Task SetSnooze(DateTime snoozeUntil)
    {
        SnoozedUntil = snoozeUntil;
        ShowCheckInPopup = false;
        NextCheckInTime = snoozeUntil;
        NotifyStateChanged();
        await Task.CompletedTask;
    }

    public void TriggerCheckIn()
    {
        if (SnoozedUntil.HasValue && DateTime.Now < SnoozedUntil.Value)
        {
            return;
        }

        ShowCheckInPopup = true;
        IsTimerTriggeredPopup = true;
        SnoozedUntil = null;
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

        var todayIndex = TodayEntries.FindIndex(e => e.Id == entry.Id);
        if (todayIndex >= 0)
        {
            TodayEntries[todayIndex] = entry;
        }

        await RecalculateAllEntriesAsync();
        NotifyStateChanged();
    }

    public async Task AddNewEntryAsync(TimeEntry entry)
    {
        entry.Id = await _indexedDb.AddTimeEntryAsync(entry);
        AllEntries.Add(entry);
        if (entry.Date == DateTime.Today.ToString("yyyy-MM-dd"))
        {
            TodayEntries.Add(entry);
        }
        await RecalculateAllEntriesAsync();
        NotifyStateChanged();
    }

    public async Task DeleteEntryAsync(int id)
    {
        if (CurrentTask?.Id == id)
        {
            CurrentTask = null;
        }
        
        await _indexedDb.DeleteTimeEntryAsync(id);
        AllEntries.RemoveAll(e => e.Id == id);
        TodayEntries.RemoveAll(e => e.Id == id);
        await RecalculateAllEntriesAsync();
        NotifyStateChanged();
    }

    public async Task SaveSettingsAsync(AppSettings settings)
    {
        Settings = settings;
        await _indexedDb.SaveSettingsAsync(settings);
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
        var today = DateTime.Today.ToString("yyyy-MM-dd");
        TodayEntries = AllEntries.Where(e => e.Date == today).ToList();
        RecalculateTodayStats();
        NotifyStateChanged();
    }

    private void CalculateNextCheckIn()
    {
        var now = DateTime.Now;
        var minutes = now.Minute;
        int nextQuarter = ((minutes / 15) + 1) * 15;
        int nextHour = now.Hour;
        
        if (nextQuarter >= 60)
        {
            nextQuarter = 0;
            nextHour++;
        }

        if (nextHour >= 24)
        {
            nextHour = 0;
        }

        NextCheckInTime = new DateTime(now.Year, now.Month, now.Day, nextHour, nextQuarter, 0);
    }

    private void RecalculateTodayStats()
    {
        TodayAccumulated = TimeSpan.Zero;
        
        foreach (var entry in TodayEntries.Where(e => e.Type != TaskType.Break && !string.IsNullOrEmpty(e.Duration)))
        {
            if (TimeSpan.TryParse(entry.Duration, out var duration))
            {
                TodayAccumulated += duration;
            }
        }

        if (TimeSpan.TryParse(Settings.DefaultTargetHours, out var target))
        {
            TodayTarget = target;
        }
    }

    private async Task RecalculateAllEntriesAsync()
    {
        var groupedByDate = AllEntries.GroupBy(e => e.Date).OrderBy(g => g.Key);
        var today = DateTime.Today.ToString("yyyy-MM-dd");
        var now = DateTime.Now;
        var updatedEntries = new List<TimeEntry>();
        
        foreach (var group in groupedByDate)
        {
            var entries = group.OrderBy(e => e.StartTime).ToList();
            TimeSpan accumulated = TimeSpan.Zero;
            
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                
                var startTime = ParseTime(entry.StartTime);
                DateTime endTime;
                
                if (entry == CurrentTask)
                {
                    endTime = startTime;
                }
                else if (i < entries.Count - 1)
                {
                    endTime = ParseTime(entries[i + 1].StartTime);
                    if (endTime <= startTime)
                    {
                        endTime = endTime.AddDays(1);
                    }
                }
                else
                {
                    endTime = startTime;
                }
                
                var duration = endTime - startTime;
                var newDuration = duration > TimeSpan.Zero ? FormatDuration(duration) : "";
                if (entry.Duration != newDuration)
                {
                    entry.Duration = newDuration;
                    updatedEntries.Add(entry);
                }
                
                if (entry.Type != TaskType.Break && !string.IsNullOrEmpty(entry.Duration))
                {
                    if (TimeSpan.TryParse(entry.Duration, out var dur))
                    {
                        accumulated += dur;
                    }
                }
                
                entry.DayAccumulatedHhmm = FormatDuration(accumulated);
                entry.DayAccumulatedDays = accumulated.TotalHours / 8.0;
                
                if (TimeSpan.TryParse(entry.DayTargetHhmm, out var target))
                {
                    var deviation = accumulated - target;
                    entry.DayDeviationHhmm = FormatDuration(deviation);
                    entry.DayDeviationDays = deviation.TotalHours / 8.0;
                }
            }
        }

        foreach (var entry in updatedEntries)
        {
            await _indexedDb.UpdateTimeEntryAsync(entry);
        }

        RecalculateTodayStats();
    }

    private static DateTime RoundToNearest15Minutes(DateTime dt)
    {
        int minutes = dt.Minute;
        int roundedMinutes;
        
        if (minutes <= 7 || minutes > 52) roundedMinutes = 0;
        else if (minutes <= 22) roundedMinutes = 15;
        else if (minutes <= 37) roundedMinutes = 30;
        else roundedMinutes = 45;

        int hour = dt.Hour;
        
        if (minutes > 52)
        {
            hour = (hour + 1) % 24;
        }

        return new DateTime(dt.Year, dt.Month, dt.Day, hour, roundedMinutes, 0);
    }

    private static string FormatDuration(TimeSpan duration)
    {
        return $"{(int)Math.Floor(duration.TotalHours):D2}:{duration.Minutes:D2}";
    }

    private static DateTime ParseTime(string time)
    {
        if (string.IsNullOrEmpty(time)) return DateTime.Today;
        var parts = time.Split(':');
        if (parts.Length >= 2)
        {
            if (int.TryParse(parts[0], out var hours) && int.TryParse(parts[1], out var minutes))
            {
                hours = Math.Clamp(hours, 0, 23);
                minutes = Math.Clamp(minutes, 0, 59);
                return DateTime.Today.AddHours(hours).AddMinutes(minutes);
            }
        }
        return DateTime.Today;
    }

    private async Task UpdateTicketCacheAsync(string ticketKey)
    {
        var existing = await _indexedDb.GetTicketFromCacheAsync(ticketKey);
        
        if (existing != null)
        {
            existing.UseCount++;
            existing.LastUsedAt = DateTime.Now;
            await _indexedDb.UpdateTicketCacheAsync(existing);
        }
        else
        {
            var allTickets = await _indexedDb.GetAllCachedTicketsAsync();
            
            if (allTickets.Count >= Settings.TicketCacheSize)
            {
                var oldest = allTickets.OrderBy(t => t.LastUsedAt).First();
                await _indexedDb.DeleteCachedTicketAsync(oldest.TicketKey);
            }
            
            var newTicket = new TicketCache
            {
                TicketKey = ticketKey,
                Summary = "",
                UseCount = 1,
                LastUsedAt = DateTime.Now
            };
            await _indexedDb.AddTicketToCacheAsync(newTicket);
        }
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
