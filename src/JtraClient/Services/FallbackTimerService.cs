namespace JtraClient.Services;

public class FallbackTimerService : IDisposable
{
    private readonly ILogger<FallbackTimerService> _logger;
    private Timer? _timer;

    public event Action? OnTimerTick;

    public FallbackTimerService(ILogger<FallbackTimerService> logger)
    {
        _logger = logger;
    }

    public void Start()
    {
        if (_timer != null) return;

        _timer = new Timer(CheckTime, null, TimeSpan.Zero, TimeSpan.FromMinutes(1));
        _logger.LogInformation("Fallback timer started");
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
        _logger.LogInformation("Fallback timer stopped");
    }

    private void CheckTime(object? state)
    {
        var now = DateTime.Now;
        
        if (now.Minute == 0 || now.Minute == 15 || now.Minute == 30 || now.Minute == 45)
        {
            _logger.LogInformation("Fallback timer tick at {Time}", now.ToString("HH:mm"));
            OnTimerTick?.Invoke();
        }
    }

    public void Dispose()
    {
        _timer?.Dispose();
    }
}
