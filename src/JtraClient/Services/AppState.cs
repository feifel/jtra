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
    public AppSettings Settings { get; private set; } = new();
    
    public TimeEntry? CurrentTask { get; private set; }
    public DateTime? NextCheckInTime { get; private set; }
    public DateTime? SnoozedUntil { get; private set; }
    public bool ShowCheckInPopup { get; private set; }
    public bool IsConnectedToServer { get; private set; }
    
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
                StartTime = connectionState.LastStartTime ?? DateTime.Now.ToString("HH:mm")
            };
        }

        if (!TodayEntries.Any())
        {
            ShowCheckInPopup = true;
        }

        CalculateNextCheckIn();
        NotifyStateChanged();
    }

    public async Task ConfirmCheckIn(TaskType type, string? ticket, string? description)
    {
        var now = DateTime.Now;
        var startTime = RoundToNearest15Minutes(now).ToString("HH:mm");
        var today = now.ToString("yyyy-MM-dd");

        if (CurrentTask != null && !string.IsNullOrEmpty(CurrentTask.StartTime))
        {
            await FinalizeCurrentEntry(startTime);
        }

        var newEntry = new TimeEntry
        {
            Date = today,
            StartTime = startTime,
            Type = type,
            Ticket = type == TaskType.Ticket ? ticket : type == TaskType.Break ? null : GetLinkedTicket(type),
            Description = description,
            DayTargetHhmm = Settings.DefaultTargetHours
        };

        newEntry.Id = await _indexedDb.AddTimeEntryAsync(newEntry);
        CurrentTask = newEntry;
        TodayEntries.Add(newEntry);
        AllEntries.Add(newEntry);

        await SaveConnectionState();
        
        ShowCheckInPopup = false;
        SnoozedUntil = null;
        CalculateNextCheckIn();
        RecalculateTodayStats();
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
        SnoozedUntil = null;
        NotifyStateChanged();
    }

    public void ShowAddEntryPopup()
    {
        ShowCheckInPopup = true;
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

        RecalculateAllEntries();
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
        RecalculateAllEntries();
        NotifyStateChanged();
    }

    public async Task DeleteEntryAsync(int id)
    {
        await _indexedDb.DeleteTimeEntryAsync(id);
        AllEntries.RemoveAll(e => e.Id == id);
        TodayEntries.RemoveAll(e => e.Id == id);
        RecalculateAllEntries();
        NotifyStateChanged();
    }

    public async Task SaveSettingsAsync(AppSettings settings)
    {
        Settings = settings;
        await _indexedDb.SaveSettingsAsync(settings);
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

    private void RecalculateAllEntries()
    {
        var groupedByDate = AllEntries.GroupBy(e => e.Date).OrderBy(g => g.Key);
        
        foreach (var group in groupedByDate)
        {
            var entries = group.OrderBy(e => e.StartTime).ToList();
            TimeSpan accumulated = TimeSpan.Zero;
            
            foreach (var entry in entries)
            {
                if (entry.Type != TaskType.Break && !string.IsNullOrEmpty(entry.Duration))
                {
                    if (TimeSpan.TryParse(entry.Duration, out var duration))
                    {
                        accumulated += duration;
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

        var result = new DateTime(dt.Year, dt.Month, dt.Day, dt.Hour, 0, 0);
        
        if (minutes > 52)
        {
            result = result.AddHours(1);
        }
        else
        {
            result = result.AddMinutes(roundedMinutes);
        }

        return result;
    }

    private static string FormatDuration(TimeSpan duration)
    {
        return $"{(int)Math.Floor(duration.TotalHours):D2}:{duration.Minutes:D2}";
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
