# Timesheet Page Plan

> Drafted 21 October 2026 from a code audit of `develop` at `d41cdf2`. Branch `feature/timesheet-page`. Covers the Timesheet remote's Entries view (`src/Frontend/remotes/timesheet`), where members record their time. Follows [[Home Page Plan]], whose day ruler and type it reuses.

## Implementation Progress

- **Phases A–E** implemented on `feature/timesheet-page` (21 October 2026). Validation is recorded in [[Project Status]].

## Audit: What Exists On `develop`

| Area | Today | Problem |
|---|---|---|
| Heading | The Shell shows "Timesheet", then the remote repeats "AIYARA · WORKSPACE / Time entries / Log and review your work time in one place." | Three headings say the same thing; the month, which the whole page is about, is a small picker on the right |
| Summary | Four identical cards with icons (hours, entries, days, leave) | Template look; the numbers need a sentence, not four boxes |
| Calendar | Day numbers with dots for entries, leave, and holidays; non-working days filled orange (`#ffddcc`) | It cannot show how much was logged on a day, which is the question members ask; the orange shouts about the least important days |
| Entry form | Below the calendar, headed "ADD ENTRY / Selected date · …"; hidden while an entry is edited inline in the table | The day's existing entries are only in the long table further down, so members cannot see what they already logged for the day they are filling in |
| Entries | One flat table of the month, the date repeated on every row, edited inline | Hard to scan by day; editing happens far from the form |
| Leave | A separate panel at the bottom, holidays listed under it | Fine, but styled as one more identical card |
| Chrome | All-caps eyebrows on every panel; every panel the same rounded card with the same shadow | Generic; nothing ties the page to time |

## Decisions

1. Frontend only, in the Timesheet remote, plus moving the day ruler into `src/Frontend/shared` so the Shell and the remote draw a day the same way. No API change.
2. The month is the page heading. It sits next to the month controls, with one sentence of totals under it ("85 h logged over 13 days, 1 day of leave.") and the organization's working hours. The four summary cards go.
3. The calendar becomes a **time card**: weeks start on Monday (as on the home page); each day shows the hours logged and fills from the bottom in proportion to the normal working day (the working hours less the break). A day over the normal working day is marked as overtime. Non-working days get the quiet day-off fill, holidays show their name, and leave its kind. Today's date is circled; the selected day is outlined.
4. Selecting a day opens the **day panel** beside the calendar: the date, the day ruler with that day's entries and the break, the day's entries with Edit and Delete, then the entry form. Edit loads the entry into this form ("Edit entry", "Save changes") instead of a row inside the table, and selects its day.
5. Saved tasks stay under the form in the same column, since they fill it in. Drag and drop onto a day or the form keeps working.
6. The month's entries are listed below, grouped by day: a day row with the date and that day's total, then its entries. Edit there works as in decision 4.
7. Leave and holidays sit under the calendar.
8. Past or closed months stay read only; the day panel then shows the day without the form.
9. The Task board view is unchanged apart from the shared styles.

## Design Direction

The memorable element is the time card: a month you can read as a chart, each day filled to the hours worked. Everything around it stays quiet.

- **Colour**: the Shell's tokens. Ledger blue `#155EEF` (logged time, primary actions), working-hours tint `#DCE6FD` (a day's fill), break hatch `#94A3B8`, day-off fill `#EEF1F5`, overtime amber `#B45309` (text and the fill's top edge only), ink `#172033`, paper `#F8FAFC`.
- **Type**: Bai Jamjuree for the month heading, day numbers, hours, and totals (self-hosted, as in the Shell); the Inter/system stack for labels and forms. No all-caps eyebrows.
- **Shape**: panels lose their shadows and keep one hairline border; the time card's cells are square-cornered so the fills read as bars.
- **Layout**: left-aligned. Desktop: calendar (left, wider) and day panel (right); leave and holidays under the calendar, saved tasks under the form; the grouped month list across the full width. Phones: one column in the order calendar, day panel, saved tasks, leave, month list. Phone cells drop the hours figure, which crossed the fill line at that size; the fill shows the day and the day panel its total. A full normal day fills a cell up to its date row, so the fill never runs through the date.
- **Separators**: the working hours read "Working hours 09:00–18:00, break 12:00–13:00" (the old middle dot is gone); Thai sentences end without a full stop.
- **Motion**: none on load. Selecting a day moves the outline; the day ruler's bands grow in when the day changes, and not under reduced motion.

```
October 2026                                         [‹] [Oct 2026] [›]
85 h logged over 13 days, 1 day of leave.  Working hours 09:00–18:00, break 12:00–13:00
[ Entries | Task board ]
┌ Mon   Tue   Wed   Thu   Fri   Sat   Sun ─┐  Wednesday 21/10/2026          3 h 30 m
│              1     2     3     4     5   │  09  10  11  12  13  14 … 18
│             7:30  7:00                   │  ██████ //// ██
│  6     7     8     9    10    11    12   │  09:00–11:30 Design review   2.50  Edit Delete
│ 7:00  6:00  7:00  5:00                   │  13:00–14:00 Sprint planning 1.00  Edit Delete
│ ▇▇▇▇  ▇▇▇   ▇▇▇▇  ▇▇▇                    │  ── Add entry ──────────────────────
│ …                                        │  Task  Start → End  …   [Save]
└──────────────────────────────────────────┘  Saved tasks
Leave and holidays                            [Payment API] [Usability findings] …
──────────────────────────────────────────────────────────────────────
Entries in October 2026
Wed 21/10/2026                                           3 h 30 m
  09:00–11:30  Design review     Atlas     Design        2.50  Edit Delete
```

## Phase A — Shared Day Ruler

- [x] A1. Move `DayRuler.vue` to `src/Frontend/shared/components`, with explicit imports; the Shell home imports it from there.
- [x] A2. Bai Jamjuree and the home-page tokens in the Timesheet remote.

## Phase B — Time Card And Heading

- [x] B1. Month heading with the controls, the totals sentence, and the working hours (`.work-hours-label`).
- [x] B2. `MonthCalendar` as the time card: Monday first, hours and fill per day, overtime, day off (`.weekend`), holiday (`.holiday-marker`), leave (`.leave-marker`), today, selected; drop a task on a day.

## Phase C — Day Panel

- [x] C1. `DayPanel`: date, ruler, the day's entries with Edit and Delete, and the read-only state.
- [x] C2. `EntryComposer` in add and edit modes; the inline edit row in the table goes.
- [x] C3. Saved tasks restyled under the form.

## Phase D — Month List, Leave, Board

- [x] D1. `EntriesTable` grouped by day with day totals.
- [x] D2. Leave and holidays under the calendar; board and shared styles without eyebrows or card shadows.

## Phase E — Quality And Docs

- [x] E1. Keyboard focus, reduced motion, 1280 / 768 / 375 / 320 px with no sideways scroll; screenshots checked in Thai and English.
- [x] E2. Playwright: existing Timesheet tests follow the new structure; new checks for day hours and fills, overtime, the day panel's entries, and editing in the form.
- [x] E3. Timesheet remote and Shell `nuxt build`; Docker Compose `timesheet-remote` and `frontend` rebuilt and healthy. Docs: [[Project Status]], [[Timesheet Project Index]].

## Out Of Scope

- A running timer, copying a previous day, or bulk entry.
- Changes to the API, the Task board's behaviour, or the Shell's remote frame.

## Related

- [[Home Page Plan]]
- [[Working Days Plan]]
- [[Project Status]]
