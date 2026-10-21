# Administration Page Plan

> Drafted 21 October 2026 from a code audit of `develop` at `db770cc`. Branch `feature/administration-page`. Covers the Administration remote (`src/Frontend/remotes/administration`), where a Tenant Admin runs their organization. Follows [[Home Page Plan]], [[Timesheet Page Plan]], and [[Reporting Page Plan]], whose tokens, type, and pictures of time it reuses.

## Implementation Progress

- **Phases A–C** implemented on `feature/administration-page` (21 October 2026). Validation is recorded in [[Project Status]].

## Audit: What Exists On `develop`

| Area | Today | Problem |
|---|---|---|
| Heading | "WORKSPACE SETTINGS" eyebrow, "Administration", a subtitle, and a pill showing the tenant's GUID | The Shell already says Administration; the GUID means nothing to an admin, while the organization's own name, logo, and code are not shown |
| Counters | Three cards: projects, categories, scheduled holidays | Counts nobody asked for, in the template card kit |
| Length | Nine panels in one scroll: organization, date and time format, members (with groups and invitations), projects, categories, working days and hours, holidays, password policy, close a month | An admin who comes to invite someone or add a holiday scrolls past everything else; related settings are not grouped |
| Panels | Every panel has a coloured glyph icon (◈ ◷ ◎ ▦ ◇ ▤ ▣ ⌑), a heading, and a hint, in identical rounded cards with shadows | Decoration that carries no information |
| Members | Flex rows whose role, status, group, and actions land at different positions per member | The list does not read as a table; columns do not line up |
| Working days and hours | Seven checkbox cards, hour selects, a break toggle, and a sentence | The setting is a shape in time (a week of working hours and a break) but is shown only as form fields |

## Decisions

1. Frontend only, in the Administration remote. No API change. Every existing action, message, validation, and API call stays.
2. The heading is the organization itself: its logo, name (the page's `h1`), and organization code. The tenant GUID pill and the counter cards go.
3. The settings are grouped into five tabs (a WAI-ARIA tab list: arrow keys, Home/End, only the selected tab is a tab stop), so each visit shows one subject:
   - **Organization**: name and logo; date and time format.
   - **People**: members and invitations, member groups, pending invitations. The tab shows the member count.
   - **Working time**: working days and hours; holidays; close a month.
   - **Projects and categories**: projects; categories.
   - **Security**: password policy.
   The chosen tab is remembered for the session.
4. Panels lose their glyph icons and shadows; each keeps a heading and one plain hint.
5. Members become a real table: member (name, email, "You"), role, status, group, actions, so every column lines up. On phones each member stacks.
6. Working days and hours shows **the week**: one row per weekday (Monday first) with the working hours as a band and the break hatched, drawn from the values being edited, so a change shows at once before it is saved. Each row is also that day's checkbox; a day off shows the day-off fill. The hour selects, break toggle, and summary stay under it.

## Design Direction

The week is the memorable element: the organization's working time drawn as seven bars, the same picture the Timesheet calendar and the home page use for a day. Everything else is quiet forms in flat panels.

- **Colour**: shared tokens. Ledger blue `#155EEF` (selected tab, primary actions), working-hours tint `#DCE6FD` (the band), break hatch `#94A3B8`, day-off fill `#EEF1F5`, ink `#172033`, warning `#B42318` (deactivate, delete, close a month).
- **Type**: Bai Jamjuree for the organization name, tab labels, panel headings, and the week's hour numerals; the Inter/system stack for forms. No all-caps eyebrows.
- **Layout**: left-aligned; a tab bar under the heading; one column of panels per tab, two columns where two short panels sit together (projects and categories). Tables align their columns; phones stack.
- **Tabs never scroll** (changed on request the same day, `fix/administration-tabs-fit`): a single underlined row while the five fit (wider than 56rem); five equal buttons below that; on phones (under 36rem) three on the first row and the two longer ones sharing the second. The Thai "โครงการ|และหมวดหมู่" marks where it may break, since Thai has no spaces.
- **Phones**: members stack one per block; the week keeps its bars with every other hour labelled; group rows and invitations wrap instead of pushing buttons off the edge.
- **Motion**: the tab underline moves to the chosen tab; nothing animates on load.

```
[logo] Acme Studio
       Organization code ACME123
Organization   People 12   Working time   Projects and categories   Security
──────────────────────────────────────────────────────────────────────────
Working days and hours
        08   09   10   11   12   13   14   15   16   17   18
☑ Mon        ██████████████//////███████████████
☑ Tue        ██████████████//////███████████████
…
☐ Sat   Day off
[Start 09:00] – [End 17:00]   ☑ Break [12:00] – [13:00]
A normal working day is 7 hours; reports count a longer day as overtime.   [Save working days and hours]
```

## Phase A — Structure

- [x] A1. Heading with the organization's logo, name, and code; counters and the GUID pill removed.
- [x] A2. Tabs with keyboard support and the session-remembered choice; panels regrouped under them.

## Phase B — Panels

- [x] B1. Flat panels without glyph icons; shared tokens and Bai Jamjuree in the remote.
- [x] B2. Members as an aligned table that stacks on phones; groups and invitations restyled to match.
- [x] B3. Working days and hours with the live week picture; day rows as checkboxes.

## Phase C — Quality And Docs

- [x] C1. Keyboard focus, reduced motion, 1280 / 768 / 375 / 320 px with no sideways scroll; screenshots in Thai and English.
- [x] C2. Playwright: Administration tests open the right tab first; new checks for the tabs (keyboard, remembered choice), the heading, and the week picture following edits.
- [x] C3. Administration `nuxt build`; Docker Compose `administration-remote` rebuilt and healthy. Docs: [[Project Status]], [[Timesheet Project Index]].

## Out Of Scope

- New settings, API changes, and the system administrator console (`/sys-admin`).

## Related

- [[Tenant Member Management Plan]]
- [[Working Days Plan]]
- [[Date And Time Format Plan]]
- [[Project Status]]
