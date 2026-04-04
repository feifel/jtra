using JtraServer.Controllers;
using JtraServer.Hubs;
using JtraServer.Services;
using Microsoft.AspNetCore.StaticFiles;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSignalR();
builder.Services.AddHostedService<TimerService>();
builder.Services.AddHttpClient();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

app.UseCors();

var contentTypeProvider = new FileExtensionContentTypeProvider();
contentTypeProvider.Mappings[".dat"] = "application/octet-stream";
contentTypeProvider.Mappings[".pdb"] = "application/octet-stream";
contentTypeProvider.Mappings[".wasm"] = "application/wasm";
contentTypeProvider.Mappings[".blat"] = "application/octet-stream";
contentTypeProvider.Mappings[".dll"] = "application/octet-stream";
contentTypeProvider.Mappings[".br"] = "application/x-br";

var staticFileOptions = new StaticFileOptions
{
    ContentTypeProvider = contentTypeProvider
};

app.UseDefaultFiles();
app.UseStaticFiles(staticFileOptions);

app.MapHub<TimerHub>("/timerHub");

app.MapJiraProxy();

var backupFolderPath = builder.Configuration["BackupFolderPath"] ?? "./backups";
app.MapBackup(backupFolderPath);

app.MapFallbackToFile("index.html");

app.Run();
