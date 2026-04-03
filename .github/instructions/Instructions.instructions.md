---
description: Project coding instructions for JTRA, including architecture constraints and JIRA proxy rules.
applyTo: "**/*"
---

# JTRA Project Instructions

Use these instructions for code changes, reviews, and architecture decisions in this repository.

## Architecture Constraints

1. JTRA is a Blazor WebAssembly client with an ASP.NET Core server.
2. Browser storage is IndexedDB; server does not persist user time data.
3. Timer and synchronization behavior are server-driven (SignalR), with client fallback behavior.

## Critical Rule: JIRA Calls Must Go Through Server Proxy

1. All JIRA HTTP calls must be made by the server.
2. The browser client must never call JIRA URLs directly.
3. Client code must call server endpoints under `/api/jira/...` only.

Reason:
- Direct browser calls to JIRA are blocked by CORS in normal enterprise setups.
- Server-to-server calls are not restricted by browser CORS policy.

Implementation standard:
1. Client sends JIRA settings/credentials to server endpoint headers (`X-Jira-Base-Url`, `X-Jira-Pat`) as currently designed.
2. Server performs outbound JIRA request with `Authorization: Bearer <PAT>`.
3. Server returns normalized response to client.

## Relevance Triggers

Apply this file with high priority when a task involves any of the following:

1. Ticket summary lookup.
2. Worklog submission.
3. Changes in `JtraClient/Services/JiraTicketService.cs`.
4. Changes in `JtraServer/Controllers/JiraController.cs`.
5. Any mention of CORS errors, preflight requests, or cross-origin failures.
6. Any refactor that touches JIRA integration paths.

## Code Review Guardrails

When reviewing or generating changes:

1. Flag any direct `HttpClient` call from client code to external JIRA domains as a defect.
2. Prefer keeping JIRA parsing and protocol handling on the server boundary.
3. Preserve the existing proxy contract unless explicitly asked to change it.
4. If proxy contract changes, update both client and server in one change and verify build.

## Validation Expectations

After JIRA-related changes:

1. Build solution from `src/Jtra.sln`.
2. Verify client path calls `/api/jira/...` and not direct JIRA host URLs.
3. Verify server endpoint performs outbound JIRA call.
4. Confirm no new CORS-related browser errors are introduced.