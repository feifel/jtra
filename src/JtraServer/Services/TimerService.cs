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
            var nextQuarter = GetNextQuarterHour(now);
            var delay = nextQuarter - now;

            _logger.LogInformation("Timer waiting {Delay} until {NextQuarter}", delay, nextQuarter.ToString("HH:mm"));

            await Task.Delay(delay, stoppingToken);

            try
            {
                await _hubContext.Clients.All.SendAsync("TimerTick", nextQuarter.ToString("HH:mm"), stoppingToken);
                _logger.LogInformation("Timer tick sent at {Time}", nextQuarter.ToString("HH:mm"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending timer tick");
            }
        }
    }

    private static DateTime GetNextQuarterHour(DateTime now)
    {
        var minutes = now.Minute;
        int nextQuarterMinute;

        if (minutes < 15) nextQuarterMinute = 15;
        else if (minutes < 30) nextQuarterMinute = 30;
        else if (minutes < 45) nextQuarterMinute = 45;
        else nextQuarterMinute = 0;

        var next = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0);
        if (nextQuarterMinute == 0 && minutes >= 45)
        {
            next = next.AddHours(1);
        }
        else
        {
            next = next.AddMinutes(nextQuarterMinute);
        }

        return next;
    }
}
