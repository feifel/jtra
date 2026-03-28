using Microsoft.AspNetCore.SignalR.Client;

namespace JtraClient.Services;

public class TimerHubClient : IAsyncDisposable
{
    private readonly ILogger<TimerHubClient> _logger;
    private HubConnection? _hubConnection;

    public bool IsConnected => _hubConnection?.State == HubConnectionState.Connected;

    public event Action? OnTimerTick;
    public event Action<bool>? OnConnectionChanged;

    public TimerHubClient(ILogger<TimerHubClient> logger)
    {
        _logger = logger;
    }

    public async Task StartAsync(string serverUrl)
    {
        _hubConnection = new HubConnectionBuilder()
            .WithUrl($"{serverUrl}/timerHub")
            .WithAutomaticReconnect(new[] { TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10) })
            .Build();

        _hubConnection.On<string>("TimerTick", (time) =>
        {
            _logger.LogInformation("Timer tick received: {Time}", time);
            OnTimerTick?.Invoke();
        });

        _hubConnection.Reconnecting += error =>
        {
            _logger.LogWarning("Reconnecting to SignalR hub...");
            OnConnectionChanged?.Invoke(false);
            return Task.CompletedTask;
        };

        _hubConnection.Reconnected += connectionId =>
        {
            _logger.LogInformation("Reconnected to SignalR hub");
            OnConnectionChanged?.Invoke(true);
            return Task.CompletedTask;
        };

        _hubConnection.Closed += error =>
        {
            _logger.LogWarning("Connection to SignalR hub closed");
            OnConnectionChanged?.Invoke(false);
            return Task.CompletedTask;
        };

        try
        {
            await _hubConnection.StartAsync();
            OnConnectionChanged?.Invoke(true);
            _logger.LogInformation("Connected to SignalR hub");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to SignalR hub");
            OnConnectionChanged?.Invoke(false);
        }
    }

    public async Task StopAsync()
    {
        if (_hubConnection != null)
        {
            await _hubConnection.StopAsync();
            OnConnectionChanged?.Invoke(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_hubConnection != null)
        {
            await _hubConnection.DisposeAsync();
        }
    }
}
