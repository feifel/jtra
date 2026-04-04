# JTRA Refactoring & Enhancement Plan

## 🎯 Goal
Modernize the codebase, improve UX consistency, and add new features while maintaining existing functionality.

---

## Phase 0: Pre-Refactor Cleanup (Optional but Recommended)
- [ ] Remove unused CSS/JS assets  
- [ ] Standardize naming conventions (`PascalCase` for Razor components)  
- [ ] Ensure all models are `record` or `class` with `IEquatable<T>` where needed  

---

## Phase 1: File Renaming & Structural Refactoring

### ✅ Rename Pages
| Old Path | New Path |
|---------|----------|
| `Pages/Home.razor` → `Pages/Worklog.razor` |  
| `Pages/TaskTypes.razor` → `Pages/Tasks.razor` |  
| `Pages/TicketsView.razor` → `Pages/Tickets.razor` |  
| `Pages/SettingsView.razor` → `Pages/Settings.razor` |  

### ✅ Move & Rename Dialogs
| Old Path | New Path |
|---------|----------|
| `Pages/EditEntryDialog.razor` → `Dialogs/Worklog.razor` |  
| `Pages/EditTaskDialog.razor` → `Dialogs/Task.razor` |  

> 💡 **Note**: Create new `Dialogs/` folder at root of `JtraClient`.

### ✅ Update Routing (`App.razor`, `Program.cs`)
- Replace `/home` → `/worklog`
- Replace `/tasktypes` → `/tasks`
- Replace `/ticketsview` → `/tickets`
- Replace `/settingsview` → `/settings`
- Ensure dialog routes use `<Dialog>` components (e.g., via Radzen or custom modal system)

### ✅ Update `_Imports.razor`
- Add `@using JtraClient.Dialogs`  
- Confirm all model/service imports are present  

---

## Phase 2: Worklog Page Enhancements

### 📄 `Pages/Worklog.razor`
- [ ] Add header with title ("Worklog") and action buttons (e.g., "Add", "Export")
- [ ] Replace inline add-row with today’s entries summary block above list  
- [ ] Use dynamic dialog title: `"Edit Worklog"` / `"Add Worklog"` based on context  

### 📄 `Dialogs/Worklog.razor`
- [ ] Implement duplicate prevention logic:
  - Skip if same `TaskId` + overlapping time window (within tolerance)
  - Only allow different tasks or non-overlapping times  
- [ ] Bind to `AppState.TimeEntries`, update via `IndexedDbService`

### 📄 `Services/AppState.cs`
- [ ] Add computed property: `TodayTimeEntries`  
- [ ] Add method: `TryAddEntryWithoutDuplicates(TimeEntry entry)`  

---

## Phase 3: Tickets & Tasks

### 📄 `Pages/Tickets.razor`
- [ ] Implement new dialog (`Dialogs/Ticket.razor`) for ticket creation/editing  
- [ ] Reuse pattern from `Task.razor` (dialog, form, validation)  

### 📄 `Dialogs/Task.razor`
- [ ] Keep existing functionality  
- [ ] Add "Break", "Holiday" as top-priority default tasks in dropdown (hardcoded)  

### 📄 `Models/TimeEntry.cs`, `Models/TaskEntry.cs`
- [ ] Ensure `IsDefault` flag or similar for system tasks  
- [ ] Add `IsExcludedFromReports` property if needed  

---

## Phase 4: Charts Page

### 📄 `Pages/Charts.razor` *(new)*
- [ ] Radzen Sunburst chart (using `Radzen.Blazor.Chart`)  
- [ ] Filter UI:
  - Preset dropdown: "Last Week", "This Month", etc.
  - From/To date pickers  
  - Excluded tasks multiselect (default: Break, Public Holiday)  

### 📄 `Services/DurationCalculator.cs`
- [ ] Add method: `CalculateTimeByCategory(IEnumerable<TimeEntry>, IEnumerable<string> excludedTaskIds)`  
- [ ] Return dictionary for chart data binding  

---

## Phase 5: Reports Page

### 📄 `Pages/Reports.razor` *(new)*
- [ ] Month selector (dropdown or calendar)  
- [ ] Daily table:
  - Columns: Date, Expected Hours, Reported Hours, Deviation  
  - Exclude Public Holidays from expected/reported totals  
- [ ] Totals row at bottom  

### 📄 `Services/AppState.cs`, `IndexedDbService.cs`
- [ ] Add method: `GetDailyReport(DateTime month)` → list of daily summaries  
- [ ] Use `AppState.Settings.PublicHolidays` to filter excluded days  

---

## Phase 6: Settings Page

### 📄 `Pages/Settings.razor`
- [ ] Read-only email field (populated via JIRA API `/rest/api/2/myself`)  
- [ ] PAT field:
  - Make readonly  
  - Show "Configure" button → opens modal to enter PAT & validate  
- [ ] Remove "Max Snooze Hours" setting  
- [ ] Backup section:
  - Timer interval input (hours)  
  - Manual backup button  
  - Export JSON + POST to `/api/jira/backup`  

### 📄 `Services/JiraTicketService.cs`
- [ ] Add method: `GetMyselfAsync()` → returns email  
- [ ] Add method: `ValidatePatAsync(string pat)` → returns success/failure  

### 📄 `Server/JiraController.cs`
- [ ] Add `[HttpPost("backup")]` endpoint to accept JSON and store server-side  

---

## Phase 7: Help Page

### 📄 `Pages/Help.razor` *(new)*
- [ ] App description (short, friendly)  
- [ ] Key features list  
- [ ] Contact/support info  

---

## ✅ Files to Add for Next Steps

| Category | File(s) |
|---------|---------|
| **Core** | `src/JtraClient/Pages/_Imports.razor` *(to be added)* |
| **Routing/Config** | Already covered via `Program.cs`, `App.razor` |
| **Server API** | Confirm `JiraController.cs` supports `/backup` endpoint |

Once `_Imports.razor` is added, I’ll begin Phase 1 implementation immediately.
