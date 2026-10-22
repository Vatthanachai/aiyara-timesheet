# Leave Types Plan

> Drafted 22 October 2026 from item 2 of [[Product Backlog]]. Implemented the same day on `feature/leave-types-balance` (phases A and B). Covers the organization's leave types, part-day leave, and each member's yearly balance.

## Decisions

1. A Tenant Admin defines leave types in Administration → Working time: a name (1–60 characters, unique without regard to case) and days a year (0–366 in half days), or no limit. Retiring a type keeps the leave already taken with it. Leave keeps the name it was saved with when a type is renamed.
2. With leave types, leave names one (`leaveTypeId`) and its kind is the type's name; without any, leave keeps the free-text kind (Annual, Sick, Other) as before, so organizations that do not use types work unchanged. A leave without a type, saved before the organization added types, is shown but cannot be changed, only removed.
3. Leave covers the whole day, or part of it in `minutes` (1 up to the normal working day from [[Working Days Plan]]). Timesheet offers Whole day, Half day (half the normal day), or Some hours.
4. A balance is per member, leave type, and calendar year: days used (a part day counts as its minutes over the normal working day), and days remaining when the type has a limit. Remaining may go below zero; the page shows it in red rather than refusing the leave.
5. Leave days in Timesheet's month line, Reporting's member month, the exports, and reports count part days the same way.

## Phase A — API

- [x] A1. `LeaveType` entity and `LeaveEntry.LeaveTypeId` / `Minutes`, migration `AddLeaveTypes`.
- [x] A2. Routes: `POST leave-types`, `PUT leave-types/{id}`, `DELETE leave-types/{id}` (Tenant Admin; `409 LEAVE_TYPE_EXISTS`), `GET me/leave-balance?year=`, `GET members/{ownerId}/leave-balance?year=` (Tenant Admin). The catalog lists `leaveTypes`; leave requests take `leaveTypeId` and `minutes`.
- [x] A3. Reports and the PDF renderer count part days.
- [x] A4. Tests (`LeaveTypeTests`, `ReportFoundationTests`) and the Phase 3 smoke: a type, an employee refused, leave without a type refused, a half day counted as 0.5.

## Phase B — UI

- [x] B1. Administration → Working time: a Leave types panel to add, rename, change days, and retire.
- [x] B2. Timesheet: the leave form chooses the type and how long; part days show their hours; the year's balance per type under the leave list.
- [x] B3. Reporting → Member timesheets: part-day leave, leave days counting part days, and the member's balance for the month's year.
- [x] B4. Playwright: leave types in Administration (add, duplicate name, change days, retire, at 375 and 320 px); Timesheet leave by type, half day and hours, balance and overdrawn.

## Out Of Scope

- Leave requests approved by a Tenant Admin before they count.
- Entitlements per member or carried over between years.
- Leave over several days in one entry.

## Related

- [[Product Backlog]]
- [[Working Days Plan]]
- [[Timesheet Approval Plan]]
- [[Project Status]]
