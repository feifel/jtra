using Microsoft.AspNetCore.SignalR;
using System.Threading.Channels;

namespace JtraServer.Services;

public class TimerService : BackgroundService
{
    private readonly IHubContext<Hubs.TimerHub> _hubContext;
    private readonly ILogger<TimerService> _logger;

    public TimerService(IHubContext<Hubs.TimerHub> hubContext, ILogger<TimerService> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTime.Now;
            var nextTick = GetNextMinuteBoundary(now);
            var delay = nextTick - now;

            _logger.LogInformation("Timer waiting {Delay} until {NextTick}", delay, nextTick.ToString("HH:mm"));

            await Task.Delay(delay, stoppingToken);

            try
            {
                await _hubContext.Clients.All.SendAsync("TimerTick", nextTick.ToString("HH:mm"), stoppingToken);
                _logger.LogInformation("Timer tick sent at {Time}", nextTick.ToString("HH:mm"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending timer tick");
            }
        }
    }

    private static DateTime GetNextMinuteBoundary(DateTime now)
    {
        return new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0).AddMinutes(1);
    }
}
