using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.Logging;
using JtraClient;
using JtraClient.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.Logging.SetMinimumLevel(LogLevel.Information);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var serverUrl = builder.Configuration["ServerUrl"] ?? builder.HostEnvironment.BaseAddress;
builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(serverUrl) });

builder.Services.AddScoped<IndexedDbService>();
builder.Services.AddScoped<CsvExportService>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddSingleton<AppState>();
builder.Services.AddSingleton<FallbackTimerService>();
builder.Services.AddSingleton<TimerHubClient>();

var host = builder.Build();

var appState = host.Services.GetRequiredService<AppState>();
var timerHub = host.Services.GetRequiredService<TimerHubClient>();
var fallbackTimer = host.Services.GetRequiredService<FallbackTimerService>();

appState.WireUpTimerHub(timerHub);
appState.WireUpFallbackTimer(fallbackTimer);
appState.OnServerConnectionChanged += connected =>
{
    if (connected) fallbackTimer.Stop();
    else fallbackTimer.Start();
};

await appState.InitializeAsync();
await timerHub.StartAsync(serverUrl);

await host.RunAsync();
