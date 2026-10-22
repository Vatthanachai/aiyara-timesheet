# Running Timer Plan

> Drafted 22 October 2026 from item 5 of [[Product Backlog]]. Implemented 22 October 2026 on `feature/running-timer`: Phase A (API) and Phase B (UI). A member starts a timer on Today or Timesheet and stops it into a time entry, with the organization's break left out.

## Audit: What Exists On `develop`

| Capability | Backend | UI |
|---|---|---|
| Entry checks | `CheckEntryAsync`: task name, lengths, live references, own personal task, `INVALID_TIME_RANGE`, `ENTRY_IN_BREAK`; duration from `TimesheetRules.WorkedMinutes` less the break unless `IncludesBreak` | Entry form |
| Who may edit | `CanMutate`/`Editable`: current month or a returned month until submitted, approved, or locked; else `409 MONTH_CLOSED` | `canEdit` on Timesheet |
| Overnight | An entry whose end is before its start runs past midnight, up to 24 hours | Entry form |
| Timer | None | None |

## Decisions

1. **One table, `running_timers`** (migration `AddRunningTimers`): owner, start (UTC), and optional prefill (task name, detail, notes, project, category, personal task, `IncludesBreak`). A unique index on tenant and owner allows one timer per member. Stopping or discarding deletes the row.
2. **Routes** under `api/v1/timer`: `GET` the current timer, `POST start`, `POST stop`, `DELETE` to discard.
3. **Start** needs nothing; a prefill is checked like an entry's text and references. A second start is `409 TIMER_RUNNING`; two starts at once are decided by the unique index, handled like `TEMPLATE_EXISTS`.
4. **Stop** builds an entry from the timer and runs the shared `CheckEntryAsync` and `CanMutate`, so the rules are the entry's: month open, task name present, break left out (unless `includesBreak`), `ENTRY_IN_BREAK`, `INVALID_TIME_RANGE`. Fields left out of the stop request fall back to the timer's prefill. The entry is audited as created with the reason `stopped timer`, and one month-changed event is raised.
5. **Times** are in the member's organization time zone, to the minute (seconds dropped): the entry's day and start are the timer's start, its end is now. A timer under a minute long is `400 INVALID_TIME_RANGE`.
6. **Midnight:** a timer that crosses midnight becomes an overnight entry on its start day (end before start), as the entry form already allows.
7. **Refusals keep the timer.** `409 MONTH_CLOSED` (with `errors.dates`) when the start day's month is closed, which happens when a timer runs across a month end; `409 TIMER_TOO_LONG` at 24 hours or more, since an entry is at most 24 hours; and every `400` entry check. Nothing is deleted on a refusal. The stop request can send `date`, `startTime`, and `endTime` to place the entry on an open day or fix a forgotten timer; otherwise the member discards it.
8. The server's clock is read through `TimeProvider`, so tests fix it. `GET` and start return `nowUtc` so the page can count from the server's time rather than the device's.
9. Reminders and month summaries ignore a running timer until it is stopped.

## Phase A: API

- [x] A1. `RunningTimer` entity, unique index, migration `AddRunningTimers`.
- [x] A2. `TimesheetOperations`: `Timer`, `StartTimer`, `StopTimer`, `DiscardTimer`; `TimeProvider` injected.
- [x] A3. Controller routes with OpenAPI response types.
- [x] A4. Tests (`RunningTimerTests`): break left out and kept with `includesBreak`, one timer per member and tenant, isolation, overnight, closed month, submitted month, entry errors, too long, placing a stop on another day.
- [x] A5. Phase 3 smoke: start, second start refused, stop refused then stopped with times, discard.

## Phase B: UI

- [x] B1. Today and Timesheet: start, running clock, stop, discard. One shared `TimerPanel` component; the clock counts from the server's `nowUtc`.
- [x] B2. Show the refusals: closed month, too long, missing task name, under a minute, inside the break. A refusal keeps the timer and offers a date and times to stop with.
- [x] B3. Playwright at 375 and 320 px; docs.

## Known Limits

- The panel exposes only the task name. The project, category, personal task, detail, and notes that the API accepts as a prefill are not offered; the stop request sends only the name, and the member edits the entry afterwards.
- Start is disabled on Today when the month is locked, and on Timesheet only when the current month is not editable (the panel is shown for any month, but the timer always starts now).
- A timer left running is stopped by hand: with chosen date and times, or by discarding it.

## Out Of Scope

- Pausing a timer, or several timers at once.
- Starting a timer for another member.
- Timer reminders, or stopping a forgotten timer automatically.

## Related

- [[Product Backlog]]
- [[Copy And Bulk Entry Plan]]
- [[Timesheet Approval Plan]]
- [[Project Status]]
