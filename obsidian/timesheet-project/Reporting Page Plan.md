# Reporting Page Plan

> Drafted 21 October 2026 from a code audit of `develop` at `c230396`. Branch `feature/reporting-page`. Covers the Reporting remote (`src/Frontend/remotes/reporting`): member timesheets, My timesheet, requesting reports, report history, and report settings. Follows [[Timesheet Page Plan]], whose "hours against the normal working day" picture it reuses. See [[Reporting Workspace Plan]] for what the remote does.

## Implementation Progress

- **Phases A–D** implemented on `feature/reporting-page` (21 October 2026). Validation is recorded in [[Project Status]].

## Audit: What Exists On `develop`

| Area | Today | Problem |
|---|---|---|
| Heading | The Shell shows "Reports", then the remote "Reports and documents" and a subtitle | Repeats itself before anything useful |
| Status | Four identical cards: Ready, In progress, Needs attention, All reports | Template look; the one number that matters (needs attention) has the same weight as the total |
| Request | A form card above everything, report names joined with a middle dot ("Monthly timesheet · PDF") | Fine as a form; it sits above the people review that admins open the page for |
| Member timesheets | A group totals table, then a member table of hours, entries, and leave as numbers | Nothing shows whether a member has logged what the month expects; reading who is behind means doing arithmetic |
| A member's month | Five stat cards, an entries table, three lists | The month's shape (which days were worked, which are missing) is invisible |
| History | A seven-column table with a forced 52rem width; the actions cell is `display: flex`, which breaks its row lines | Scrolls sideways on most screens; statuses and their reasons squeeze into a narrow column |
| Settings | One long card: formats, schedules, retention, with middle-dot strings | Three unrelated settings read as one form |

## Decisions

1. Frontend only, in the Reporting remote. No API change. The remote also reads `GET /timesheets/catalog` (open to every member) for the working days, hours, and holidays it needs to know what a month expects.
2. **Expected hours**: a month expects its working days (the organization's work week, less holidays) times the normal working day (working hours less the break). A member's expectation also leaves out their leave days. In the current month a marker shows what is expected by today.
3. Order: member timesheets (admins) or My timesheet (members) first, then documents (request, status, history), then settings (admins).
4. Member timesheets become an **hours ledger**: each member's row has a bar of hours logged against what the month expects, the to-date marker, and overtime in amber past the end. Group totals use the same bars.
5. A member's month (and My timesheet) opens with a **month strip**: one bar per day filled against the normal working day, days off grey, then the figures as one row of numbers instead of five cards, then the entries and lists as before.
6. The four status cards become one sentence above the history ("1 ready to download, 1 being generated, 2 need attention."), with the attention count in red when it is not zero.
7. History keeps its table (and filters) but drops to five columns: the document (a PDF or XLSX file tag, the name, the period), who it is about (admins), the status with its reason, when it was requested and completed, and the actions. No forced width; on phones each row stacks.
8. Settings become three panels side by side: report formats, automatic reports, file retention. Middle-dot strings become separate, labelled parts.
9. Exports, the request form's behaviour, polling, and every API call stay as they are.

## Design Direction

The ledger is the memorable element: rows of people, each a bar of hours against what the month asks. Everything else is flat and quiet, in the same tokens as the Shell and Timesheet.

- **Colour**: shared tokens. Ledger blue `#155EEF` (hours logged), working-hours tint `#DCE6FD` (the expected length), ink `#172033` (the to-date marker), overtime amber `#B45309`, day-off fill `#EEF1F5`, attention red `#B42318` (only for counts and failed statuses).
- **Type**: Bai Jamjuree for headings, figures, and periods; the Inter/system stack for everything else. File tags ("PDF", "XLSX") are set in the display face at a small size; they are file extensions, so they stay as written.
- **Shape**: one hairline border per section, no shadows, bars with small radii.
- **Layout**: left-aligned, one column of sections. Figures right-aligned in tables. Settings in three columns on wide screens, one on phones.
- **Phones**: the ledger keeps name, bar (under the name), and hours; entries and leave give way and stay in a member's own view. View buttons read "View" with the full "View timesheet: name" as their name. History rows stack. The signed-PDF upload is an invisible file input over a button-styled label.
- **Motion**: none on load. Opening a member swaps the view; nothing else moves.

```
Member timesheets                             [All groups ▾] [October 2026]
Month open (may still change)  Expected so far: 120:00 of 168:00 hours per member.
Somchai Staff     ████████████▌·········|········   7:30 of 160:00   3 entries  1 leave  View
Former member     █                                 1:00             1 entry
Anong Admin       ·····················|········    0:00 of 168:00

Documents                                       1 ready to download, 1 being generated, 2 need attention.
[Report ▾] [Month ▾] [Year ▾] [Report on ▾]  Period: 01/09/2026 – 30/09/2026 (Asia/Bangkok)  [Generate report]
PDF  Monthly timesheet   Somchai Staff   Waiting for September 2026 to be locked   Requested 01/10/2026 09:00
     01/09 – 30/09/2026
```

## Phase A — Ledger

- [x] A1. Catalog read and expected-hours helpers (month, member, to date).
- [x] A2. Member table and group totals with hours bars, the to-date marker, and overtime.

## Phase B — A Member's Month

- [x] B1. Month strip for a member's month and My timesheet.
- [x] B2. Figures as one row; detail heading without the middle dot.

## Phase C — Documents And Settings

- [x] C1. Status sentence; request form restyled; report names without middle dots.
- [x] C2. History in five columns with file tags, status reasons, no forced width, rows that stack on phones.
- [x] C3. Settings in three panels.

## Phase D — Quality And Docs

- [x] D1. Fonts and tokens in the remote; keyboard focus; 1280 / 375 / 320 px with no sideways scroll; screenshots in Thai and English.
- [x] D2. Playwright: existing Reporting tests follow the new structure; new checks for expected hours, the to-date marker, overtime, and the month strip.
- [x] D3. Reporting `nuxt build`; Docker Compose `reporting-remote` rebuilt and healthy. Docs: [[Project Status]], [[Timesheet Project Index]].

## Out Of Scope

- New report kinds, charts beyond the ledger and the month strip, and changes to the generated PDF/XLSX documents.

## Related

- [[Reporting Workspace Plan]]
- [[Timesheet Page Plan]]
- [[Home Page Plan]]
- [[Project Status]]
