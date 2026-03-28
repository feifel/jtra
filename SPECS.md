# JTRA – JIRA Time Recording Application: Specification

## 1. Overview

JTRA is a cross-platform desktop application that reminds the user every 15 minutes to confirm or change their current work task, records time against JIRA tickets, stores data locally in a CSV file, and — on request — submits recorded time to JIRA as worklogs via the JIRA REST API.

---

## 2. Technology Stack

### Self-Hosted Blazor WebAssembly + .NET 8 Server

| Layer | Technology | Rationale |
|---|---|---|
| Application | **Self-hosted .NET 8 app** (Kestrel) | Single executable, no installation required, cross-platform |
| UI / Frontend | **Blazor WebAssembly** | Runs in browser, rich interactivity, shared C# code with server |
| Language | **C# / .NET 8** | Unified language across frontend and backend |
| Styling | **Tailwind CSS** | Utility-first, fast to prototype |
| Local config storage | **JSON file** on server | Persistent settings stored alongside app |
| Time data | **CSV file** on server | Portable, human-readable, openable in Excel / LibreOffice |
| Secrets (JIRA PAT) | **JSON config file** (user-restricted permissions) | Kept out of plain config; never committed or logged |
| Background timer | **Server-side BackgroundService** | Reliable timer aligned to clock boundaries (`hh:00/15/30/45`) |
| Real-time communication | **SignalR** | Server pushes timer events to connected browsers |
| Notifications | **Web Notifications API** | Browser-native notifications when timer fires |
| JIRA API | **HttpClient on server** | No CORS restrictions; PAT sent as Bearer token |
| Auto-open browser | **Process.Start** on app launch | User experience similar to desktop app |
| Packaging | **Single-file self-contained publish** | One `.exe` (Windows) or binary (macOS/Linux), ~25-30 MB |

### Architecture Overview

```
┌─────────────────────────────────────────────────────────────────┐
│                        User Machine                              │
│                                                                  │
│   ┌─────────────────────────────────────────────────────────┐   │
│   │  JtraServer (.NET 8)                                     │   │
│   │                                                          │   │
│   │   ┌─────────────┐    ┌─────────────┐    ┌────────────┐  │   │
│   │   │ Static Files│    │  REST API   │    │   Timer    │  │   │
│   │   │ (Blazor UI) │    │  (JIRA proxy│    │  Service   │  │   │
│   │   │             │    │   + data)   │    │            │  │   │
│   │   └─────────────┘    └─────────────┘    └────────────┘  │   │
│   │                          │                    │          │   │
│   │                    SignalR Hub                │          │   │
│   │                          │                    │          │   │
│   │                   Kestrel (port 5000)         │          │   │
│   └──────────────────────────┼────────────────────┼──────────┘   │
│                              │                    │              │
│   ┌──────────────────────────┼────────────────────┼──────────┐   │
│   │  Browser                 │                    │          │   │
│   │   ┌────────────────────┐ │              ┌─────┴─────┐    │   │
│   │   │ http://localhost   │◄┼──────────────│ CSV File  │    │   │
│   │   │ :5000              │ │              │ Settings  │    │   │
│   │   └────────────────────┘ │              └───────────┘    │   │
│   └──────────────────────────┼───────────────────────────────┘   │
│                              │                                   │
└──────────────────────────────┼───────────────────────────────────┘
                               │
                               ▼
                          JIRA Server
```

### Why Self-Hosted Blazor Instead of Tauri?

| Requirement | Self-Hosted Blazor | Tauri |
|---|---|---|
| No installation required | ✅ Run single .exe | ❌ Requires installer |
| Can be locally installed | ✅ Optional (shortcut/startup) | ✅ Required |
| Always-on-top popup | ❌ Browser limitation | ✅ Native window |
| Browser notifications | ✅ Works everywhere | ✅ Native notifications |
| Bundle size | ~25-30 MB | ~5-8 MB |
| Timer reliability | ✅ Server-side | ✅ Native |
| JIRA CORS bypass | ✅ Server proxy | ✅ Native HTTP |
| Linux support | ✅ Full | ✅ Full |
| macOS support | ✅ Full | ✅ Full |
| Development complexity | Lower (C# only) | Higher (Rust + TypeScript) |

### Cross-Platform Publishing

| Platform | RID | Architecture | Output | Size |
|---|---|---|---|---|
| Windows | `win-x64` | x64 | `JtraServer.exe` | ~25-30 MB |
| Windows ARM | `win-arm64` | ARM64 | `JtraServer.exe` | ~25-30 MB |
| macOS Intel | `osx-x64` | x64 | `JtraServer` | ~25-30 MB |
| macOS Apple Silicon | `osx-arm64` | ARM64 | `JtraServer` | ~25-30 MB |
| Linux | `linux-x64` | x64 | `JtraServer` | ~25-30 MB |

Publish command:
```bash
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

### Code Signing (for distribution)
| Platform | Requirement | Notes |
|---|---|---|
| **Windows** | Optional for internal use | Unsigned apps show a one-time SmartScreen "Run anyway" prompt when downloaded from the internet. Not shown when installed from a network share. |
| **macOS** | Ad-hoc signing minimum | Free, no Apple Developer account needed. Users get a one-time Privacy & Security prompt. Apple Silicon Macs require at least ad-hoc signing. |
| **Linux** | Not required | No signing needed. |

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
- **App close behavior**: When the app is closed and the current task is not already a `Break`, a `Break` entry is automatically created to mark the end of the work session. This ensures all time is accounted for.

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

On app restart, the app reads the last CSV entry to determine what was being tracked (using `start_time` and calculating duration from the next entry). The timer then schedules the next firing at the next clock boundary (`hh:00/15/30/45`). If the next boundary has already passed (i.e. the app was closed during a slot), a check-in popup is shown immediately on startup. If a custom next popup time was set before the app was closed, that time is restored from persisted state (unless it has already passed, in which case a popup is shown immediately).

### 3.2 Task Entry

A task entry consists of:

| Field | Rules |
|---|---|
| **Type** | Selected from the type dropdown (see below). Determines whether a JIRA ticket field is required and whether the entry is eligible for worklog submission. |
| **JIRA Ticket** | Required only when type is `Ticket`. Auto-filled (and read-only) when the selected configurable type has a linked ticket configured. Hidden/disabled for types with no ticket association. Must match `[A-Z]+-[0-9]+` (e.g. `PROJ-123`). |
| **Description** | Free text, max 100 characters. Pre-filled with the previous description for the same ticket/type combination. |

#### CSV Row Lifecycle

When a task is started, a CSV row is written immediately with the following columns populated:
- `date`, `start_time`, `type`, `ticket`, `description`, `day_target_hhmm`

The remaining calculated columns (`duration`, `day_accumulated_hhmm`, `day_accumulated_days`, `day_deviation_hhmm`, `day_deviation_days`) are computed and written when the user starts the next task (i.e., when the current slot ends).

#### Editing Historical Entries

Users can edit historical entries at any time by **double-clicking** a row in the main window table. Only the editable columns can be modified: `date`, `start_time`, `type`, `ticket`, `description`, `day_target_hhmm`. When an entry is edited, all dependent calculated columns for that row and all subsequent rows are recalculated (similar to spreadsheet behavior).

> **Future enhancement (Phase 2):** Allow users to **delete** and **insert** entries, with automatic recalculation of all subsequent rows.

#### Type Dropdown

The type is always selected first. It determines what fields are shown below it.

**Built-in types (always present, not removable):**

| Value | Description | JIRA ticket required | Submitted to JIRA |
|---|---|---|---|
| `Ticket` | Work on a specific JIRA ticket — the primary use case | Yes | Yes |
| `Break` | A break period (also created automatically when app is closed) | No | No |

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
- The entry is written to the CSV with that ticket number in the `ticket` column.
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
- Ticket summaries are fetched from JIRA once per ticket and **cached locally** in `ticket-cache.json` (invalidated after 7 days, configurable).

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

Both rounded start time and rounded duration are stored in the CSV.

### 3.5 CSV Data File

#### Location
- Default: OS user data directory
  - Windows: `%APPDATA%\JTRA\timelog.csv`
  - macOS: `~/Library/Application Support/JTRA/timelog.csv`
  - Linux: `~/.config/jtra/timelog.csv`
- **Configurable** via Settings: the user can point to a custom path (shared drive, OneDrive folder, etc.).

#### Schema

```
date,start_time,type,ticket,description,duration,day_accumulated_hhmm,day_accumulated_days,day_target_hhmm,day_deviation_hhmm,day_deviation_days,submitted_to_jira
```

| Column | Format | Description |
|---|---|---|
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
| `submitted_to_jira` | `true\|false` | Whether this entry has been submitted as a JIRA worklog |

> **Notes:**
> - `end_time` is not stored; it is calculated from the next entry's `start_time`. This enforces a continuous, gapless timeline — the user must report all time (using `Break` for breaks or by closing the app which auto-creates a `Break` entry).
> - Entries with `Break` are excluded from accumulated time and are not submitted to JIRA.
> - Entries without JIRA tickets are excluded from JIRA worklog submission.
> - The `day_*` columns are recalculated and written fresh on every new entry, so the CSV is always self-consistent.
> - The `ticket` column is empty for non-`Ticket` type entries **unless** the configurable type has a linked ticket configured (see section 3.9), in which case the linked ticket number is written automatically.
> - **Previously used tickets** for the dropdown are derived from the CSV file on startup, ordered by most recently used first.

### 3.6 Main Window

The main window shows a scrollable log of **all historical entries** across all days, with today's stats prominently displayed at the top. It stays open (possibly minimised) to keep the timer running.

- The entry table is **fully scrollable** and shows every entry from the CSV file, newest first.
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
- Successfully submitted entries are marked `submitted_to_jira = true` in the CSV.
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

After submission, a results dialog shows success/error status per worklog entry. Successfully submitted entries are marked `submitted_to_jira = true` in the CSV.

### 3.9 Settings Screen

| Setting | Type | Default |
|---|---|---|
| JIRA base URL | URL string | _(empty)_ |
| JIRA Personal Access Token | Password field | _(empty, stored separately from config)_ |
| Default target hours per day | `HH:mm` input | `08:00` |
| CSV file path | File path picker | OS user data dir |
| Maximum snooze duration | Number (hours) | `4` |
| Auto-confirm break timeout | Number (minutes) | `10` |
| Ticket summary cache TTL | Number (days) | `7` |
| **Configurable types** | List with toggle + label edit per type | All enabled, default labels |

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
| **Cross-platform** | Windows 10+, macOS 12+ (Intel + Apple Silicon), Linux (Ubuntu 22+) |
| **No installation required** | Single executable, double-click to run; optional local installation (shortcut/startup) for convenience |
| **No runtime for end users** | Self-contained single-file publish; no .NET runtime required on user machine |
| **Browser notifications** | Web Notifications API for check-in alerts; user must grant permission on first use |
| **JIRA API / CORS** | HTTP calls made by server-side HttpClient — no browser CORS restrictions apply |
| **Offline operation** | App works fully offline; JIRA connectivity only needed for ticket summary lookup and worklog submission |
| **Data portability** | CSV is always readable in Excel / LibreOffice Calc; can be copied/shared freely |
| **PAT security** | PAT stored in a JSON config file with restricted file permissions; never logged or included in CSV |
| **Timer behaviour** | Timer runs on server (BackgroundService); reliable 15-minute intervals aligned to clock boundaries |
| **Real-time updates** | SignalR connection keeps browser UI in sync with server timer state |
| **Auto-open browser** | App automatically opens default browser to `http://localhost:5000` on startup |
| **Port configuration** | Default port 5000; configurable via command line argument or settings file |
| **Single instance** | Only one instance of JTRA may run at a time (enforced via OS-level mutex/file lock) |

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
[User starts JtraServer.exe]
      │
      ├── Server starts Kestrel on port 5000
      ├── Browser auto-opens http://localhost:5000
      ├── TimerService (BackgroundService) starts
      │
      ├── New calendar day? ──► Show Day Start dialog (set target hours)
      │
      └── Resume from last CSV entry, schedule next firing at next hh:00/15/30/45 boundary

[15-min timer fires (server-side)]
      │
      ├── SignalR broadcasts "CheckInTime" to all connected clients
      │
      ▼
[Browser receives SignalR event]
      │
      ├── Show browser notification (Web Notifications API)
      │
      └── Display check-in modal in UI
            │
            ├── User confirms (Enter / same type+ticket)
            │         └──► Nothing to do — current task continues
            │
            ├── User changes type or ticket/description
            │         └──► Close current slot → write calculated columns to CSV row
            │               Open new slot → write new in-progress CSV row
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

[User closes browser tab but server still running]
      │
      └──► Timer continues on server; next fire will send SignalR (no client connected)
            User can re-open http://localhost:5000 anytime

[User closes JtraServer]
      │
      └──► If current task is not Break → Create Break entry starting now
            Save timer state to persistent storage

[User double-clicks a historical entry in main window]
      │
      └──► Open edit dialog for that row's editable columns
           On save → API call → Recalculate all dependent columns for this and all subsequent rows
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
│   │   │   ├── JiraService.cs           # JIRA API calls (HttpClient)
│   │   │   ├── CsvService.cs            # CSV read/write operations
│   │   │   ├── SettingsService.cs       # Settings JSON management
│   │   │   └── TicketCacheService.cs    # Ticket summary cache
│   │   ├── Hubs/
│   │   │   └── TimerHub.cs              # SignalR hub for real-time updates
│   │   ├── Controllers/
│   │   │   ├── JiraController.cs        # JIRA proxy endpoints
│   │   │   ├── SettingsController.cs    # Settings CRUD
│   │   │   └── EntriesController.cs     # Time entries CRUD
│   │   ├── Models/
│   │   │   ├── TimeEntry.cs             # CSV row model
│   │   │   ├── Settings.cs              # App settings model
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
│   │   │   └── SettingsView.razor       # Settings screen
│   │   ├── Services/
│   │   │   ├── TimerHubClient.cs        # SignalR client connection
│   │   │   ├── NotificationService.cs   # Web Notifications API wrapper
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
├── publish.bat                          # Build script for all platforms
└── SPECS.md
```

### Key Implementation Notes

**Server-side timer (TimerService.cs):**
- Runs as `BackgroundService` for reliable timer execution
- Aligned to clock boundaries (`hh:00/15/30/45`)
- Pushes notifications via SignalR to connected clients

**SignalR Hub (TimerHub.cs):**
- Broadcasts `CheckInTime` event when timer fires
- Handles client subscription/unsubscription
- Syncs timer state on client reconnect

**Browser notifications (NotificationService.cs):**
- Requests permission on first use
- Shows notification with "Open JTRA" action
- Focuses browser tab when notification is clicked

---

## 8. Implementation Decisions (Resolved)

| # | Question | Decision |
|---|---|---|
| 1 | Should configurable types with a linked ticket count toward the daily accumulated total? | **Yes** — if a type has a linked ticket it is treated like a `Ticket` entry for accumulation and JIRA submission purposes. Types without a linked ticket (`Break`, unlinked configurable types) are excluded. |
| 2 | What description is used when merging consecutive same-ticket + same-description slots for JIRA submission? | The shared description is used directly; no merge needed since grouping requires identical descriptions |
| 3 | Should the app support multiple users on one machine? | **One dataset per OS user** — data lives in OS user profile directories |
| 4 | Should a system tray icon be shown? | **No** — the app runs as a standalone server with browser UI; user interacts via browser. System tray is not needed. |
| 5 | Should the JIRA PAT be stored encrypted? | The PAT is stored in a JSON config file with OS-level file permissions restricted to the current user. For additional security, the file path should be in a protected directory (user profile). Encryption could be added in a future version if required. |
| 6 | How should CSV rows for in-progress tasks be handled? | **Write immediately on start** with `date`, `start_time`, `type`, `ticket`, `description`, `day_target_hhmm`. Calculated columns (`duration`, `day_*`) are written when the next task starts. |
| 7 | What happens if app is closed for several days? | A `Break` entry is created when app closes. Users can edit any historical entry (double-click row), and dependent calculated columns are recalculated automatically. |
| 8 | Should "working days" setting affect app behavior? | **Removed** — this configuration option is not needed. |
| 9 | Where does the "previously used tickets" list come from? | **Derived from CSV** on startup, ordered by most recently used first. |
| 10 | When should `Break` entry be created on app close? | When the app is closed and the current task is not already a `Break`, a `Break` entry is automatically created. The `End` type has been removed. |
| 11 | How to edit historical entries? | **Double-click row** in main window table opens an edit dialog. Only `date`, `start_time`, `type`, `ticket`, `description`, `day_target_hhmm` are editable. Dependent columns are recalculated on save. Delete and insert functionality planned for Phase 2. |
| 12 | What happens when user confirms same task in check-in popup? | **Nothing** — the current task continues, no CSV update needed. The in-progress row remains until the task changes. |
| 13 | Should start_time be actual or rounded? | **Rounded to nearest 15-min boundary** (e.g., 09:07 → 09:00, 09:08 → 09:15). |
| 14 | Should check-in interval be configurable? | **No** — fixed at 15 minutes. Removed from settings. |

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

### Phase 1 — Core Server + Timer + Local Data
- .NET 8 project scaffold (server + Blazor WebAssembly client)
- Kestrel server with auto-open browser on start
- BackgroundService for clock-boundary timer (hh:00/15/30/45)
- SignalR hub for real-time timer notifications
- Browser notification integration (Web Notifications API)
- Day-start dialog with configurable target hours
- CSV read/write (all columns)
- Main UI with daily log and stats
- Edit historical entries (double-click row)

### Phase 2 — JIRA Integration
- Settings UI (JIRA URL, PAT storage)
- Ticket summary fetch + local cache
- Searchable ticket dropdown with summaries
- Worklog submission preview + submit
- Delete and insert entries (with recalculation)

### Phase 3 — Polish & Distribution
- Auto-confirm for break timeout with countdown
- Per-day target hours override
- Single-instance enforcement (mutex/file lock)
- Ad-hoc macOS signing configuration
- Cross-platform publishing scripts (win/osx/linux)
- Optional: Auto-updater mechanism
