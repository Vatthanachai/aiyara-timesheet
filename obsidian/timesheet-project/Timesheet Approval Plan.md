# Timesheet Approval Plan

> Drafted 22 October 2026 from item 1 of [[Product Backlog]] and a code audit of `develop` at `5cbcf21`. Branch `feature/timesheet-approval`. Covers how a member submits a month and how a Tenant Admin approves it or returns it.

## Audit: What Exists On `develop`

| Capability | Backend | UI |
|---|---|---|
| Closing a month | `POST months/{year}/{month}/lock` (Tenant Admin): one `month_locks` row per organization, an organization snapshot and one snapshot per member, `timesheet.month.locked.v1` for Reporting | No button; called by the smoke tests |
| Who may edit | `CanMutate`: the date is in the member's current month and the month is not locked | Timesheet `canEdit` mirrors it; past months read only |
| Member month review | `members/summary`, `members/{ownerId}/timesheet`, `me/timesheet` return `locked` | Reporting → Member timesheets, My timesheet |
| Email | Notification's internal `credential-email` and `report-ready`; Timesheet already calls Identity's `LookupProfile` over gRPC | — |

There is no per-member state for a month: a month is open for everyone until the organization locks it.

## Decisions

1. Approval is per member and month, in a new `month_approvals` table in the Timesheet service (`OwnerId`, `Year`, `Month`, `Status`, submission and decision times, `DecidedBy`, `ReturnReason`), unique per tenant, member, and month. No row means **Open**. Statuses: `submitted`, `approved`, `returned`.
2. Transitions:
   - A member submits their own month: Open or Returned → Submitted. Only for the current or a past month that is not locked.
   - A Tenant Admin approves: Submitted → Approved. A Tenant Admin returns with a reason (1–1000 characters): Submitted → Returned.
   - Anything else is `409 INVALID_TRANSITION`; a locked month is `409 MONTH_CLOSED`.
3. Who may edit: a month that is Submitted or Approved is read only for its member. A Returned month can be edited by its member even when it is a past month, until the organization locks it, so the member can fix what was returned. Otherwise the current-month rule stays.
4. Approval takes the member's month snapshot at once (same payload as the lock's), so reports and signed PDFs read the approved figures. Locking the month later keeps that snapshot and takes snapshots only for members who do not have one. Locking stays the organization's last step and does not require every member to be approved, so organizations that do not use approval work as before.
5. Every transition is audited as entity `month_approval` with the member as subject; a return records its reason.
6. A returned month emails the member (Notification `internal/v1/month-returned`, recipient from Identity `LookupProfile`). The email is best effort after the change is saved: a failed delivery does not undo the return, and the reason is always shown on the member's Timesheet page.
7. Reads: `GET me/months/{year}/{month}` returns the member's status, the return reason, `locked`, and whether the month can be edited, so the page does not repeat the rule. `me/timesheet` and `members/{ownerId}/timesheet` include `status` and `returnReason`; `members/summary` includes each member's `status`.

## Phase A — API

- [x] A1. `MonthApproval` entity, mapping, unique index, migration `AddMonthApprovals`.
- [x] A2. `TimesheetOperations`: `MonthStatus`, `SubmitMonth`, `ApproveMonth`, `ReturnMonth`; `CanMutate` follows decision 3; `LockMonth` skips existing member snapshots; status in the member views and summary.
- [x] A3. Controller routes with OpenAPI response types: `GET me/months/{year}/{month}`, `POST me/months/{year}/{month}/submit`, `POST members/{ownerId}/months/{year}/{month}/approve`, `POST members/{ownerId}/months/{year}/{month}/return`.
- [x] A4. Return email: Notification template and endpoint; Timesheet sender with `Notifications:BaseUrl` / `Notifications:InternalKey` in Compose and AppHost.
- [x] A5. Tests (`MonthApprovalTests`): each transition and its refusals, editing rules for submitted, approved, and returned months, the approval snapshot and the lock keeping it, employees cannot approve, another tenant sees nothing, the return notifier is called once. No test project covers Notification, so the email is checked against the Compose stack.

## Phase B — UI

- [ ] B1. Timesheet: the month's status under the heading, **Submit for approval** for an open or returned month, the return reason, and editing as the API says.
- [ ] B2. Reporting → Member timesheets: a status in each member row and on a member's month; **Approve** and **Return** (with a reason) for a submitted month. My timesheet shows the member's own status.
- [ ] B3. Playwright: a member submits, the admin returns with a reason, the member sees it, resubmits, and the admin approves.
- [ ] B4. Docs: [[Project Status]], [[Product Backlog]], [[Timesheet Project Index]].

## Out Of Scope

- Group leads as approvers (Tenant Admins only for now).
- A member withdrawing a submission; an admin can return it instead.
- Reopening an approved month before the lock (the snapshot is immutable).
- Reminders to submit or to approve (see [[Product Backlog]] item 3).

## Related

- [[Product Backlog]]
- [[Timesheet Page Plan]]
- [[Reporting Page Plan]]
- [[Security And Tenant Model]]
- [[Project Status]]
