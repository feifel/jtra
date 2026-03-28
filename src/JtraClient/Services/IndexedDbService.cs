using System.Text.Json;
using JtraShared.Models;
using Microsoft.JSInterop;

namespace JtraClient.Services;

public class IndexedDbService
{
    private readonly IJSRuntime _jsRuntime;
    private readonly ILogger<IndexedDbService> _logger;
    private bool _initialized = false;

    public IndexedDbService(IJSRuntime jsRuntime, ILogger<IndexedDbService> logger)
    {
        _jsRuntime = jsRuntime;
        _logger = logger;
    }

    public async Task InitializeAsync()
    {
        if (_initialized) return;

        await _jsRuntime.InvokeVoidAsync("indexedDbInterop.init");
        _initialized = true;
        _logger.LogInformation("IndexedDB initialized");
    }

    private async Task<T?> InvokeAsync<T>(string identifier, params object?[] args)
    {
        var element = await _jsRuntime.InvokeAsync<JsonElement>(identifier, args);
        return JsonSerializer.Deserialize(element.GetRawText(), typeof(T), JtraJsonContext.Default) is T result ? result : default;
    }

    public async Task<List<TimeEntry>> GetTimeEntriesAsync()
    {
        var entries = await InvokeAsync<List<TimeEntry>>("indexedDbInterop.getAllEntries");
        return entries ?? new List<TimeEntry>();
    }

    public async Task<TimeEntry?> GetTimeEntryAsync(int id)
    {
        return await InvokeAsync<TimeEntry>("indexedDbInterop.getEntry", id);
    }

    public async Task<int> AddTimeEntryAsync(TimeEntry entry)
    {
        return await _jsRuntime.InvokeAsync<int>("indexedDbInterop.addEntry", entry);
    }

    public async Task UpdateTimeEntryAsync(TimeEntry entry)
    {
        await _jsRuntime.InvokeVoidAsync("indexedDbInterop.updateEntry", entry);
    }

    public async Task DeleteTimeEntryAsync(int id)
    {
        await _jsRuntime.InvokeVoidAsync("indexedDbInterop.deleteEntry", id);
    }

    public async Task<List<TimeEntry>> GetEntriesByDateAsync(string date)
    {
        var entries = await InvokeAsync<List<TimeEntry>>("indexedDbInterop.getEntriesByDate", date);
        return entries ?? new List<TimeEntry>();
    }

    public async Task<AppSettings> GetSettingsAsync()
    {
        var settings = await InvokeAsync<AppSettings>("indexedDbInterop.getSettings");
        return settings ?? new AppSettings();
    }

    public async Task SaveSettingsAsync(AppSettings settings)
    {
        await _jsRuntime.InvokeVoidAsync("indexedDbInterop.saveSettings", settings);
    }

    public async Task<TicketCache?> GetTicketFromCacheAsync(string ticketKey)
    {
        return await InvokeAsync<TicketCache>("indexedDbInterop.getTicketFromCache", ticketKey);
    }

    public async Task AddTicketToCacheAsync(TicketCache ticket)
    {
        await _jsRuntime.InvokeVoidAsync("indexedDbInterop.addTicketToCache", ticket);
    }

    public async Task ClearTicketCacheAsync()
    {
        await _jsRuntime.InvokeVoidAsync("indexedDbInterop.clearTicketCache");
    }

    public async Task<ConnectionState?> GetConnectionStateAsync()
    {
        return await InvokeAsync<ConnectionState>("indexedDbInterop.getConnectionState");
    }

    public async Task SaveConnectionStateAsync(ConnectionState state)
    {
        await _jsRuntime.InvokeVoidAsync("indexedDbInterop.saveConnectionState", state);
    }

    public async Task ClearAllDataAsync()
    {
        await _jsRuntime.InvokeVoidAsync("indexedDbInterop.clearAllData");
    }

    public async Task ImportFromCsvAsync(List<TimeEntry> entries)
    {
        await _jsRuntime.InvokeVoidAsync("indexedDbInterop.clearAllEntries");
        foreach (var entry in entries)
        {
            await AddTimeEntryAsync(entry);
        }
    }
}
