# JTRA Development Guide

## Project Overview

JTRA is a Blazor WebAssembly time-tracking application with an ASP.NET Core server backend. Users track work against JIRA tickets via a browser-based UI, with data stored in IndexedDB. The app features recurring check-in prompts (every 15 minutes), SignalR real-time communication, and CSV export/import.

### Architecture

- **JtraClient**: Blazor WebAssembly frontend (.NET 8)
- **JtraServer**: ASP.NET Core backend server (.NET 8)  
- **JtraShared**: Shared models library
- **Database**: Browser IndexedDB (client-side only)

---

## Build & Test Commands

### Solution Structure

```
src/
├── Jtra.sln              # Main solution file
├── JtraClient/           # Blazor WebAssembly client
├── JtraServer/           # ASP.NET Core server
└── JtraShared/           # Shared models
```

### Build Commands

```bash
# Build entire solution
dotnet build src/Jtra.sln

# Build specific project
dotnet build src/JtraClient/JtraClient.csproj
dotnet build src/JtraServer/JtraServer.csproj
dotnet build src/JtraShared/JtraShared.csproj

# Release build
dotnet build -c Release src/Jtra.sln

# Publish server (includes client assets)
dotnet publish -c Release src/JtraServer/JtraServer.csproj
```

### Run Commands

```bash
# Run server (client served from wwwroot)
dotnet run --project src/JtraServer/JtraServer.csproj

# Run client in dev mode (hot reload)
dotnet run --project src/JtraClient/JtraClient.csproj

# Debug build with verbose output
dotnet build -c Debug -v diag src/Jtra.sln
```

### Test Commands

**Note**: This project does not currently have automated unit or integration tests. Manual testing is performed via browser UI.

To run the application and test manually:
```bash
dotnet run --project src/JtraServer/JtraServer.csproj
# Then open http://localhost:5000 in a browser
```

---

## Code Style Guidelines

### C# Coding Conventions

#### Naming

- **Classes**: PascalCase (e.g., `AppState`, `CsvExportService`)
- **Interfaces**: IPascalCase (e.g., `IService` if used)
- **Methods**: PascalCase (e.g., `SaveSettingsAsync`, `GetTimeEntriesAsync`)
- **Properties**: PascalCase (e.g., `TodayAccumulated`, `IsConnectedToServer`)
- **Private fields**: camelCase with underscore prefix (e.g., `_logger`, `_indexedDb`)
- **Local variables**: camelCase (e.g., `entriesForDate`, `currentEntry`)
- **Constants**: PascalCase (e.g., `SeedTaskEntries`)

#### File Organization

- One class per file, matching the filename
- Files organized by feature area:
  - `Models/` - Shared data models
  - `Services/` - Client-side services
  - `Pages/` - Page components
  - `Dialogs/` - Modal dialog components
  - `Controllers/`, `Hubs/`, `Services/` (server)

#### Namespaces

- Use feature-specific namespaces: `JtraClient.Services`, `JtraShared.Models`
- Avoid deep nesting; keep it simple

### C# Language Features

#### Nullability

- Nullable reference types enabled (`<Nullable>enable</Nullable>`)
- Always check for null before accessing properties
- Use null-forgiving operator (`!`) only when you're certain the value is not null
- Prefer `string?` over non-nullable strings when empty values are valid

#### Async/Await

- All I/O operations use async/await pattern
- Method names end with `Async` (e.g., `LoadCachedTicketsAsync`)
- Use `async Task` for void-returning async methods in event handlers
- Don't block on async code; avoid `.Result` or `.Wait()`

#### Error Handling

```csharp
// Prefer early returns and validation
if (string.IsNullOrWhiteSpace(value))
{
    return Results.BadRequest("Value is required");
}

// Use try-catch for expected failure points
try
{
    var response = await httpClient.SendAsync(request);
    // Handle response
}
catch (Exception ex)
{
    _logger.LogError(ex, "Error performing operation");
    return Results.Problem(ex.Message);
}
```

#### LINQ Usage

- Prefer method syntax over query syntax
- Chain methods for readability:
```csharp
var filtered = entries
    .Where(e => e.Date == targetDate)
    .OrderBy(e => e.StartTime)
    .ThenBy(e => e.Id)
    .ToList();
```

### Blazor Component Conventions

#### File Naming

- Components: PascalCase.razor (e.g., `WorklogDialog.razor`)
- Pages: PascalCase.razor in `Pages/` folder
- Dialogs: PascalCase.razor in `Dialogs/` folder

#### Component Parameters

```csharp
[Parameter] public TicketCache Entry { get; set; } = new();
[Parameter] public EventCallback<(string, string)> OnSave { get; set; }
[Parameter] public EventCallback<string> OnDelete { get; set; }
[Parameter] public EventCallback OnClose { get; set; }
```

#### Event Callback Naming

- Use `On<Action>` pattern (e.g., `OnSave`, `OnClose`)
- For complex data, use tuples: `EventCallback<(string TicketKey, string Summary)>`
- Always invoke callbacks with `await` in async methods

#### State Management

- Components implement `IDisposable` to unsubscribe from events
- Use `AppState.OnChange += StateHasChanged` pattern
- Call `StateHasChanged()` after state updates if needed
- Prefer unidirectional data flow: parent owns state, children receive callbacks

### TypeScript/JavaScript Conventions (Minimal)

The project uses minimal JavaScript for IndexedDB interop via IJSRuntime. When writing JS:

- Use module pattern or ES6 modules
- Keep JS separate from C# logic
- Use explicit error handling in JS interop

### CSS/Styling Conventions

- Use Bootstrap classes where available
- Component-specific styles in `wwwroot/css/app.css`
- Modal dialogs use `.popup-overlay` and `.popup-content` classes
- Form elements use standard Bootstrap `.form-control`, `.btn`, etc.

#### CSS Classes

| Class | Purpose |
|-------|---------|
| `entry-row` | List item row |
| `entry-header` | Header row in lists |
| `entries-list` | Container for entry lists |
| `popup-overlay` | Modal overlay background |
| `popup-content` | Modal dialog content |
| `form-group` | Form field container |

---

## Important Patterns

### State Management (AppState)

- Singleton service managing all application state
- Triggers re-render via `OnChange` event
- Centralized data access point for time entries, tasks, tickets, settings

### IndexedDB Pattern

1. Initialize JS interop in `IndexedDbService.InitializeAsync()`
2. Use source-gen JSON context (`JtraJsonContext`) for serialization
3. Strip ID from new entries to allow auto-increment

```csharp
// Example: Add new entry
var json = JsonSerializer.Serialize(entry, JtraJsonContext.Default.TimeEntry);
using var doc = JsonDocument.Parse(json);
// ... strip ID field ...
await _jsRuntime.InvokeVoidAsync("indexedDbInterop.addEntry", stripped);
```

### SignalR Pattern

- Server-side `TimerService` fires every minute
- Sends timer ticks to all clients via SignalR Hub
- Client-side `TimerHubClient` receives events and triggers check-ins
- Fallback timer activates on disconnect

---

## Key Files Reference

| File | Purpose |
|------|---------|
| `src/JtraClient/Program.cs` | Client DI setup, service registration |
| `src/JtraServer/Program.cs` | Server DI setup, middleware configuration |
| `src/JtraShared/Models/AppSettings.cs` | User settings model |
| `src/JtraShared/Models/TimeEntry.cs` | Time tracking entry |
| `src/JtraShared/Models/TaskEntry.cs` | Task/task type definition |
| `src/JtraClient/Services/AppState.cs` | Main state management |
| `src/JtraClient/Services/IndexedDbService.cs` | IndexedDB interop |
| `src/JtraServer/Hubs/TimerHub.cs` | SignalR hub for timer events |
| `src/JtraServer/Controllers/JiraController.cs` | JIRA API proxy endpoints |

---

## Common Tasks

### Adding a New Setting

1. Add property to `AppSettings` in `JtraShared/Models/AppSettings.cs`
2. Update Settings page UI in `Settings.razor`
3. Update default values as needed
4. Restart app to apply new setting

### Adding a New Page

1. Create `.razor` file in `Pages/` folder with `@page "/path"` directive
2. Add navigation link in `NavMenu.razor`
3. Register any required services in `Program.cs`

### Modifying CSV Format

- Update header row in `CsvExportService.ExportToCsv()`
- Ensure import parsing matches export format
- Update validation checks for header matching

---

## Deployment Notes

### Server Deployment

The server is a standard .NET 8 web application deployable to:
- Windows Server (IIS or Kestrel)
- Linux (systemd, Docker)
- Cloud platforms (Azure App Service, AWS)

```bash
# Publish for deployment
dotnet publish -c Release -o ./publish src/JtraServer/JtraServer.csproj

# Run published app
dotnet ./publish/JtraServer.dll
```

### Client Build Process

The server project automatically publishes the client during build:
1. `JtraClient` is built and published to `bin/BlazorPublish`
2. Files are copied to `wwwroot/_framework`
3. Server serves static files from `wwwroot`

---

## Testing Guidelines

This project uses manual testing via browser UI:

### Manual Test Checklist

- [ ] Timer fires at correct 15-minute boundaries
- [ ] Check-in popup shows current task
- [ ] Add/edit/delete time entries works correctly
- [ ] CSV export/import preserves data integrity
- [ ] JIRA ticket summary fetches correctly
- [ ] Worklog submission to JIRA succeeds
- [ ] Settings persist across reloads
- [ ] IndexedDB data survives page refresh

---

## Troubleshooting

### Build Errors

- Clean and rebuild: `dotnet clean && dotnet build`
- Delete `bin/obj` folders if stale builds occur
- Ensure .NET 8 SDK is installed

### Runtime Issues

- Check browser console for JavaScript errors
- Verify SignalR connection in Network tab
- Confirm IndexedDB has data in Application tab
- Check server logs for API errors

### Debugging

```bash
# Enable detailed logging
dotnet run --project src/JtraServer/JtraServer.csproj -- --verbose

# In Visual Studio, set multiple startup projects
# - JtraServer (server)
# - JtraClient (client dev server)
```

---

## Related Documentation

- `Plan.md` - Implementation plan and feature roadmap
- `SPECS.md` - Detailed technical specification
- `ToDo.md` - Pending features and improvements
- `RuntimeErrorDueToInconsistentSdk.md` - SDK troubleshooting notes
