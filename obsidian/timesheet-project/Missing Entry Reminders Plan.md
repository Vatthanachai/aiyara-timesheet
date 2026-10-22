# Missing Entry Reminders Plan

> Drafted 22 October 2026 from item 3 of [[Product Backlog]] and a code audit of `develop` at `6c762d6`. Implemented the same day on `feature/missing-entry-reminders` (phases A and B). Covers the emails that remind members to log their time.

## Audit: What Exists On `develop`

| Capability | Where |
|---|---|
| Working weekdays, working hours, holidays, leave | Timesheet service (`work_weeks`, `holidays`, `leave_entries`); see [[Working Days Plan]] and [[Leave Types Plan]] |
| Who may still change a month | `TimesheetOperations.Editable`: the current month, or a returned one, until it is submitted, approved, or locked |
| The organization's time zone | Identity `tenants.TimeZoneId`, read over gRPC `GetOrganizationSettings` |
| A member's email | Identity gRPC `LookupProfile`, one member at a time; there is no call listing a tenant's members |
| Sending an email | Notification `internal/v1/*` behind `X-Internal-Key`; Timesheet already calls `month-returned` |
| Scheduled work | Quartz runs in the Report Worker only; Timesheet runs a `BackgroundService` for its outbox |

## Decisions

1. Reminders are off until a Tenant Admin turns them on, in Administration → Working time: **Daily reminder**, **Month-end reminder**, and the time they are sent (default 09:00, the organization's time zone). Settings live in a new Timesheet table `reminder_settings`, one row per organization; no row means off.
2. The daily reminder goes out at that time on each working day (a working weekday that is not a holiday). It is about the previous working day: members who logged no time and no leave on it get an email naming the day. It is sent only when that day is in the current month, since a member cannot change a past month unless it was returned; the month-end reminder covers the month as a whole.
3. The month-end reminder goes out at that time on the month's last working day. A member gets it when the hours logged plus leave (a whole day of leave is the normal working day) are short of what the month expects up to the day before: its working days so far times the normal working day. The email gives the hours logged, the hours expected, and up to ten working days with nothing logged.
4. Who is reminded: the organization's active Tenant Admins and Employees, from a new Identity gRPC call `ListMembers`. A member is not reminded about days before they joined (membership creation, in the organization's time zone), nor about a month that is submitted, approved, or locked.
5. Each email is recorded in a new `reminder_deliveries` table, unique per organization, member, kind, and day, before it is sent; a second worker, or a second run the same day, finds the row and sends nothing. Sending is best effort, like the month-returned email: a failure is logged without the address and not retried.
6. The Timesheet API runs the reminders in a `BackgroundService` once a minute. Each organization's settings row keeps the last day each reminder ran, so the work happens once a day.
7. Notification gains `internal/v1/timesheet-reminder` with an English template for each kind, like the other transactional emails.

## Phase A — API

- [x] A1. Identity: `ListMembers` in `identity_validation.v1.proto` and `IdentityValidationGrpcService`.
- [x] A2. Timesheet: `ReminderSettings` and `ReminderDelivery` entities, migration `AddReminders`; `GET reminders` and `PUT reminders` (Tenant Admin).
- [x] A3. Timesheet: `MissingEntryReminders` (who to remind, for a given time) and `ReminderWorker`; the member directory and the email sender behind interfaces.
- [x] A4. Notification: `timesheet-reminder` dispatcher, template, and endpoint.
- [x] A5. Tests: settings and their refusals; the daily reminder skips days off, holidays, leave, members who logged, joined later, or whose month is submitted; month-end shortfall and the days listed; a second run sends nothing; another tenant is untouched.

## Phase B — UI

- [x] B1. Administration → Working time: a Reminders panel with the two switches and the time.
- [x] B2. Playwright: the panel saves both switches and the time, at 375 and 320 px.
- [x] B3. Docs: [[Project Status]], [[Product Backlog]], [[Timesheet Project Index]].

## Out Of Scope

- Reminders to submit a month, or to approve one.
- A member turning reminders off for themselves.
- Reminders in Thai: the emails follow the other transactional emails, which are English.
- Push or in-app notifications.

## Related

- [[Product Backlog]]
- [[Working Days Plan]]
- [[Leave Types Plan]]
- [[Timesheet Approval Plan]]
- [[Project Status]]
