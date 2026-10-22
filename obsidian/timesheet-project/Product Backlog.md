# Product Backlog

> Drafted 22 October 2026 from a review of [[Development Roadmap]], [[Project Status]], each plan's "Out Of Scope" list, and the code on `develop`. Phases 0–5 are complete; nothing below is approved or scheduled yet. Each item becomes its own plan note and Git Flow branch when it is picked up.

## Suggested Order

1. [[#1. Timesheet approval|Timesheet approval]] — `feature/timesheet-approval` (done)
2. [[#2. Leave types and balances|Leave types and balances]] — `feature/leave-types-balance` (done)
3. [[#3. Missing-entry reminders|Missing-entry reminders]] — `feature/missing-entry-reminders` (done)
4. [[#4. Copy and bulk entry|Copy and bulk entry]] — `feature/copy-and-bulk-entry` (done)
5. [[#5. Running timer|Running timer]] — `feature/running-timer` (done)
- Tooling, outside the numbered order: a separate test stack and a deploy step for the main stack, on `feature/test-compose` (see [[Test Stack Plan]]).
6. [[#6. Billable work and budgets|Billable work and budgets]] — `feature/billable-budgets` (done)
7. [[#7. MFA for organization members|MFA for organization members]] — next, in progress
- [[#11. Continuous integration|Continuous integration]] — deferred by the user until they have used the app themselves; not scheduled

## Workflow Gaps

### 1. Timesheet approval

> Done 22 October 2026 on `feature/timesheet-approval`: see [[Timesheet Approval Plan]]. Group leads as approvers are still open.

Today a month can only be locked (`POST months/{year}/{month}/lock`); there is no submit, approve, or return step, and the code has no approval concept.

- Month states: Open → Submitted → Approved, or Returned with a reason back to Open.
- Tenant Admins (and later group leads) approve or return a member's month from Reporting's member timesheets.
- Approval locks the month and takes the snapshot that reports and signed PDFs read; a returned month notifies the member.
- Audit every transition; the existing lock becomes the approval's last step.

### 2. Leave types and balances

> Done 22 October 2026 on `feature/leave-types-balance`: see [[Leave Types Plan]]. Leave requests approved by a Tenant Admin are still open.

`LeaveEntry` holds a date and a free-text `Kind` only.

- Organization-defined leave types (sick, personal, annual, …) managed in Administration.
- Yearly entitlement per type, the balance used and remaining, shown in Timesheet and Reporting.
- Half-day and hourly leave counted against the normal working day from [[Working Days Plan]].
- Optional leave requests approved by a Tenant Admin, reusing the approval flow above.

### 3. Missing-entry reminders

> Done 22 October 2026 on `feature/missing-entry-reminders`: see [[Missing Entry Reminders Plan]]. Reminders to submit or approve a month are still open.

Quartz, the Notification service, and the hours-expected ledger already exist.

- Email members who logged nothing on a past working day, and members whose month is short of what it expects near month end.
- Tenant Admins choose whether reminders run and when, in the organization's time zone.
- Skip holidays, non-working days, and leave.

## Faster Time Entry

### 4. Copy and bulk entry

> Done 22 October 2026 on `feature/copy-and-bulk-entry`: see [[Copy And Bulk Entry Plan]]. Still open: replacing a day's entries when copying, templates shared across the organization, and recurring entries created on a schedule.

Out of scope in [[Timesheet Page Plan]].

- Copy a previous day or week into the current one.
- Enter the same entry across several selected days.
- Saved templates for recurring work.

### 5. Running timer

> Done 22 October 2026 on `feature/running-timer`: see [[Running Timer Plan]]. Still open: choosing a project, category, or personal task for the timer, pausing, and stopping a forgotten timer automatically.

Out of scope in [[Timesheet Page Plan]] and [[Home Page Plan]].

- Start and stop a timer from Today and from Timesheet; stopping creates the entry, with the organization's break left out.
- One running timer per member, kept on the server so it survives a reload or another device.

## Projects And Cost

### 6. Billable work and budgets

> Done 22 October 2026 on `feature/billable-budgets`: see [[Billable Work And Budgets Plan]]. Still open: rate history, generated XLSX/CSV/PDF and scheduled by-project and by-client reports, a client entity, budgets per period or in money, and an over-budget warning for members.

Before this item `Project` and `Category` carried only names.

- Billable / non-billable flag and an hourly rate per project.
- An hours budget per project, with used against budget.
- Reports by project and client, alongside the existing per-member and per-group reports.

## Security And Administration

### 7. MFA for organization members

TOTP and recovery codes exist for Platform Admins only. Let Tenant Admins require MFA for their members, reusing the same enrollment and recovery flow. See [[Security And Tenant Model]].

### 8. Tenant-facing audit log

Out of scope in [[Tenant Member Management Plan]]. Member actions already land in `platform_organization_audit`; show the organization's own entries to its Tenant Admins.

### 9. Session management

A page listing the account's active sessions (device, last used) with a sign-out per session and "sign out everywhere", built on the existing refresh-session revocation.

### 10. Bulk invitations

Out of scope in [[Tenant Member Management Plan]]. Invite several members at once or import them from CSV, each with a group.

## Engineering And Operations

### 11. Continuous integration

The repository has no CI pipeline; the `develop` validation gate in `AGENTS.md` is run by hand. Add a pipeline that runs `git diff --check`, the .NET build and tests, frontend builds, and Playwright on every branch.

### 12. Backup and restore

Scripted backup and restore of the PostgreSQL databases and RustFS buckets, with a runbook section in `docs/developer-runbook.md`.

### 13. Broader end-to-end tests

Playwright now runs 85 tests against mocked APIs, covering sign-in, invitations and activation, time entry, leave, approval, reports and exports, and Administration settings. What is missing is browser flows against the real Compose stack; today only the PowerShell smoke scripts reach it.

### 14. Mobile and offline

An installable PWA that lets members record time without a connection and syncs when back online.

## Related

- [[Development Roadmap]]
- [[Project Status]]
- [[Timesheet Project Index]]
