# Reporting Workspace Plan

> Drafted 12 October 2026 from a code review of `develop` at `8aa62e1` (reporting remote `src/Frontend/remotes/reporting/app/app.vue`, Report API, Report Worker). Implementation branch: `feature/reporting-workspace`.

## Implementation Progress

- **G0 fixed.** The reporting remote declares `runtimeConfig.public.shellOrigin`. Like the timesheet and identity remotes, it now checks `event.source === window.parent` and posts `aiyara.ready` once listening, so it no longer misses the Shell's first session message (which could otherwise take up to 60 s to resend). The administration remote had the same missing handshake; `fix/administration-shell-handshake` (12 October 2026) fixed it.
- **R1 done.** `ReportRunReasons` (Report.Services) formats the Worker's source-month message and parses it back for the API. `GET /runs` is paged/filterable with `statusReason`, `blockedMonth`, `completedAtUtc`, `signedVersion`, and `definitionId`. The dashboard adds `blocked` and excludes it from `queued`. Definitions and schedules list enabled rows only.
- **R2 done.** `GET /api/v1/identity/members` returns Tenant Admins their organization's members; members without a name sort last. Employees get 403.
- **R3 done.** Rewritten remote: summary, kind-aware period pickers with a period preview (defaulting to the last complete period), admin subject choice, history with names, reasons, signed badge, filters, paging, and 10 s auto-refresh, plus admin settings (rename/disable formats, time-zone select, monthly schedule note, retention). Downloads are named `<report> <period>.<ext>`. The page has no horizontal scroll at 320 px: the visually-hidden "Actions" header had escaped the scrolling table, so the table container is now its positioning context.
- Tests: `ReportRunListingTests` (Phase 4), `TenantMembersHttpTests` (Phase 1), and two Playwright flows that run the remote inside the Shell (Playwright now starts the reporting remote on port 3103).

## Current State

The Report API and Worker are complete: definitions, manual runs, schedules, retention, signed-PDF upload, download of the latest (signed first) version, tenant isolation, and an audit trail. The reporting remote exposes most of it in a single form-heavy page. The problems are in what users can see and do.

### Gaps

| # | Area | Finding |
|---|---|---|
| G0 | Session | The reporting remote is the only remote without `runtimeConfig.public.shellOrigin`, so `NUXT_PUBLIC_SHELL_ORIGIN` is never applied, the `aiyara.session` origin check never matches, and the page always shows "Please sign in" inside the Shell. Reporting has been unusable from the UI since it was added. |
| G1 | Run status | When a source month is not locked, the Worker keeps the run `Queued`, stores "Source month YYYY-MM has not been locked." in `FailureReason`, and retries every minute. The API does not return the reason, so the UI shows "Queued" indefinitely. Real failures also show no reason. |
| G2 | People | Runs show the first 8 characters of `subjectUserId`. No API lists a tenant's members, so an admin cannot request a report for a specific employee (the UI always sends `subjectUserId: null`). |
| G3 | Stale data | Definitions and schedules endpoints return disabled rows. Disabled definitions stay selectable, so requesting one fails with 404; disabled schedules stay listed with a working "Disable" button. |
| G4 | Periods | Users type arbitrary from/to dates for every kind. Weekly, monthly, and annual reports have natural periods (Monday weeks, calendar months, calendar years in the tenant time zone) that the scheduler already uses. |
| G5 | Progress | Queued and running reports never refresh; users must reload the page. |
| G6 | History | Only the latest 50 runs, with no filter by status or report, and no paging. No completion time and no marker for an uploaded signed version. |
| G7 | Admin settings | Definitions cannot be renamed or disabled in the UI. The schedule time zone is free text. Monthly schedules always fire at 00:15 local regardless of the chosen time, but the form still asks for one. |
| G8 | Downloads | Files are named `report-<run id>.<ext>`. |

## Decisions

- Employees keep requesting only their own reports (the existing server rule). Tenant Admins choose "Whole organization" or one member.
- The member directory exposes id, display name, email, role, and status to Tenant Admins only. Employees receive 403.
- A blocked or failed run shows a reason code, never raw exception text: `SOURCE_MONTH_NOT_LOCKED` (with the month) or `GENERATION_FAILED`.
- No database migration. Every field derives from existing columns.

## Delivery Plan

### R1 — Report API

- `GET /runs` returns `completedAtUtc`, `statusReason` (`SOURCE_MONTH_NOT_LOCKED` / `GENERATION_FAILED` / null), `blockedMonth` (`yyyy-MM` when blocked), `signedVersion` (latest signed upload or null), and `definitionId`.
- `GET /runs` accepts `status`, `definitionId`, `page`, and `pageSize`, and returns `{ totalCount, page, pageSize, items }`.
- `GET /definitions` and `GET /schedules` return enabled rows only.

### R2 — Member directory (Identity)

- `GET /api/v1/identity/members` (via Gateway) for an active Tenant Admin. It returns the tenant's members with id, first/last name, email, role, and status, ordered by name.

### R3 — Reporting remote

- **Summary row:** ready, in progress, needs attention (blocked or failed).
- **Request panel:** pick a report; the period control follows its kind (week / month / year pickers in the tenant time zone; free range only for Performance). Admins choose whole organization or a member. A preview line states the exact period.
- **History:** filter by status and report, with paging. Each row shows the person's name, period, status with its reason ("Waiting for 2026-09 to be locked"), completion time, a signed-version badge, download, and signed-PDF upload for admins. The list refreshes every 10 seconds while any run is queued or running.
- **Settings (admins):** definitions with rename and disable; schedules with a time-zone select, time shown only for weekly and annual, next and last run; retention reloads after save.
- **Downloads:** `<report name> <period>.<ext>`.

### Validation

- Report API and Identity unit/HTTP tests for the new fields, filters, enabled-only lists, and members authorization.
- Playwright coverage for the remote: period presets, admin subject choice, blocked reason, polling, filters, settings.
- `nuxt build` for the Shell and the reporting remote; the solution build and all .NET tests; live Compose smoke (phases 3–4 exercise reporting).

## Related

- [[Development Roadmap]]
- [[Project Status]]
- [[Security And Tenant Model]]
