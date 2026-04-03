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

var host = builder.Build();

var appState = host.Services.GetRequiredService<AppState>();
var fallbackTimer = host.Services.GetRequiredService<FallbackTimerService>();

appState.WireUpFallbackTimer(fallbackTimer);

await appState.InitializeAsync();
fallbackTimer.Start();

await host.RunAsync();
