using Microsoft.JSInterop;

namespace JtraClient.Services;

public class TimerHubClient : IAsyncDisposable
{
    private readonly AppState _appState;
    private readonly ILogger<TimerHubClient> _logger;
    private readonly IJSRuntime _jsRuntime;
    private DotNetObjectReference<TimerHubClient>? _dotNetReference;
    private bool _isStarted;

    public bool IsConnected { get; private set; }

    public event Action? OnTimerTick;
    public event Action<bool>? OnConnectionChanged;

    public TimerHubClient(AppState appState, IJSRuntime jsRuntime, ILogger<TimerHubClient> logger)
    {
        _appState = appState;
        _jsRuntime = jsRuntime;
        _logger = logger;
    }

    public async Task StartAsync(string serverUrl)
    {
        if (_isStarted)
        {
            return;
        }

        _dotNetReference ??= DotNetObjectReference.Create(this);

        try
        {
            await _jsRuntime.InvokeVoidAsync("timerHubInterop.start", serverUrl, _dotNetReference);
            _isStarted = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start timer hub interop");
            SetConnectionState(false);
        }
    }

    public async Task StopAsync()
    {
        if (!_isStarted)
        {
            SetConnectionState(false);
            return;
        }

        await _jsRuntime.InvokeVoidAsync("timerHubInterop.stop");
        _isStarted = false;
        SetConnectionState(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_isStarted)
        {
            await StopAsync();
        }

        _dotNetReference?.Dispose();
        _dotNetReference = null;
    }

    [JSInvokable]
    public Task HandleTimerTick(string time)
    {
        _logger.LogInformation("Timer tick received: {Time}", time);
        _appState.RecordServerTick(time);
        OnTimerTick?.Invoke();
        return Task.CompletedTask;
    }

    [JSInvokable]
    public Task HandleConnectionChanged(bool connected)
    {
        if (connected)
        {
            _logger.LogInformation("Connected to SignalR hub");
        }
        else
        {
            _logger.LogWarning("Connection to SignalR hub unavailable");
        }

        SetConnectionState(connected);
        return Task.CompletedTask;
    }

    private void SetConnectionState(bool connected)
    {
        IsConnected = connected;
        OnConnectionChanged?.Invoke(connected);
    }
}
