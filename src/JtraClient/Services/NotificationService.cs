using Microsoft.JSInterop;

namespace JtraClient.Services;

public class NotificationService
{
    private readonly IJSRuntime _jsRuntime;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(IJSRuntime jsRuntime, ILogger<NotificationService> logger)
    {
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
            await _jsRuntime.InvokeVoidAsync("notificationInterop.show", title, body);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to show notification");
        }
    }

    public async Task<bool> HasPermissionAsync()
    {
        return await _jsRuntime.InvokeAsync<bool>("notificationInterop.hasPermission");
    }
}
