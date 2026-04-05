# Implementation Plan

## Overview

This plan covers 6 feature areas from `ToDo.md`. The app is a Blazor WebAssembly client (`JtraClient`) backed by an ASP.NET Core server (`JtraServer`) and a shared model library (`JtraShared`). Data is persisted in IndexedDB via JS interop. JIRA API calls are proxied through the server to avoid CORS.

---

## 1. Tickets — Dialog-based Add/Remove

**Goal:** Replace the current inline add-form in `Tickets.razor` with a `TicketDialog.razor` in the `Dialogs/` folder, matching the pattern used by `TaskDialog.razor` / `Tasks.razor`.

**Steps:**

1. **Create `src/JtraClient/Dialogs/TicketDialog.razor`**
   - Parameters: `TicketCache Entry`, `EventCallback<TicketCache> OnSave`, `EventCallback<string> OnDelete`, `EventCallback OnClose`
   - Render a modal overlay (same CSS class `popup-overlay` / `popup-content` as `TaskDialog.razor`)
   - Fields: read-only Ticket Key (for edit), editable Summary; for new entries show an editable Ticket Key input
   - Buttons: Confirm, Delete (only when editing existing), Cancel
   - On Confirm: invoke `OnSave`; on Delete: invoke `OnDelete`; on Cancel/overlay click: invoke `OnClose`

2. **Refactor `src/JtraClient/Pages/Tickets.razor`**
   - Remove the inline `showAddForm` panel and its associated state (`newTicketKey`, `isSavingTicket`, `feedbackMessage`, etc.)
   - Add `showEditDialog` / `editingTicket` state (same pattern as `Tasks.razor`)
   - Wire "Add" button → open dialog with a blank `TicketCache`
   - Wire double-click on a row → open dialog with a copy of that ticket
   - Handle `OnSave` → call existing `AddTicketAsync` / update logic; handle `OnDelete` → call `DeleteTicketAsync`

---

## 2. Tasks — Hardcoded Default Tasks

**Goal:** Show 3 pinned/default task rows at the top of the Tasks list that cannot be deleted or edited.

**Steps:**

1. **Define the 3 default tasks as a static list in `Tasks.razor` (or a constant class)**
   ```
   { Category = "Break" }
   { Category = "Holiday", Subcategory = "Public" }
   { Category = "Holiday", Subcategory = "Private" }
   ```
   These have `Id = 0` (or negative sentinel) and are never persisted.

2. **Render them at the top of the list in `Tasks.razor`**
   - Before the `@foreach` over `AppState.AllTaskEntries`, render the 3 default rows with a distinct visual style (e.g., `entry-row entry-default` CSS class, slightly greyed out, no double-click handler).
   - Do not include them in export/import flows.

3. **Use them in worklog/check-in flows**
   - Wherever `AllTaskEntries` is iterated to populate dropdowns (e.g., `WorklogDialog.razor`), prepend the 3 defaults so they appear at the top of the selection list.

---

## 3. Charts Page

**Goal:** New page `/charts` showing a Radzen Sunburst chart of Category/Subcategory distribution over a selectable time range.

**Steps:**

1. **Add Radzen dependency** (if not already present)
   - Verify `JtraClient.csproj` references `Radzen.Blazor`; add it if missing.
   - Register Radzen services in `Program.cs` and add Radzen CSS/JS to `index.html`.

2. **Create `src/JtraClient/Pages/Charts.razor`** (`@page "/charts"`)

3. **Filter bar (top of page)**
   - `Label` text input → used as the chart title.
   - **Calendar / date-range picker button** that opens a dropdown with predefined ranges:
     - Today, Yesterday, Last 7 Days, This Week, Last Week, Last 2 Weeks, This Month, Last Month, This Year, Last Year
     - Selecting a range populates the From/To fields.
   - `From` DateTime input and `To` DateTime input (also editable manually).
   - `Excluded Tasks` multi-select (default: "Break" and "Holiday/Public").

4. **Data computation**
   - Filter `AppState.AllEntries` by date range and excluded tasks.
   - Group by `Category` → `Subcategory`, summing duration (use existing `DayAccumulatedHhmm` or compute from consecutive entry start times).
   - Build a hierarchical data structure suitable for the Radzen `RadzenChart` Sunburst series.

5. **Sunburst chart**
   - Use `<RadzenChart>` with a sunburst/pie series.
   - Set chart title from the Label field.
   - Show tooltips with hours and percentage.

6. **Navigation**
   - Add "Charts" link to the nav menu (`NavMenu.razor`).

---

## 4. Reports Page

**Goal:** New page `/reports` showing a monthly time report table.

**Steps:**

1. **Create `src/JtraClient/Pages/Reports.razor`** (`@page "/reports"`)

2. **Month selector** at the top (year + month dropdowns or a `<input type="month">`).

3. **Per-day table** with columns:
   | Date | Day | Expected HH:mm | Reported HH:mm | Deviation ±HH:mm | Reported Days | Deviation Days |
   - **Expected hours**: 8h for Mon–Fri; 0h for Sat/Sun; also 0h if that day has a worklog entry whose task is `Category=Holiday, Subcategory=Public` (public holiday).
   - **Reported hours**: sum of all `TimeEntry` durations for that date, excluding entries whose task is `Category=Holiday, Subcategory=Public`.
   - **Deviation**: Reported − Expected (positive = overtime).
   - **Reported Days**: Reported hours / 8.
   - **Deviation Days**: Deviation hours / 8.
   - Highlight weekends and public holidays visually.

4. **Monthly totals row** at the bottom:
   - Total Expected Hours / Total Reported Hours
   - Total Expected Days / Total Reported Days / Total Deviation (hours and days)

5. **Helper logic**
   - A small helper method `IsPublicHoliday(date)` that checks whether any `TimeEntry` on that date maps to the "Holiday/Public" default task.
   - Duration calculation: reuse `DurationCalculator` service or compute from consecutive `StartTime` entries per day.

6. **Navigation**
   - Add "Reports" link to the nav menu.

---

## 5. Settings Enhancements

### 5a. Read-only Email Field

- Add `Email` property to `AppSettings` (in `JtraShared/Models/AppSettings.cs`).
- In `Settings.razor`, add a read-only `<input type="email" readonly>` at the top of the form, bound to `settings.Email`.

### 5b. PAT — Read-only + Configure Dialog

1. **Make the PAT field read-only** in `Settings.razor` (show masked value).
2. **Add a "Configure" button** next to it that opens `PatDialog.razor`.
3. **Create `src/JtraClient/Dialogs/PatDialog.razor`**:
   - Instructions section: step-by-step guide to generate a JIRA PAT (static text/HTML).
   - Password input for the new token.
   - "Save" button:
     - Calls server endpoint `GET /api/jira/myself` (new endpoint, see below) with the entered PAT.
     - On success: stores PAT in settings, stores returned `emailAddress` in `settings.Email`, saves settings, closes dialog.
     - On failure: shows inline error message.
4. **New server endpoint** `GET /api/jira/myself` in `JiraController.cs`:
   - Accepts `X-Jira-Base-Url` and `X-Jira-Pat` headers.
   - Calls `GET {jiraBaseUrl}/rest/api/2/myself` and returns `{ emailAddress }`.

### 5c. Remove "Max Snooze Hours"

- Remove the `MaxSnoozeHours` form group from `Settings.razor`.
- Remove `MaxSnoozeHours` from `AppSettings.cs`.
- Find all usages of `MaxSnoozeHours` in `AppState.cs` / snooze logic and remove the cap (allow unlimited snooze duration).

### 5d. Backup Feature

1. **Server-side configuration** — add `BackupFolderPath` to `appsettings.json` and bind it in `Program.cs`.
2. **New server endpoint** `POST /api/backup` in a new `BackupController.cs`:
   - Accepts a JSON body containing all app data (settings, time entries, task entries, ticket cache).
   - Reads the user email from the payload (or a header).
   - Writes the JSON to `{BackupFolderPath}/{email}.json`.
3. **Client-side `BackupService.cs`** in `JtraClient/Services/`:
   - `CreateBackupPayloadAsync()`: reads all data from `IndexedDbService` and serializes to a JSON object.
   - `SendBackupAsync()`: POSTs the payload to `/api/backup`.
4. **Backup timer** — in `AppState.cs` or a new `BackupTimerService.cs`:
   - A periodic `System.Threading.Timer` (configurable interval, e.g., every 30 minutes) that calls `BackupService.SendBackupAsync()`.
   - Add `BackupIntervalMinutes` to `AppSettings` with a sensible default.
5. **Settings UI**:
   - Add `BackupIntervalMinutes` selector to `Settings.razor`.
   - Add a "Backup Now" button that calls `BackupService.SendBackupAsync()` immediately and shows success/error feedback.

---

## 6. Help Page

**Goal:** A static informational page describing the app.

**Steps:**

1. **Create `src/JtraClient/Pages/Help.razor`** (`@page "/help"`)
   - Content: app description, key concepts (Tasks, Tickets, Worklog, Check-in, Snooze), how to configure JIRA connection, keyboard shortcuts if any.
   - Use standard Bootstrap markup consistent with the rest of the app.

2. **Navigation**
   - Add "Help" link to the nav menu.

---

## Shared / Cross-cutting Changes

| Area | File(s) |
|---|---|
| Nav menu links (Charts, Reports, Help) | `src/JtraClient/Layout/NavMenu.razor` (or equivalent) |
| `AppSettings` model additions (`Email`, `BackupIntervalMinutes`) | `src/JtraShared/Models/AppSettings.cs` |
| Remove `MaxSnoozeHours` usages | `AppSettings.cs`, `AppState.cs`, `Settings.razor` |
| New JSON serialization entries for new types | `src/JtraClient/Services/JtraJsonContext.cs` |
| Server `appsettings.json` — `BackupFolderPath` | `src/JtraServer/appsettings.json` |

---

## Implementation Order (Suggested)

1. **Settings 5c** — Remove MaxSnoozeHours (smallest, no dependencies)
2. **Settings 5a** — Add Email field (model + UI only)
3. **Tasks 2** — Hardcoded default tasks (pure UI, no new services)
4. **Tickets 1** — TicketDialog refactor (UI pattern already established)
5. **Settings 5b** — PAT dialog + `/api/jira/myself` endpoint
6. **Settings 5d** — Backup service + server endpoint
7. **Reports 4** — Reports page (needs duration calculation logic)
8. **Charts 3** — Charts page (needs Radzen, most complex UI)
9. **Help 6** — Help page (static content, last)
