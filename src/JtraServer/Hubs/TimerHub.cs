using Microsoft.AspNetCore.SignalR;

namespace JtraServer.Hubs;

public class TimerHub : Hub
{
    private readonly ILogger<TimerHub> _logger;

    public TimerHub(ILogger<TimerHub> logger)
    {
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("Client connected: {ConnectionId}", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("Client disconnected: {ConnectionId}", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    public async Task RequestCurrentTime()
    {
        await Clients.Caller.SendAsync("TimerTick", DateTime.Now.ToString("HH:mm"));
    }
}
