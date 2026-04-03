using Microsoft.JSInterop;

namespace JtraClient.Services;

public class NotificationService
{
    private readonly AppState _appState;
    private readonly IJSRuntime _jsRuntime;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(AppState appState, IJSRuntime jsRuntime, ILogger<NotificationService> logger)
    {
        _appState = appState;
        _jsRuntime = jsRuntime;
        _logger = logger;
    }

    public async Task<bool> RequestPermissionAsync()
    {
        var permission = await _jsRuntime.InvokeAsync<string>("notificationInterop.requestPermission");
        _logger.LogInformation("Notification permission: {Permission}", permission);
        return permission == "granted";
    }

    public async Task ShowNotificationAsync(string title, string body)
    {
        try
        {
            var status = await _jsRuntime.InvokeAsync<string>("notificationInterop.show", title, body);
            _appState.RecordNotificationAttempt(status);
            _logger.LogInformation("Notification attempt result: {Status}", status);
        }
        catch (Exception ex)
        {
            _appState.RecordNotificationAttempt("error");
            _logger.LogError(ex, "Failed to show notification");
        }
    }

    public async Task<bool> HasPermissionAsync()
    {
        return await _jsRuntime.InvokeAsync<bool>("notificationInterop.hasPermission");
    }
}
