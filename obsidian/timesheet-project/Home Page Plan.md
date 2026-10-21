# Home Page Plan

> Drafted 21 October 2026 from a code audit of `develop` at `b97dde4`. Branch `feature/home-page`. Covers the Shell's home page (`/`): what a visitor sees before signing in, and what a member sees first after signing in.

## Implementation Progress

- **Phases A–C** implemented on `feature/home-page` (21 October 2026). Validation is recorded in [[Project Status]].

## Audit: What Exists On `develop`

| State | Today | Problem |
|---|---|---|
| Signed out | A generic hero ("Your workspace"), a sign-in card, then five equal links (forgot password, sign up, invitation, activate, system admin) stacked under the button | Nothing says what the product is for; the five links compete with Sign in and with each other; "Forgot password?" sits far from the password field |
| Signed in | The same hero, then one card per remote (Profile, Timesheet, Reports, Administration) | It repeats the header navigation and tells members nothing about their day; recording time is two clicks away |

`GET /api/v1/timesheets/me/timesheet?year&month` already returns, for the signed-in member, the month's entries (with `durationMinutes`, project and category names), leave, holidays, `workingDays`, `workHours` (start, end, break), and whether the month is closed. The home page needs no new API.

## Decisions

1. Frontend only, in the Shell (`src/Frontend/app`). No backend, contract, or remote change.
2. Signed-in tenant members (Tenant Admin and Employee) get a **Today** page built from `me/timesheet`: a greeting with today's date in the organization's format, hours logged today against the normal working day (working hours less the break), a ruler of today's working hours with today's entries and the break on it, this week day by day, and this month's total. One primary action, **Record time**, opens the Timesheet remote.
3. The week runs Monday to Sunday. Non-working weekdays, holidays (by name), and leave are labelled instead of drawn as empty bars. When the week crosses a month boundary the other month is fetched too.
4. Today is the browser's local date. An entry whose end is before its start (overnight) is drawn up to midnight.
5. The remote list stays, but as a quiet list of links under the day, not a card grid. Profile is left out, since it lives in the profile menu.
6. Platform Admins have no timesheet: their home is the System admin link only, and `me/timesheet` is never called for them.
7. If `me/timesheet` fails, the page keeps the greeting, Record time, and the links, and says the day could not be loaded with a Try again button.
8. Signed out: one sentence on what the product does, an illustrative day ruler (decorative, hidden from assistive technology), and the sign-in form. "Forgot password?" moves next to the password label. Signing up, invitation codes, and account activation are grouped as "New here?"; System administrator sign in moves to the page foot.
9. The organization-choice step after signing in keeps its behaviour; only its layout follows the new form.

## Design Direction

The day itself is the memorable element: a horizontal ruler of the working hours, hour numerals set large in the display face, logged time as solid ledger-blue bands, the break hatched. Everything around it stays quiet.

- **Colour** (on top of `shared/tokens.css`): paper `#F8FAFC` (canvas), ink `#172033`, ledger blue `#155EEF` (logged time, primary action), working-hours track `#DCE6FD`, break hatch `#94A3B8`, day-off fill `#EEF1F5`.
- **Type**: Bai Jamjuree (self-hosted through `@fontsource/bai-jamjuree`, Thai and Latin, 500 and 600) for the greeting, totals, and hour numerals; the existing Inter/system stack for body text and the form. No all-caps eyebrows on this page.
- **Layout**: left-aligned throughout. Signed out: two columns on wide screens (statement and ruler | form), one column on phones with the form first. Signed in: greeting and Record time, the ruler across the full width, then week (left) and month plus links (right); one column on phones.
- **Ruler details**: the hour numerals and the bands share a lane inset by 1rem, so the first and last hour are centred on their marks; phones show every other hour once a day spans more than nine.
- **Thai**: the signed-out headline is written as two phrases, each kept on one line, because Thai has no spaces to break at ("ที / ละชั่วโมง" was the first result).
- **Motion**: one moment only. On load, today's bands grow along the ruler once; nothing else animates. Under `prefers-reduced-motion` they appear at once.

```
Signed in, wide
┌──────────────────────────────────────────────────────────────┐
│ Wednesday 21/10/2026                                         │
│ Good afternoon, Orathai                     [ Record time ]  │
│ 5 h 30 m logged today of 7 h                                 │
│                                                              │
│  08    09    10    11    12    13    14    15    16    17    │
│  ┊     ███████████████ ////// ██████████       ┊             │
│        Design review   break  Sprint planning  now           │
├──────────────────────────────┬───────────────────────────────┤
│ This week                    │ October 2026                  │
│ Mon  ███████████  7 h        │ 98 h 30 m over 15 days        │
│ Tue  ██████       4 h 30 m   │                               │
│ Wed  ███████      5 h 30 m   │ Timesheet                     │
│ Thu  Chulalongkorn Day       │ Reports                       │
│ Fri  —                       │ Administration                │
│ Sat  Day off                 │                               │
│ Sun  Day off                 │                               │
└──────────────────────────────┴───────────────────────────────┘
```

## Phase A — Signed-In Today

- [x] A1. `useMyTimesheet` composable: fetch `me/timesheet` for the months the current week touches, with the same 401 → renew → retry as the layout; skipped for Platform Admins.
- [x] A2. `DayRuler` component: hour scale from the working hours (widened to fit entries), entries as bands with task names, the break hatched, a "now" mark during the day; accessible as a list of entries with times.
- [x] A3. Today page in `pages/index.vue`: greeting, logged-today summary against the normal working day, states for a non-working day, a holiday, leave, and no entries yet; Record time.
- [x] A4. Week list and month total; the remote links list; Platform Admin home.
- [x] A5. Loading and error states with Try again.

## Phase B — Signed-Out Welcome

- [x] B1. Statement, illustrative ruler, regrouped sign-in form ("Forgot password?" by the password label, "New here?" links, system admin link at the foot).
- [x] B2. The organization-choice step in the same layout.

## Phase C — Quality And Docs

- [x] C1. Bai Jamjuree through `@fontsource/bai-jamjuree`; home-page tokens; keyboard focus, reduced motion, 1280 / 375 / 320 px with no sideways scroll; screenshots checked.
- [x] C2. Playwright: the session test follows the new page; new tests for today's summary and ruler, a holiday in the week, a failed load with Try again, and the Platform Admin home.
- [x] C3. Shell `nuxt build`; Docker Compose `frontend` rebuilt and healthy. Docs: [[Project Status]], [[Timesheet Project Index]].

## Out Of Scope

- Starting a timer or recording time from the home page itself (Record time opens Timesheet).
- Team or organization summaries for Tenant Admins (Reporting has them).
- Changes to the other signed-out pages (sign up, activate, reset password).

## Related

- [[Working Days Plan]]
- [[Reporting Workspace Plan]]
- [[Project Status]]
