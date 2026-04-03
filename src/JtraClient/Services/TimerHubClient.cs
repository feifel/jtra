namespace JtraClient.Services;

public class TimerHubClient : IAsyncDisposable
{
    private readonly ILogger<TimerHubClient> _logger;

    public bool IsConnected { get; private set; }

    public event Action? OnTimerTick;
    public event Action<bool>? OnConnectionChanged;

    public TimerHubClient(ILogger<TimerHubClient> logger)
    {
        _logger = logger;
    }

    public async Task StartAsync(string serverUrl)
    {
        _logger.LogWarning("SignalR timer client is disabled in this build. Using fallback timer only.");
        IsConnected = false;
        OnConnectionChanged?.Invoke(false);
        await Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        IsConnected = false;
        OnConnectionChanged?.Invoke(false);
        await Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        IsConnected = false;
        await Task.CompletedTask;
    }
}
