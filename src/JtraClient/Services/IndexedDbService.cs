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

    // Deserialize a JsonElement returned from JS using source-gen context (avoids NullabilityInfoContext)
    private async Task<T?> InvokeAsync<T>(string identifier, params object?[] args)
    {
        var element = await _jsRuntime.InvokeAsync<JsonElement>(identifier, args);
        if (element.ValueKind == JsonValueKind.Null || element.ValueKind == JsonValueKind.Undefined)
            return default;
        return (T?)JsonSerializer.Deserialize(element.GetRawText(), typeof(T), JtraJsonContext.Default);
    }

    // Serialize a model to JsonElement using source-gen context before passing to JS
    private static JsonElement ToJsonElement<T>(T value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo)
    {
        var json = JsonSerializer.Serialize(value, typeInfo);
        return JsonSerializer.Deserialize<JsonElement>(json);
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
        // Serialize without the 'id' field so IndexedDB uses autoIncrement instead of treating 0 as an explicit key
        var json = JsonSerializer.Serialize(entry, JtraJsonContext.Default.TimeEntry);
        using var doc = JsonDocument.Parse(json);
        var dict = doc.RootElement.EnumerateObject()
            .Where(p => !p.Name.Equals("id", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(p => p.Name, p => p.Value.Clone());
        var stripped = JsonSerializer.SerializeToElement(dict);
        return await _jsRuntime.InvokeAsync<int>("indexedDbInterop.addEntry", stripped);
    }

    public async Task UpdateTimeEntryAsync(TimeEntry entry)
    {
        var element = ToJsonElement(entry, JtraJsonContext.Default.TimeEntry);
        await _jsRuntime.InvokeVoidAsync("indexedDbInterop.updateEntry", element);
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
        var element = ToJsonElement(settings, JtraJsonContext.Default.AppSettings);
        await _jsRuntime.InvokeVoidAsync("indexedDbInterop.saveSettings", element);
    }

    public async Task<TicketCache?> GetTicketFromCacheAsync(string ticketKey)
    {
        return await InvokeAsync<TicketCache>("indexedDbInterop.getTicketFromCache", ticketKey);
    }

    public async Task AddTicketToCacheAsync(TicketCache ticket)
    {
        var element = ToJsonElement(ticket, JtraJsonContext.Default.TicketCache);
        await _jsRuntime.InvokeVoidAsync("indexedDbInterop.addTicketToCache", element);
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
        var element = ToJsonElement(state, JtraJsonContext.Default.ConnectionState);
        await _jsRuntime.InvokeVoidAsync("indexedDbInterop.saveConnectionState", element);
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
