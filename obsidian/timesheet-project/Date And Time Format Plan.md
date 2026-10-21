# Date And Time Format Plan

> Drafted 21 October 2026 from a code audit of `develop` at `ae3b37c`. Covers how an organization's Tenant Admin chooses how dates and times appear across the product. Branch `feature/date-time-format`.

## Audit: What Exists On `develop`

| Where | Dates today | Times today |
|---|---|---|
| Timesheet remote (entries, leave, holidays) | ISO text `2026-10-05` | `09:00` (`slice(0, 5)`) |
| Reporting → Member timesheets / My timesheet | `Intl` short weekday + day + month in the viewer's locale (`th-TH` shows the Buddhist year) | `09:00` |
| Reporting → Report history | `Intl` `dateStyle: medium` + `timeStyle: short` | same |
| Reporting → Export XLSX / CSV | ISO `2026-10-05` | `09:00` |
| Generated PDF / XLSX reports (Report worker) | XLSX `2026-10-05`, PDF day number; period `yyyy-MM-dd – yyyy-MM-dd` | `09:00` |
| Administration (pending invitation expiry) | `toLocaleDateString` | — |

There is no organization setting for either. Browser date inputs (`type="date"` / `"month"`) use the device's own format and cannot be restyled; they stay as they are.

## Decisions

1. The setting belongs to the organization in Identity, next to its name and logo: `tenants.DateFormat` and `tenants.TimeFormat`, defaulting to `dd/MM/yyyy` and `HH:mm`. `GET /api/v1/identity/organization` returns both to every member; `PUT /api/v1/identity/organization/formats` changes them (active Tenant Admin with a current password, like renaming) and is audited as `organization.formats_updated`.
2. Date formats: `dd/MM/yyyy`, `dd/MM/bbbb` (Buddhist year, พ.ศ.), `d MMM yyyy` (month name in the viewer's language), `yyyy-MM-dd`, `MM/dd/yyyy`. Time formats: `HH:mm` (24-hour) and `h:mm a` (12-hour, `AM`/`PM`). Anything else is `400 VALIDATION_FAILED`.
3. The Shell already loads the organization for its header; it passes `dateFormat` and `timeFormat` to every remote in the `aiyara.session` message, like the language. A shared `shared/date-format.ts` formats dates and times, so every remote formats the same way.
4. Month labels follow the year style: `dd/MM/bbbb` shows Buddhist years (`ตุลาคม 2569`), every other format Gregorian years (`ตุลาคม 2026`), in both languages.
5. CSV exports keep ISO dates and 24-hour times for importing; XLSX exports and generated reports use the organization's formats.
6. The Report worker reads the formats when it renders, through a new Identity gRPC call `GetOrganizationSettings`, so scheduled and month-close reports follow the setting too. If Identity cannot answer, the report uses the defaults rather than failing.

## Phase A — Identity

- [x] A1. `Tenant.DateFormat` / `TimeFormat` (migration `AddOrganizationFormats`), `OrganizationFormats` with the allowed values, `OrganizationBrandingService.SetFormatsAsync` (admin check, validation, audit), `PUT organization/formats`, both fields in the organization view.
- [x] A2. gRPC `GetOrganizationSettings(tenant_id)` → `found`, `date_format`, `time_format`, `time_zone_id`.
- [x] A3. Tests: members read the defaults; only admins change them; invalid values 400; audit (none for an unchanged save); another tenant unaffected. The gRPC call is checked end to end in Phase C, because the service is internal.

## Phase B — Shell, Administration, Timesheet, Reporting

- [x] B1. `shared/date-format.ts` (`formatDate`, `formatTime`, `formatMonth`, `readSessionFormats`) and the Shell passing the formats in `aiyara.session`.
- [x] B2. Administration **Date and time format** panel: two choices with a live preview of today; saving tells the Shell to reload the organization, so every remote gets the new formats.
- [x] B3. Timesheet: entries, leave, holidays, and the month label. Reporting: Member timesheets / My timesheet, history timestamps and periods, and the XLSX export.
- [x] B4. Playwright: the admin saves a format and the preview changes; the Timesheet and Reporting remotes show `dd/MM/bbbb` and `h:mm a`; narrow widths.

## Phase C — Generated Reports

- [ ] C1. `ReportDocumentData` carries the formats; the XLSX date and time columns, the PDF day column, and the period use them.
- [ ] C2. The worker reads them with `GetOrganizationSettings`, falling back to the defaults.
- [ ] C3. Tests: renderer output for each format; the worker uses the organization's formats and survives an Identity failure. Docs: [[Project Status]], [[Timesheet Project Index]].

## Out Of Scope

- Per-member format preferences.
- Number and currency formats, first day of the week.
- Restyling browser date pickers.

## Related

- [[Working Days Plan]]
- [[Security And Tenant Model]]
- [[Project Status]]
