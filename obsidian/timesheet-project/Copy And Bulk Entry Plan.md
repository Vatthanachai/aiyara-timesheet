# Copy And Bulk Entry Plan

> Drafted 22 October 2026 from item 4 of [[Product Backlog]] and a code audit of `develop` at `18cac4a`. Phase A on `feature/copy-and-bulk-entry`. Covers copying a previous day or week of entries, entering one entry on several days at once, and a member's saved entry templates.

## Audit: What Exists On `develop`

| Capability | Backend | UI |
|---|---|---|
| One entry | `POST/PUT/DELETE entries`: `SaveEntry` checks the month (`CanMutate`), the task name and text lengths, live project, category, and own personal task, then computes the duration with `TimesheetRules.WorkedMinutes` and the organization's break (`IncludesBreak` counts it as work); `INVALID_TIME_RANGE`, `ENTRY_IN_BREAK` | Timesheet entry form, one day at a time |
| Who may edit | `Editable`: the member's current month, or a month returned to them, until it is submitted, approved, or locked; otherwise `409 MONTH_CLOSED` | Timesheet `canEdit` from `me/months/{year}/{month}` |
| Overlap | None: entries on the same day may overlap; the single form does not check | — |
| Audit and outbox | `Audit` per entry change; `Changed` adds `timesheet.month.changed.v1` for the month | — |
| Days off | Work week (`WorkWeek.WorkingDaysMask`), holidays, and leave (whole or part day) | Calendar marks them |
| Reusable work | Personal tasks (name, status, project, category); no saved times or text | Task picker in the form |

Nothing copies entries, writes more than one entry per request, or remembers an entry's times and text.

## Decisions

1. **All or nothing.** Copy and bulk requests write every entry or none, in one serializable transaction. A request that touches a day the member may not change is refused whole with `409 MONTH_CLOSED`, and `errors.dates` lists those days, so the page can name them.
2. **Same rules as one entry.** Bulk and copied entries pass the checks of `SaveEntry`: the month (`Editable`), the task name and lengths, live references, and the duration from `TimesheetRules.WorkedMinutes` with today's break and the entry's `IncludesBreak`. No overlap check, because single entries have none.
3. **Bulk entry** (`POST entries/bulk`) writes the same entry on each of 1–31 distinct dates the member chose. It writes on exactly those dates, days off and holidays included, because the member picked them; the page preselects only working days. Days that already have entries get one more, as the single form would.
4. **Copy** (`POST entries/copy`) copies the member's entries from `days` consecutive days starting at `from` (1 for a day, 7 for a week) onto the same number of days starting at `to`, day by day. The source may be any month, even a locked one; only the target must be editable. The two ranges may not overlap.
5. A target day is **skipped**, not refused, and reported in `skipped` with a reason, when it is a holiday (`holiday`), not a working weekday (`day-off`), has whole-day leave (`leave`), or already has entries (`has-entries`). Skipping a day with entries makes copying twice harmless and never mixes two days. Part-day leave does not skip the day. A source day without entries is not reported.
6. A copied entry keeps the times, task name, detail, notes, personal task, and `IncludesBreak`, and its duration is computed again with the current break. A project, category, or personal task deleted since is left empty on the copy. An entry that would now lie wholly inside the break (duration 0) is not copied.
7. **Templates** are a member's own (`entry_templates`: name, task name, start and end, project, category, detail, notes, `IncludesBreak`), listed, created, renamed, changed, and deleted (soft) only by their owner. The name is 1–100 characters and unique per member, ignoring case (`409 TEMPLATE_EXISTS`). Contents follow the entry checks; the break check (`ENTRY_IN_BREAK`) waits until the template is used, since the break can change.
8. A template is only a prefill: the page fills the entry form or the bulk form from it and saves through `entries` or `entries/bulk`. There is no separate "apply" route. Listing a template leaves out a project or category deleted since.
9. Audit: each written entry is audited as `entry` `created`, a copy with the reason `copied from yyyy-MM-dd`, bulk with the request's `reason`; templates are audited as `entry_template`. One `timesheet.month.changed.v1` per month touched, not per entry.

## Phase A — API

- [x] A1. `EntryTemplate` entity, mapping, index on tenant and owner, migration `AddEntryTemplates`.
- [x] A2. `TimesheetOperations`: `BulkEntries`, `CopyEntries`, `EntryTemplates`, `SaveEntryTemplate`, `DeleteEntryTemplate`; entry checks shared with `SaveEntry`.
- [x] A3. Controller routes with OpenAPI response types: `POST entries/bulk`, `POST entries/copy`, `GET/POST entry-templates`, `PUT/DELETE entry-templates/{id}`.
- [x] A4. Tests (`CopyAndBulkEntryTests`): bulk writes every day or none, refuses a closed day and names it, keeps entry validation; copy of a day and a week, each skip reason, recomputed break, deleted references, refused overlapping ranges; templates' validation, duplicate names, owner and tenant isolation.
- [x] A5. Phase 3 smoke: a template, bulk entry on two working days, a week copied with a second copy skipped, and a copy into last month refused.

## Phase B — UI

- [ ] B1. Timesheet: **Copy previous day** and **Copy previous week**, with the skipped days and their reasons shown after.
- [ ] B2. Entry form: **Several days** picks dates (working days preselected) and saves through `entries/bulk`; a closed day is named from `errors.dates`.
- [ ] B3. Templates: pick one to fill the form, **Save as template**, and a list to rename, change, or delete them.
- [ ] B4. Playwright for copy, bulk, and templates at 375 and 320 px.
- [ ] B5. Docs: [[Project Status]], [[Product Backlog]], [[Timesheet Project Index]].

## Out Of Scope

- Replacing a target day's entries when copying (it is skipped instead).
- Copying leave, or copying another member's entries.
- Overlap checks between entries.
- Templates shared across the organization, or recurring entries created on a schedule.

## Related

- [[Product Backlog]]
- [[Timesheet Page Plan]]
- [[Timesheet Approval Plan]]
- [[Working Days Plan]]
- [[Project Status]]
