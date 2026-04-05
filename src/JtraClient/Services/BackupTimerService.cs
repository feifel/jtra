using System.Threading;

namespace JtraClient.Services;

public class BackupTimerService : IDisposable
{
    private readonly BackupService _backupService;
    private readonly AppState _appState;
    private readonly ILogger<BackupTimerService> _logger;
    private Timer? _timer;
    private bool _disposed;

    public event Action? OnBackupCompleted;

    public BackupTimerService(BackupService backupService, AppState appState, ILogger<BackupTimerService> logger)
    {
        _backupService = backupService;
        _appState = appState;
        _logger = logger;
    }

    public void Start()
    {
        if (_timer != null) return;

        var intervalMinutes = _appState.Settings.BackupIntervalMinutes;
        if (intervalMinutes <= 0) intervalMinutes = 30;

        var interval = TimeSpan.FromMinutes(intervalMinutes);
        _timer = new Timer(TimerCallback, null, interval, interval);
        _logger.LogInformation("Backup timer started with interval of {Interval} minutes", intervalMinutes);
    }

    public void Restart()
    {
        Stop();
        Start();
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
        _logger.LogInformation("Backup timer stopped");
    }

    public async Task TriggerBackupNowAsync()
    {
        _logger.LogInformation("Manual backup triggered");
        var success = await _backupService.SendBackupAsync();
        OnBackupCompleted?.Invoke();
    }

    private async void TimerCallback(object? state)
    {
        if (_disposed) return;

        try
        {
            var success = await _backupService.SendBackupAsync();
            OnBackupCompleted?.Invoke();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Backup timer callback failed");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer?.Dispose();
    }
}
