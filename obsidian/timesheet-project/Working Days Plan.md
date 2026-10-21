# Working Days Plan

> Drafted 21 October 2026 from a code audit of `develop` at `4536301` and implemented the same day on `feature/working-days` (phases A and B). Covers how an organization's Tenant Admin sets which weekdays the organization works.

## Implementation Progress

- **Phase A** (API): `work_weeks` table (migration `AddWorkWeeks`), `PUT /api/v1/work-week` (Gateway `/api/v1/timesheets/work-week`), `workingDays` in the catalog, in closed-month snapshots, and in the member month review.
- **Phase E** (break, `feature/work-breaks`): an optional break in the working day; entries overlapping it have it deducted unless marked "worked through the break"; report overtime counts against the working day less the break.
- **Phase D** (working hours, `feature/work-hours`): start and end of the working day on the work week; Administration → Working days and hours; new Timesheet entries start at them and the calendar shows them.
- **Phase C** (reports, `feature/report-days-off`): generated reports and the Reporting member month view show days off.
- **Phase B** (UI): Administration **Working days** panel above Holidays; the Timesheet calendar shades the organization's non-working weekdays. Both remotes fall back to Monday–Friday when a catalog has no `workingDays`.

## Audit: What Exists On `develop`

| Capability | Backend | UI |
|---|---|---|
| Holidays | `holidays` table in the Timesheet service, admin CRUD under `api/v1/holidays`, returned by `GET /catalog`, frozen into month snapshots | Administration **Holidays** panel; Timesheet calendar marks holiday cells |
| Weekend / working weekdays | None | `MonthCalendar.vue` hard-codes Saturday and Sunday as `weekend` |
| Member month review | `GET /members/{ownerId}/timesheet` returns entries, leave, and holidays | Reporting `MemberTimesheets.vue` lists holidays |

Nothing in the backend counts working days today, so the setting only changes what the calendar shows and what is recorded with a closed month. Organizations that work Saturday or Sunday–Thursday currently see the wrong days shaded.

## Decisions

1. The setting belongs to the Timesheet service, next to holidays: one `work_weeks` row per tenant with the working weekdays as a bit mask (bit `n` = `DayOfWeek` `n`). An organization without a row works Monday to Friday, so no data migration is needed.
2. The API speaks in weekday numbers `0`–`6` (`DayOfWeek`, Sunday = 0), matching JavaScript `Date.getDay()`. At least one day is required; duplicates and out-of-range values are `VALIDATION_FAILED`.
3. `GET /catalog` returns `workingDays` to every member, so the calendar needs no extra request. Only an active Tenant Admin may change it (`PUT /work-week`), audited as entity `work_week`.
4. Closing a month records the current `workingDays` in its snapshots, like holidays, so later changes do not rewrite a closed month. The member month review also returns `workingDays`.
5. The calendar marks every non-working weekday with the existing `weekend` style. Holidays keep their own marker.

## Phase A — API

- [x] A1. `WorkWeek` entity (`TenantId` unique, `WorkingDaysMask`), `TimesheetDbContext` mapping, and migration `AddWorkWeeks`.
- [x] A2. `WorkWeekRequest(int[] WorkingDays)`; `TimesheetOperations.SaveWorkWeek` (admin only, validation, upsert, audit) and `workingDays` in `Catalog`; `PUT api/v1/work-week` on `TimesheetController` with OpenAPI response types.
- [x] A3. `workingDays` in month-lock snapshots and in `MemberTimesheet`.
- [x] A4. Tests: default Monday–Friday, admin saves and reads back, employee gets 403, invalid bodies get 400, another tenant keeps its own setting, snapshot carries the setting. Phase 3 smoke saves a work week through Gateway.

## Phase B — UI

- [x] B1. Administration remote **Working days** panel: seven weekday toggles (Thai and English labels), Save, success and error messages; the last checked day cannot be cleared.
- [x] B2. Timesheet `MonthCalendar` takes `workingDays` from the catalog and shades the other days.
- [x] B3. Playwright: the admin saves a work week; the timesheet shades a newly non-working weekday. Narrow-width check of the panel.
- [x] B4. Docs: [[Project Status]], [[Timesheet Project Index]].

## Phase C — Days Off In Reports

Added 21 October 2026 on request: reports show the days that are not working days.

- [x] C1. `ReportDocumentRenderer.ReadDaysOff` turns a month snapshot's `holidays` and `workingDays` (Monday–Friday when absent, for months closed before work weeks) into rows: a holiday by name with the remark `Holiday`, any other non-working weekday as `Day off` with the remark `Weekly day off`. The worker reads them from one snapshot per month, since they are the organization's, and adds them to PDF and XLSX reports (shaded grey, no hours, no member) with a `Days off` total. Worked days, hours, and overtime still count work rows only.
- [x] C2. Reporting → Member timesheets: a `Days off` total, a Days off list (holidays and weekly days off) in place of Organization holidays, and a remark under the date of entries that fall on a day off.
- [x] C3. Tests: renderer (Sunday–Thursday week with a holiday, the Monday–Friday fallback, document text), the organization report contains day-off rows, Playwright for the member view. Docs: [[Project Status]].

## Phase D — Working Hours

Added 21 October 2026 on request: a Tenant Admin sets when the working day starts and ends. Branch `feature/work-hours`.

Decisions: the hours live on the same `work_weeks` row (`StartTime`, `EndTime`; no row means 09:00–17:00, the times new entries used to start with). The start must be before the end, so shifts that cross midnight are not supported. `PUT /work-week` takes optional `startTime` / `endTime`, and an omitted pair keeps the current hours, so existing callers keep working. The catalog returns `workHours` to every member; closed-month snapshots and the member month views include them. Overtime in reports stays "more than 8 hours worked in a day", because the hours include breaks.

- [x] D1. `WorkWeek.StartTime` / `EndTime` (migration `AddWorkHours`), validation (`VALIDATION_FAILED` for start ≥ end or seconds), audit, `workHours` in the catalog, snapshots, `members/{ownerId}/timesheet`, and `me/timesheet`.
- [x] D2. Administration: the panel becomes **Working days and hours**, with start and end chosen in 15-minute steps and shown in the organization's time format.
- [x] D3. Timesheet: a new entry starts at the organization's hours, and the calendar shows them.
- [x] D4. Tests (API, Playwright, phase 3 smoke) and docs.

## Phase E — Break Time

Added 21 October 2026 on request: a break inside the working day (for example lunch 11:30–12:30) that does not count as work, unless a member says they worked through it. Branch `feature/work-breaks`, built on `feature/work-hours`.

Decisions:
1. The work week gets an optional break (`BreakStart`, `BreakEnd`); none is the default, so nothing changes until an admin sets one. A break lies within the working hours and ends after it starts. It is saved together with the hours: a `PUT /work-week` that sends `startTime` / `endTime` also sets the break (both break times, or neither for no break), and one that leaves the hours out keeps the hours and the break.
2. An entry that overlaps the break has the overlap deducted from its duration (08:30–17:30 with 11:30–12:30 is 8 hours), including both halves of an overnight entry. A new `includesBreak` flag on the entry counts the break as work, for a meeting over lunch or work that ran into it. An entry entirely inside the break without the flag would be worth nothing, so it is refused (`400 ENTRY_IN_BREAK`).
3. Durations are worked out when an entry is saved, so a later change to the break does not rewrite existing entries; closed months are already frozen.
4. The standard working day is the working hours minus the break. Generated reports count overtime days against it, read from the month's snapshot, instead of a fixed 8 hours. Months closed before working hours existed keep 8 hours.

- [x] E1. API: break on the work week and `includesBreak` on entries (migration `AddWorkBreaks`), duration rules, validation, `workHours.breakStart` / `breakEnd`, the flag in snapshots and member views.
- [x] E2. Administration: break start and end (quarter hours, optional) in Working days and hours.
- [x] E3. Timesheet: the entry form's duration preview deducts the break, and a "worked through the break" checkbox appears when an entry overlaps it (new and edited entries); the calendar shows the break.
- [x] E4. Reports: overtime against the standard working day.
- [x] E5. Tests and docs.

## Out Of Scope

- Shifts, breaks, and expected-hours or utilization reports.
- Per-member or per-group schedules.
- Blocking time entries on non-working days (members may still log weekend work).

## Related

- [[Tenant Member Management Plan]]
- [[Reporting Workspace Plan]]
- [[Project Status]]
