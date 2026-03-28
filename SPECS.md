# JTRA – JIRA Time Recording Application: Specification

## 1. Overview

JTRA is a cross-platform web application that reminds the user every 15 minutes to confirm or change their current work task, records time against JIRA tickets, stores data locally in the browser's IndexedDB, and — on request — submits recorded time to JIRA as worklogs via the JIRA REST API.

---

## 2. Technology Stack

### Hybrid Architecture: Central Server + Browser-Local Storage

| Layer | Technology | Rationale |
|---|---|---|
| Application | **.NET 8 server** (Kestrel) deployed centrally | Single deployment for all users, IT-managed |
| UI / Frontend | **Blazor WebAssembly** | Runs in browser, rich interactivity, shared C# code with server |
| Language | **C# / .NET 8** | Unified language across frontend and backend |
| Styling | **Blazor default styling** | Simple, no additional CSS framework required |
| Time data | **IndexedDB** (browser) | Data stored locally in each user's browser; no server-side storage |
| Settings | **IndexedDB** (browser) | Per-user settings stored locally |
| Secrets (JIRA PAT) | **IndexedDB** (browser) | Stored per-user, never sent to server except as Bearer token for API calls |
| Background timer | **Server-side BackgroundService** | Reliable timer aligned to clock boundaries (`hh:00/15/30/45`) |
| Real-time communication | **SignalR** | Server pushes timer events to all connected browsers |
| Notifications | **Web Notifications API** | System-level notifications (Windows Action Center, macOS Notification Center, Linux notifications) when timer fires; works like Outlook web app |
| JIRA API | **HttpClient on server** | No CORS restrictions; PAT sent as Bearer token |
| Data portability | **CSV export/import** | Users can export IndexedDB data to CSV and import back |

### Architecture Overview

```
┌──────────────────────────────────────────────────────────────────┐
│                     Central Server                               │
│                                                                  │
│   ┌──────────────────────────────────────────────────────────┐   │
│   │  JtraServer (.NET 8)                                     │   │
│   │                                                          │   │
│   │   ┌─────────────┐    ┌─────────────┐    ┌────────────┐   │   │
│   │   │ Static Files│    │  REST API   │    │   Timer    │   │   │
│   │   │ (Blazor UI) │    │  (JIRA proxy│    │  Service   │   │   │
│   │   │             │    │   only)     │    │            │   │   │
│   │   └─────────────┘    └─────────────┘    └────────────┘   │   │
│   │                          │                    │          │   │
│   │                    SignalR Hub                │          │   │
│   │                          │                    │          │   │
│   │                   Kestrel (port 5000)         │          │   │
│   └──────────────────────────┼────────────────────┼──────────┘   │
│                              │                                   │
└──────────────────────────────┼───────────────────────────────────┘
                               │
                 ┌─────────────┼─────────────┐
                 │             │             │
                 ▼             ▼             ▼
          ┌───────────┐  ┌───────────┐  ┌───────────┐
          │ Browser   │  │ Browser   │  │ Browser   │
          │ (User A)  │  │ (User B)  │  │ (User C)  │
          │           │  │           │  │           │
          │ IndexedDB │  │ IndexedDB │  │ IndexedDB │
          │ - Time    │  │ - Time    │  │ - Time    │
          │   entries │  │   entries │  │   entries │
          │ - Settings│  │ - Settings│  │ - Settings│
          │ - PAT     │  │ - PAT     │  │ - PAT     │
          └───────────┘  └───────────┘  └───────────┘
                               │
                               ▼
                          JIRA Server
```

### Why Hybrid Architecture?

| Requirement | Hybrid (Central Server) | Self-Hosted |
|---|---|---|
| IT deployment simplicity | ✅ Single server to deploy/maintain | ❌ Deploy to each user's machine |
| No user installation | ✅ Just open URL in browser | ✅ Run single .exe |
| Timer reliability | ✅ Server-side with SignalR | ✅ Server-side |
| JIRA CORS bypass | ✅ Server proxy | ✅ Server proxy |
| Data stored on user's machine | ✅ IndexedDB in browser | ✅ IndexedDB in browser |
| Works offline | ✅ PWA with cached UI | ✅ Full offline |
| Multiple users | ✅ Single server serves all | ❌ One instance per user |
| Browser tab required | ✅ Yes | ❌ Server runs independently |
| Data portability | ✅ CSV export/import | ✅ CSV export/import |

### Server Deployment

The central server is a standard .NET 8 web application that can be deployed to:
- Windows Server (IIS or standalone Kestrel)
- Linux (systemd service, Docker container)
- Cloud hosting (Azure App Service, AWS, etc.)

Deployment options:
```bash
# Publish for server deployment
dotnet publish -c Release -o ./publish

# Or as Docker container
docker build -t jtra-server .
```

---

## 3. Functional Requirements

### 3.1 Recurring Check-In Popup (15-minute timer)

- The timer fires at **fixed clock boundaries**: `hh:00`, `hh:15`, `hh:30`, `hh:45` — regardless of when the app was started or when the last confirmation occurred.
  - On startup, the timer calculates the time until the next boundary and schedules the first firing accordingly.
  - Example: if the app starts at 09:23, the first popup fires at 09:30.
- A **browser notification** is shown when the timer fires: "Time to check in: What are you working on?"
- A **modal dialog** appears in the browser UI (triggered via SignalR from server).
- **The current task is shown prominently** as the default selection.
- The user can:
  - Press **Enter** (or click Confirm) to continue the current task unchanged.
  - Change the **type** to switch activity category (e.g. from `Ticket` to `Meetings`).
  - Change the **JIRA ticket** or **description** within the current type.
  - Select type **`Break`** to start a break.
- Keyboard shortcuts: **Enter** = confirm current, **Escape** = confirm current.
- **Snooze / reschedule next popup**: The user can manually change when the next popup will appear, useful for meetings or focused work sessions.
- **App close behavior**: When the browser tab is closed, the current task and time are saved to IndexedDB. On the next session, a non-intrusive UI indicator shows the user that there may be missing entries to add.

#### Next Popup Time Display and Control

- The main window and check-in popup display **when the next popup will occur**.
- The user can **manually adjust the next popup time**:
  - A time picker or preset buttons (e.g., +30min, +1h, +2h, "at 14:00") allow rescheduling the next check-in.
  - Useful when the user knows they will be in a long meeting or focused work session and don't want interruptions every 15 minutes.
  - Example: If the user is in a 2-hour meeting from 10:00 to 12:00, they can set the next popup to 12:00 instead of being asked at 10:15, 10:30, etc.
- When a custom next popup time is set:
  - The timer is rescheduled to fire at the specified time instead of the next 15-minute boundary.
  - After the custom popup fires, the timer resumes normal 15-minute boundary scheduling.
  - The custom popup time must be in the future (at least 1 minute ahead).
  - The maximum snooze duration is configurable in Settings (default: 4 hours).

#### Auto-confirm During a Break

If the active task is **`break`** and the check-in popup is not interacted with within **10 minutes** (configurable), the popup auto-confirms the break and extends it by one additional 15-minute slot. A counter in the popup shows the remaining auto-confirm time.

#### Timer Resilience

The client stores its current task in IndexedDB (`connection_state` store) before the browser unloads (using `beforeunload` event). On startup, the app:

1. Reads the last entry from IndexedDB to determine what was being tracked
2. Compares the last entry's start time with the current time
3. If there's a time gap (user was away), displays a **non-intrusive indicator** in the UI: "Last tracked: XX:XX. Add missing entries?"
4. User can click to add entries for the gap period, or ignore it

The server does not track client disconnect/reconnect. The user is responsible for filling any gaps in their time tracking. This keeps the architecture simple and puts the user in control.

On startup, the app waits for the next SignalR timer event from the server.

### 3.2 Task Entry

A task entry consists of:

| Field | Rules |
|---|---|
| **Type** | Selected from the type dropdown (see below). Determines whether a JIRA ticket field is required and whether the entry is eligible for worklog submission. |
| **JIRA Ticket** | Required only when type is `Ticket`. Auto-filled (and read-only) when the selected configurable type has a linked ticket configured. Hidden/disabled for types with no ticket association. Must match `[A-Z]+-[0-9]+` (e.g. `PROJ-123`). |
| **Description** | Free text, max 100 characters. Pre-filled with the previous description for the same ticket/type combination. |

#### IndexedDB Entry Lifecycle

When a task is started, an entry is written immediately to IndexedDB with the following fields populated:
- `date`, `start_time`, `type`, `ticket`, `description`, `day_target_hhmm`

The remaining calculated fields (`duration`, `day_accumulated_hhmm`, `day_accumulated_days`, `day_deviation_hhmm`, `day_deviation_days`) are computed and written when the user starts the next task (i.e., when the current slot ends).

#### Editing Historical Entries

Users can edit historical entries at any time by **double-clicking** a row in the main window table. Only the editable columns can be modified: `date`, `start_time`, `type`, `ticket`, `description`, `day_target_hhmm`. When an entry is edited, all dependent calculated columns for that row and all subsequent rows are recalculated (similar to spreadsheet behavior).

> **Future enhancement (Phase 2):** Allow users to **delete** and **insert** entries, with automatic recalculation of all subsequent rows.

#### Type Dropdown

The type is always selected first. It determines what fields are shown below it.

**Built-in types (always present, not removable):**

| Value | Description | JIRA ticket required | Submitted to JIRA |
|---|---|---|---|
| `Ticket` | Work on a specific JIRA ticket — the primary use case | Yes | Yes |
| `Break` | A break period | No | No |

**Configurable types (enabled/disabled individually in Settings, labels editable):**

| Default label | Default description | JIRA ticket required | Submitted to JIRA |
|---|---|---|---|
| `Messages` | Reading and answering emails / Teams messages | No (optional linked ticket) | If linked ticket configured |
| `Support` | Helping other people or teams | No (optional linked ticket) | If linked ticket configured |
| `Meetings` | All types of meetings | No (optional linked ticket) | If linked ticket configured |
| `ChangeMgmt` | Release preparation: deployment plans, release notes, deployments | No (optional linked ticket) | If linked ticket configured |
| `Reviews` | Code reviews and merge request approvals | No (optional linked ticket) | If linked ticket configured |
| `Training` | Internal/external training, webinars | No (optional linked ticket) | If linked ticket configured |
| `Other` | Anything that does not fit another category | No (optional linked ticket) | If linked ticket configured |

- All configurable types are **enabled by default**.
- The label of each configurable type can be renamed by the user in Settings.
- Configurable types can be **disabled** (hidden from the dropdown) but not deleted.
- The order of types in the dropdown is fixed: built-in types first (`Ticket`, `Break`), then configurable types in the order listed above.

#### Linked Ticket for Configurable Types

Each configurable type can optionally have a **linked JIRA ticket** configured in Settings. When a linked ticket is set:

- Selecting that type in the check-in popup **automatically populates** the ticket field with the configured ticket number.
- The ticket field is shown but **read-only** (the user cannot change it).
- The entry is written to IndexedDB with that ticket number in the `ticket` column.
- The entry **is included in JIRA worklog submission**, using the linked ticket as the target issue.

This is useful for recurring overhead activities that map to a fixed JIRA ticket. For example:
- `Support` → linked ticket `SUP-123` (all support time logged against that ticket)
- `Meetings` → linked ticket `OVERHEAD-7`
- `Training` → linked ticket `TRAIN-1`

If no linked ticket is configured for a type, the ticket field is hidden and the entry is not submitted to JIRA.

#### JIRA Ticket Dropdown (shown only when type = `Ticket`)

- Lists all previously used JIRA tickets, **most recently used first**.
- Each entry displays: `TICKET-123 – <summary fetched from JIRA>`.
- Supports **live search/filter** as the user types.
- Ticket summaries are fetched from JIRA once per ticket and **cached locally** in IndexedDB (`ticket_cache` store), invalidated after 7 days (configurable).

### 3.3 Day Start Dialog

When the user opens the app or logs their first task of a new calendar day, a day-start dialog is shown:

- Today's date (read-only)
- **Target hours** for today — defaults to `08:00` (configurable), editable for this specific day (e.g. `04:00` for a half-day holiday)
- The per-day override is stored in the config file keyed by date (`YYYY-MM-DD`)

### 3.4 Time Rounding

All slot durations are **rounded up to the next 15-minute boundary** and stored in `HH:mm` format:

| Actual elapsed | Recorded duration |
|---|---|
| 00m 00s – 07m 00s | 00:00 |
| 07m 00s – 22m 00s | 00:15 |
| 22m 00s – 37m 00s | 00:30 |
| 37m 00s – 52m 00s | 00:45 |
| 52m 00s – 67m 00s | 01:00 |

Start times are also **rounded to the nearest 15-minute boundary**. For example:
- 09:07 → 09:00
- 09:08 → 09:15
- 09:22 → 09:15
- 09:23 → 09:30

Both rounded start time and rounded duration are stored in IndexedDB.

### 3.5 IndexedDB Data Storage

#### Location
- Stored in the browser's IndexedDB storage (user profile on the client machine)
- Data is tied to the browser profile and persists across sessions
- No server-side data storage required

#### Data Stores (Tables)

**time_entries**
| Field | Type | Description |
|---|---|---|
| `id` | auto-increment | Primary key |
| `date` | `YYYY-MM-DD` | Calendar date of the entry |
| `start_time` | `HH:mm:ss` | Rounded start time of the slot (rounded to nearest 15-min boundary); end time is calculated from the next entry's start time |
| `type` | string | Entry type: `Ticket`, `Break`, `Messages`, `Support`, `Meetings`, `ChangeMgmt`, `Reviews`, `Training`, `Other` (or a user-renamed label) |
| `ticket` | string | JIRA ticket number (only populated when `type = Ticket` or a configurable type with a linked ticket), otherwise empty |
| `description` | string | User-entered description (max 100 chars) |
| `duration` | `HH:mm` | Rounded-up duration (multiple of 15 minutes) |
| `day_accumulated_hhmm` | `HH:mm` | Running total of rounded minutes for the day (breaks excluded) |
| `day_accumulated_days` | decimal (2dp) | Running total expressed as fractional days (÷ 8h) |
| `day_target_hhmm` | `HH:mm` | Target hours configured for this day |
| `day_deviation_hhmm` | `±HH:mm` | `accumulated – target` |
| `day_deviation_days` | ±decimal (2dp) | Deviation expressed as fractional days (÷ 8h) |
| `submitted_to_jira` | boolean | Whether this entry has been submitted as a JIRA worklog |

**settings**
| Field | Type | Description |
|---|---|---|
| `key` | string | Setting key (e.g., `jiraBaseUrl`, `defaultTargetHours`) |
| `value` | any | Setting value |

**ticket_cache**
| Field | Type | Description |
|---|---|---|
| `ticket_key` | string | JIRA ticket key (e.g., `PROJ-123`) |
| `summary` | string | Ticket summary text |
| `fetched_at` | ISO timestamp | When the summary was fetched |
| `expires_at` | ISO timestamp | Cache expiry (7 days default) |

**connection_state**
| Field | Type | Description |
|---|---|---|
| `last_task` | object | The active task at time of browser close (type, ticket, description, start_time) |

> **Notes:**
> - `end_time` is not stored; it is calculated from the next entry's `start_time`. This enforces a continuous, gapless timeline — the user must report all time (using `Break` for breaks).
> - Entries with `Break` are excluded from accumulated time and are not submitted to JIRA.
> - Entries without JIRA tickets are excluded from JIRA worklog submission.
> - The `day_*` columns are recalculated and written fresh on every new entry, so data is always self-consistent.
> - **Previously used tickets** for the dropdown are derived from the `time_entries` store on startup, ordered by most recently used first.

#### CSV Export / Import

Since IndexedDB is not directly accessible as a file, users can:
- **Export to CSV** — Download all entries as a CSV file (Excel/LibreOffice compatible)
- **Import from CSV** — Load entries from a previously exported CSV file (useful for backup restore or migration)
- Export format matches the original CSV schema for compatibility

```
date,start_time,type,ticket,description,duration,day_accumulated_hhmm,day_accumulated_days,day_target_hhmm,day_deviation_hhmm,day_deviation_days,submitted_to_jira
```

### 3.6 Main Window

The main window shows a scrollable log of **all historical entries** across all days, with today's stats prominently displayed at the top. It stays open (possibly minimised) to keep the timer running.

- The entry table is **fully scrollable** and shows every entry from IndexedDB, newest first.
- A **date group header** separates each day, showing that day's target hours, accumulated time, and deviation.
- Today's date group is always visible at the top and auto-scrolled into view on startup.
- A **date filter / search bar** allows the user to jump to a specific date or filter by ticket/description.

```
┌───────────────────────────────────────────────────────────┐
│ JTRA – Time Tracker                 [Settings] [Submit]   │
├───────────────────────────────────────────────────────────┤
│ Today: 2026-03-26   Target: 08:00   Logged: 02:15         │
│                     Deviation: -05:45  (-0.72d)           │
├───────────────────────────────────────────────────────────┤
│ [Filter by date or ticket...                          ]   │
├───────────────────────────────────────────────────────────┤
│ ── 2026-03-26  Logged: 02:15  Target: 08:00  Dev: -05:45  │
│ Start    Type       Ticket    Description       Duration  │
│ 09:00    Ticket     PROJ-42   Fix login bug     00:45     │
│ 09:45    Break      –         –                 00:15     │
│ 10:00    Ticket     PROJ-55   Write JTRA spec   01:15 ←now│
│                                                           │
│ ── 2026-03-25  Logged: 08:00  Target: 08:00  Dev: +00:00  │
│ Start    Type       Ticket    Description       Duration  │
│ 08:00    Ticket     PROJ-42   Fix login bug     02:00     │
│ 10:00    Messages   –         Email triage      00:30     │
│ ...                                                       │
├───────────────────────────────────────────────────────────┤
│ Next check-in: 10:30  [Change time...                   ] │
└───────────────────────────────────────────────────────────┘
```

### 3.7 Check-In Popup (Browser Modal)

The check-in popup is displayed as a modal dialog in the browser when the server-side timer fires. A browser notification alerts the user, and clicking it focuses the browser tab with the modal.

```
┌──────────────────────────────────────────────────────────┐
│  What are you working on?                  10:15         │
├──────────────────────────────────────────────────────────┤
│  Type:    [▼ Ticket                                  ]   │
│           Ticket / Break / Messages / Support /          │
│           Meetings / ChangeMgmt / Reviews / Training /   │
│           Other                                          │
│                                                          │
│  Ticket:  [▼ PROJ-55 – Write JTRA spec               ]   │
│           (searchable, most recent first)                │
│           (hidden when type ≠ Ticket)                    │
│                                                          │
│  Description: [Write JTRA specification              ]   │
│               (max 100 chars, 42/100)                    │
│                                                          │
│  (break task: auto-confirming in 04:12...)               │
├──────────────────────────────────────────────────────────┤
│  Next popup: 10:30                                       │
│  [In 15 min] [In 30 min] [In 1h] [In 2h] [Custom time]   │
│                              [Confirm / Enter]           │
└──────────────────────────────────────────────────────────┘
```

### 3.8 JIRA Worklog Submission

- Accessible via the **Submit** button in the main window.
- The user selects a **date range** (defaults to today).
- The app groups consecutive entries that share **both the same JIRA ticket key AND the same description text** into a single worklog entry. If the same ticket appears with two different descriptions (e.g. two separate tasks on `PROJ-55`), they are submitted as **two separate worklog entries**.
  - Grouping rule: entries are grouped only when they are consecutive **and** both `ticket` and `description` match exactly.
  - Example: `PROJ-55 / "Write spec"` at 10:00 + `PROJ-55 / "Write spec"` at 10:15 → one worklog of 00:30.
  - Example: `PROJ-55 / "Write spec"` at 10:00 + `PROJ-55 / "Code review"` at 10:15 → two separate worklogs.
- A **preview table** is shown before submission. The user can confirm or cancel.
- On confirmation, for each grouped entry the app calls:
  ```
  POST /rest/api/2/issue/{issueKey}/worklog
  Authorization: Bearer <PAT>
  Content-Type: application/json

  {
    "timeSpentSeconds": 3600,
    "started": "2026-03-26T09:00:00.000+0100",
    "comment": "Write JTRA specification"
  }
  ```
  The `started` timestamp includes the local timezone offset (derived automatically from the OS — no user configuration needed).
- Results (success / error per entry) are shown in a results dialog after submission.
- Successfully submitted entries are marked `submitted_to_jira = true` in IndexedDB.
- `break` entries are always skipped.
- Already-submitted entries are shown in the preview but greyed out and excluded from re-submission by default (user can force re-submit with a checkbox).
- Only entries related to a JIRA ticket are eligible for JIRA worklog submission. Types with no ticket association (`Break`, and configurable types without a linked ticket) are always skipped.

```
┌───────────────────────────────────────────────────────────┐
│  Submit Worklogs to JIRA        Date: 2026-03-26          │
├───────────────────────────────────────────────────────────┤
│  Ticket    Description              Start    Duration     │
│  PROJ-42   Fix login bug            09:00    00:45        │
│  PROJ-55   Write JTRA spec          10:00    01:30 (×2)   │
│  PROJ-55   Code review PR #88       11:30    00:45        │
├───────────────────────────────────────────────────────────┤
│  Total: 03:00                   [Cancel]  [Submit]        │
└───────────────────────────────────────────────────────────┘
```

#### Worklog Submission Details

When submitting, each grouped entry is sent to JIRA:

```
POST /rest/api/2/issue/{issueKey}/worklog
Authorization: Bearer <PAT>
Content-Type: application/json

{
  "timeSpentSeconds": 3600,
  "started": "2026-03-26T09:00:00.000+0100",
  "comment": "Write JTRA specification"
}
```

- The `started` timestamp uses the rounded start time from the CSV, with the local timezone offset derived from the OS.
- The `timeSpentSeconds` is the total duration of the grouped entries in seconds.
- The `comment` is the shared description text.

After submission, a results dialog shows success/error status per worklog entry. Successfully submitted entries are marked `submitted_to_jira = true` in IndexedDB.

### 3.9 Settings Screen

| Setting | Type | Default |
|---|---|---|
| JIRA base URL | URL string | _(empty)_ |
| JIRA Personal Access Token | Password field | _(empty, stored in IndexedDB)_ |
| Default target hours per day | `HH:mm` input | `08:00` |
| Maximum snooze duration | Number (hours) | `4` |
| Auto-confirm break timeout | Number (minutes) | `10` |
| Ticket summary cache TTL | Number (days) | `7` |
| **Configurable types** | List with toggle + label edit per type | All enabled, default labels |

All settings are stored in the browser's IndexedDB (`settings` store). The JIRA PAT is never sent to the server except as a Bearer token during API proxy calls.

The configurable types section shows each of the 7 configurable types (`Messages`, `Support`, `Meetings`, `ChangeMgmt`, `Reviews`, `Training`, `Other`) as a row with:
- A toggle to **enable / disable** the type (disabled types are hidden from the check-in dropdown)
- An editable **label** field (e.g. rename `ChangeMgmt` to `Release Management`)
- An optional **linked ticket** field — a JIRA ticket number (`[A-Z]+-[0-9]+`) that is auto-filled whenever this type is selected. When set, the entry will be submitted to JIRA against that ticket. Leave blank to disable ticket association for this type.

Example settings row for Support:

```
[✓] Support   Label: [Support            ]   Linked ticket: [SUP-123  ]
```

Built-in types (`Ticket`, `Break`) are shown as read-only and cannot be disabled, renamed, or given a linked ticket.

---

## 4. Non-Functional Requirements

| Requirement | Detail |
|---|---|
| **Cross-platform** | Any modern browser (Chrome, Firefox, Edge, Safari) on Windows, macOS, Linux |
| **No installation required** | Users open URL in browser; PWA can be installed optionally for desktop integration |
| **Central deployment** | Single server instance serves all users; IT-managed deployment |
| **Browser notifications** | **Web Notifications API** — delivers system-level notifications (Windows Action Center, macOS Notification Center, Linux notifications); user must grant permission on first use. Works like Outlook web app. |
| **JIRA API / CORS** | HTTP calls made by server-side HttpClient — no browser CORS restrictions apply |
| **Offline operation** | PWA with cached UI; works fully offline for time tracking; JIRA connectivity only needed for ticket summary lookup and worklog submission |
| **Data locality** | All user data stored in browser's IndexedDB; never persisted on server |
| **Data portability** | CSV export for backup/migration; CSV import for restore |
| **PAT security** | PAT stored in IndexedDB (browser sandbox); never logged; sent only as Bearer token to JIRA API via server proxy |
| **Timer behaviour** | Timer runs on server (BackgroundService); reliable 15-minute intervals aligned to clock boundaries; pushed via SignalR |
| **Real-time updates** | SignalR connection keeps browser UI in sync with server timer state |
| **Tab visibility** | Browser tab should stay open for reliable notifications; background tabs may have delayed notifications depending on browser |
| **Missing entries** | Non-intrusive UI indicator when time gaps detected; user chooses whether to add entries |

---

## 5. JIRA API Integration

### Authentication
```
Authorization: Bearer <PAT>
```
PAT is a Personal Access Token generated by the user in their JIRA profile (supported in JIRA Data Center 8.14+ and JIRA Cloud).

### Ticket Summary Lookup (cached)
```
GET /rest/api/2/issue/{issueKey}?fields=summary
```
- Called once per new ticket key, result cached in `ticket-cache.json`
- Cache entries expire after 7 days (configurable)
- On cache miss or expiry, fetched fresh; on JIRA unavailability, stale cache is used

### Worklog Submission
```
POST /rest/api/2/issue/{issueKey}/worklog
Content-Type: application/json

{
  "timeSpentSeconds": 3600,
  "started": "2026-03-26T09:00:00.000+0000",
  "comment": "Write JTRA specification"
}
```

### JIRA Version Compatibility
- JIRA REST API v2 is used (supported on both JIRA Server/Data Center and JIRA Cloud)
- Base URL is fully configurable (e.g. `https://jira.yourcompany.com`)

---

## 6. Data Flow

```
[User opens browser URL]
      │
      ├── Blazor WebAssembly loads from server
      ├── SignalR connection established
      ├── IndexedDB data loaded (entries, settings, cached tickets)
      │
      ├── New calendar day? ──► Show Day Start dialog (set target hours)
      │
      └── Resume from last IndexedDB entry, wait for SignalR timer event
          Show indicator if there's a time gap since last entry

[15-min timer fires (server-side)]
      │
      ├── SignalR broadcasts "CheckInTime" to all connected clients
      │
      ▼
[Browser receives SignalR event]
      │
      ├── Show system notification (Web Notifications API → Windows/macOS/Linux notifications)
      │
      └── Display check-in modal in UI
            │
            ├── User confirms (Enter / same type+ticket)
            │         └──► Nothing to do — current task continues
            │
            ├── User changes type or ticket/description
            │         └──► Close current slot → write calculated fields to IndexedDB
            │               Open new slot → write new in-progress entry to IndexedDB
            │
            ├── User selects type "Break"
            │         └──► Same as type change, type = Break
            │               Auto-confirm after 10 min of inactivity
            │
            └── User snoozes / reschedules next popup
                      └──► Notify server via SignalR to reschedule timer

[User reschedules next popup from UI]
      │
      └──► SignalR call to server → TimerService reschedules to specified time

[User closes browser tab]
      │
      └──► Save current task to IndexedDB (beforeunload event)

[User reopens browser]
      │
      ├── SignalR reconnects
      ├── Load last task from IndexedDB
      │
      └──► Time gap detected? ──► Show indicator: "Last tracked: XX:XX. Add missing entries?"
                                User clicks to add entries, or dismisses

[User clicks Export]
      │
      └──► Read all entries from IndexedDB → Generate CSV → Download file

[User clicks Import]
      │
      └──► Parse uploaded CSV → Clear/merge entries → Write to IndexedDB

[User double-clicks a historical entry in main window]
      │
      └──► Open edit dialog for that row's editable fields
           On save → Recalculate all dependent fields for this and all subsequent entries
```

---

## 7. Project Structure (suggested)

```
jtra/
├── src/
│   ├── JtraServer/                      # .NET 8 Server Project
│   │   ├── Program.cs                   # App setup, Kestrel config, services
│   │   ├── Services/
│   │   │   ├── TimerService.cs          # BackgroundService for 15-min timer
│   │   │   └── JiraService.cs           # JIRA API calls (HttpClient)
│   │   ├── Hubs/
│   │   │   └── TimerHub.cs              # SignalR hub for real-time updates
│   │   ├── Controllers/
│   │   │   └── JiraController.cs        # JIRA proxy endpoints
│   │   ├── Models/
│   │   │   └── JiraModels.cs            # JIRA API DTOs
│   │   ├── wwwroot/                     # Blazor WebAssembly static files
│   │   │   ├── index.html
│   │   │   ├── css/
│   │   │   └── _framework/              # Blazor WASM runtime
│   │   └── JtraServer.csproj
│   │
│   ├── JtraClient/                      # Blazor WebAssembly Project
│   │   ├── Program.cs                   # Blazor WASM startup
│   │   ├── App.razor                    # Root component
│   │   ├── Pages/
│   │   │   ├── MainView.razor           # Daily log table, stats, timer countdown
│   │   │   ├── CheckInPopup.razor       # Check-in modal (triggered by SignalR)
│   │   │   ├── DayStartDialog.razor     # New-day target hours dialog
│   │   │   ├── EditEntryDialog.razor    # Edit historical entry dialog
│   │   │   ├── SubmitView.razor         # JIRA worklog submission preview
│   │   │   ├── ExportImport.razor       # CSV export/import UI
│   │   │   └── SettingsView.razor       # Settings screen
│   │   ├── Services/
│   │   │   ├── TimerHubClient.cs        # SignalR client connection
│   │   │   ├── NotificationService.cs   # Web Notifications API wrapper (system-level notifications)
│   │   │   ├── IndexedDbService.cs      # IndexedDB operations (time entries, settings, cache)
│   │   │   ├── CsvExportService.cs      # CSV export/import logic
│   │   │   └── AppState.cs              # Global state management
│   │   ├── Shared/
│   │   │   └── MainLayout.razor         # Layout component
│   │   └── JtraClient.csproj
│   │
│   └── JtraShared/                      # Shared models (optional)
│       ├── Models/
│       │   └── TimeEntry.cs
│       └── JtraShared.csproj
│
├── JtraServer.sln                       # Solution file
├── Dockerfile                           # Docker build for server deployment
└── SPECS.md
```

### Key Implementation Notes

**Server-side timer (TimerService.cs):**
- Runs as `BackgroundService` for reliable timer execution
- Aligned to clock boundaries (`hh:00/15/30/45`)
- Pushes notifications via SignalR to all connected clients

**SignalR Hub (TimerHub.cs):**
- Broadcasts `CheckInTime` event when timer fires
- Handles client subscription/unsubscription
- Syncs timer state on client reconnect

**Browser notifications (NotificationService.cs):**
- Requests permission on first use
- Shows system-level notification via Web Notifications API
- Notifications appear in Windows Action Center, macOS Notification Center, or Linux notifications (like Outlook web app)
- Focuses browser tab when notification is clicked

**IndexedDB storage (IndexedDbService.cs):**
- Stores all time entries, settings, and ticket cache locally
- Uses Blazor JS interop or a library like `Dexie.NET`
- Handles beforeunload event to save current task state

---

## 8. Implementation Decisions (Resolved)

| # | Question | Decision |
|---|---|---|
| 1 | Should configurable types with a linked ticket count toward the daily accumulated total? | **Yes** — if a type has a linked ticket it is treated like a `Ticket` entry for accumulation and JIRA submission purposes. Types without a linked ticket (`Break`, unlinked configurable types) are excluded. |
| 2 | What description is used when merging consecutive same-ticket + same-description slots for JIRA submission? | The shared description is used directly; no merge needed since grouping requires identical descriptions |
| 3 | Should the app support multiple users on one machine? | **One dataset per browser profile** — data lives in IndexedDB, isolated per browser profile |
| 4 | Should a system tray icon be shown? | **No** — the app runs as a web application in the browser; no native desktop integration needed. |
| 5 | Should the JIRA PAT be stored encrypted? | The PAT is stored in IndexedDB (browser sandbox). This provides isolation per browser profile. For additional security, encryption could be added in a future version if required. |
| 6 | How should entries for in-progress tasks be handled? | **Write immediately on start** with `date`, `start_time`, `type`, `ticket`, `description`, `day_target_hhmm`. Calculated fields (`duration`, `day_*`) are written when the next task starts. |
| 7 | What happens if browser is closed for several days? | A non-intrusive indicator shows the user there may be missing entries. Users can edit any historical entry (double-click row), and dependent calculated fields are recalculated automatically. |
| 8 | Should "working days" setting affect app behavior? | **Removed** — this configuration option is not needed. |
| 9 | Where does the "previously used tickets" list come from? | **Derived from IndexedDB entries** on startup, ordered by most recently used first. |
| 10 | When should `Break` entry be created? | Only when the user explicitly selects `Break` as a task type. No automatic Break entries are created. Users can manually add entries for any time gaps. |
| 11 | How to edit historical entries? | **Double-click row** in main window table opens an edit dialog. Only `date`, `start_time`, `type`, `ticket`, `description`, `day_target_hhmm` are editable. Dependent fields are recalculated on save. Delete and insert functionality planned for Phase 2. |
| 12 | What happens when user confirms same task in check-in popup? | **Nothing** — the current task continues, no IndexedDB update needed. The in-progress entry remains until the task changes. |
| 13 | Should start_time be actual or rounded? | **Rounded to nearest 15-min boundary** (e.g., 09:07 → 09:00, 09:08 → 09:15). |
| 14 | Should check-in interval be configurable? | **No** — fixed at 15 minutes. Removed from settings. |
| 15 | Central server or self-hosted? | **Central server** with IndexedDB storage in browser. Single deployment for IT, data stored locally per user. |
| 16 | CSV or IndexedDB for data storage? | **IndexedDB** with CSV export/import for portability. Data never stored on server. |
| 17 | What happens when browser tab is closed? | **Save current task to IndexedDB** — via beforeunload event. On next session, a non-intrusive indicator shows the user there may be missing entries. User decides whether to add them. |
| 18 | Should users see same data across devices/browsers? | **No** — each browser profile has independent data. This keeps architecture simple and data truly local. |
| 19 | Styling approach? | **Blazor default styling** — no additional CSS framework required. |
| 20 | Should notifications appear at system level? | **Yes** — Web Notifications API delivers to Windows Action Center, macOS Notification Center, Linux notifications, like Outlook web app. |

---

## 9. Out of Scope (v1)

- Reporting, charts, or dashboards
- Synchronisation between multiple machines
- Multiple JIRA instance support
- Offline JIRA worklog queue with retry on reconnect
- Time approval workflows
- Mobile support

---

## 10. Development Phases

### Phase 1 — Core Server + Timer + IndexedDB Storage
- .NET 8 project scaffold (server + Blazor WebAssembly client)
- Kestrel server for central deployment
- BackgroundService for clock-boundary timer (hh:00/15/30/45)
- SignalR hub for real-time timer notifications
- Browser notification integration (Web Notifications API)
- IndexedDB setup with time entries, settings, and ticket cache stores
- Day-start dialog with configurable target hours
- Main UI with daily log and stats
- Edit historical entries (double-click row)
- CSV export/import functionality

### Phase 2 — JIRA Integration
- Settings UI (JIRA URL, PAT storage in IndexedDB)
- Ticket summary fetch + local cache in IndexedDB
- Searchable ticket dropdown with summaries
- Worklog submission preview + submit
- Delete and insert entries (with recalculation)

### Phase 3 — Polish
- Time gap indicator in main UI
- Auto-confirm for break timeout with countdown
- Per-day target hours override
- PWA configuration (offline capability, installable)
- Optional: Docker deployment setup
