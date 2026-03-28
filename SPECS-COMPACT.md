# JTRA – JIRA Time Recording Application

## Overview

Cross-platform web app that reminds users every 15 minutes to confirm/change their current task, records time against JIRA tickets in IndexedDB, and submits worklogs to JIRA on request.

## Tech Stack

| Layer | Technology |
|---|---|
| Server | .NET 8 (Kestrel) - single central deployment |
| Frontend | Blazor WebAssembly |
| Storage | IndexedDB (browser-local) |
| Real-time | SignalR for timer events |
| Notifications | Web Notifications API |
| JIRA API | HttpClient on server (no CORS issues) |

## Architecture

Central server hosts Blazor UI and timer. Each browser stores its own data in IndexedDB (entries, settings, PAT, ticket cache). Server pushes timer events via SignalR; client falls back to local timer if disconnected.

## Data Stores (IndexedDB)

**time_entries**
| Field | Description |
|---|---|
| id | auto-increment PK |
| date | YYYY-MM-DD |
| start_time | HH:mm (rounded to 15-min) |
| type | Ticket / Break / Messages / Support / Meetings / ChangeMgmt / Reviews / Training / Other |
| ticket | JIRA key (required for Ticket type, auto-filled if type has linked ticket) |
| description | max 100 chars |
| duration | HH:mm (rounded, written when slot ends) |
| day_accumulated_hhmm | running total (breaks excluded) |
| day_accumulated_days | accumulated / 8h |
| day_target_hhmm | target for this day |
| day_deviation_hhmm | accumulated - target |
| day_deviation_days | deviation / 8h |
| submitted_to_jira | boolean |

**settings** - key/value store for: jiraBaseUrl, PAT, defaultTargetHours (08:00), maxSnoozeHours (4), autoConfirmBreakMinutes (10), cacheTtlDays (7), configurableTypes (JSON)

**ticket_cache** - ticket_key, summary, fetched_at, expires_at

**connection_state** - last_task at browser close

## Task Types

**Built-in (not modifiable):**
- `Ticket` - requires JIRA ticket, submitted to JIRA
- `Break` - no ticket, not submitted

**Configurable (enable/disable/rename in settings, optional linked ticket):**
Messages, Support, Meetings, ChangeMgmt, Reviews, Training, Other

Types with linked ticket: auto-fills ticket field (read-only), submits to JIRA.

## Timer Behavior

- Fires at hh:00/15/30/45 boundaries (server BackgroundService via SignalR)
- On startup: always show check-in popup (empty fields)
- On timer fire: show browser notification + modal popup
- User can snooze to custom time (client-side, in-memory only, max 4h)
- During snooze: ignore timer events until snooze time
- Client fallback timer when disconnected from server

## Check-In Popup

- Default selection: current task (for periodic check-ins)
- Enter/Escape = confirm current
- Can change: type, ticket, description
- If task changes: write completed entry to IndexedDB, create new in-progress entry
- If same task: nothing to do
- No interaction after 7 min → auto-start Break
- Break auto-confirms after 10 min inactivity

## Time Rounding

Both start_time and duration rounded to nearest 15-min boundary:
- 53–07 → 00
- 08–22 → 15
- 23–37 → 30
- 38–52 → 45

## Entry Lifecycle

1. Task starts → write entry with date, start_time, type, ticket, description, day_target_hhmm
2. Next task starts → calculate duration and day_* fields, write to previous entry
3. End time = next entry's start_time (continuous timeline)

## Cross-Midnight Handling

If previous day's last task was not Break: auto-create entry at 00:00 on new day with same task details.

## Editing

Double-click row to edit: date, start_time, type, ticket, description, day_target_hhmm. On save, recalculate all dependent fields for this and subsequent entries.

## JIRA Integration

**Authentication:** PAT stored in IndexedDB, sent as Bearer token via server proxy (server never stores PAT).

**Ticket Summary:** GET /rest/api/2/issue/{key}?fields=summary - cached 7 days.

**Worklog Submission:**
```
POST /rest/api/2/issue/{key}/worklog
Authorization: Bearer <PAT>
{
  "timeSpentSeconds": 3600,
  "started": "2026-03-26T09:00:00.000+0100",
  "comment": "description"
}
```

**Grouping:** Consecutive entries with same ticket AND same description → single worklog. Different days never grouped.

## Main Window

- Today's stats at top (target, logged, deviation)
- Scrollable log of all entries, newest first
- Date group headers with day totals
- Filter by date/ticket/description
- "Next check-in: XX:XX" with change time button

## CSV Export/Import

Export: all entries to CSV. Import: overwrite all data.

Format: date,start_time,type,ticket,description,duration,day_accumulated_hhmm,day_accumulated_days,day_target_hhmm,day_deviation_hhmm,day_deviation_days,submitted_to_jira

## Project Structure

```
jtra/src/
├── JtraServer/           # .NET 8 server
│   ├── Program.cs
│   ├── Services/TimerService.cs, JiraService.cs
│   ├── Hubs/TimerHub.cs
│   ├── Controllers/JiraController.cs
│   └── wwwroot/          # Blazor WASM files
├── JtraClient/           # Blazor WebAssembly
│   ├── Pages/MainView.razor, CheckInPopup.razor, DayStartDialog.razor, EditEntryDialog.razor, SubmitView.razor, SettingsView.razor
│   └── Services/TimerHubClient.cs, FallbackTimerService.cs, NotificationService.cs, IndexedDbService.cs, CsvExportService.cs, AppState.cs
└── JtraShared/           # Shared models (optional)
```

## Non-Functional

- Cross-browser: Chrome, Firefox, Edge, Safari
- PWA installable
- Works offline (JIRA only needed for ticket lookup/worklog)
- Data never on server
- PAT in browser sandbox only

## Out of Scope (v1)

Reporting, multi-device sync, multiple JIRA instances, offline worklog queue, approvals, mobile.

## Development Phases

**Phase 1:** Server + Blazor scaffold, timer, SignalR, IndexedDB, day-start dialog, main UI, edit entries, CSV export/import

**Phase 2:** JIRA settings, ticket cache, ticket dropdown, worklog submission, delete/insert entries

**Phase 3:** Gap indicator, break auto-confirm, PWA, Docker
