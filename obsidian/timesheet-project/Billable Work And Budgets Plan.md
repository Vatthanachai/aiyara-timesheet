# Billable Work And Budgets Plan

> Drafted 22 October 2026 from item 6 of [[Product Backlog]] and a code audit of `develop` at `7199e5e`. Implemented 22 October 2026 on `feature/billable-budgets` (Phase A, API, and Phase B, UI). Covers how a project carries a client, a billable flag, an hourly rate, and an hours budget, and how a Tenant Admin reads hours used against budget and the billable amount by project and by client.

## Audit: What Exists On `develop`

| Capability | Backend | UI |
|---|---|---|
| Projects and categories | `projects` and `categories` tables with a name only; Tenant Admin `POST/PUT/DELETE projects` and `categories` take `{ name }`; `GET catalog` returns them to every member | Administration manages the names |
| Hours per entry | `time_entries.DurationMinutes`, worked minutes with the organization's break already left out when the entry was saved; `ProjectId` optional | Timesheet entry form picks a project |
| Reports | Report service builds weekly, monthly, annual, and performance reports per member, group, or organization from month snapshots (`timesheet.month.*` events); snapshots carry the project id and name per entry, nothing about money | Reporting remote |
| Money, currency, client, budget | None anywhere | None |

## Decisions

1. **Where the data lives.** Five nullable or defaulted columns on the Timesheet service's `projects` table (migration `AddProjectBilling`): `Client`, `IsBillable` (default false), `HourlyRate`, `CurrencyCode`, `BudgetHours`. Project is already a `TenantRecord` behind the tenant query filter, so isolation is unchanged. Category stays a name.
2. **Who may edit.** Tenant Admins only, as for every catalog write today (`actor.IsAdmin`; Platform Admins never reach tenant data). A member reading the catalog gets `client` and `isBillable` but `hourlyRate` and `currencyCode` are returned as null, since rates are the organization's business. Budgets are shown to members in the catalog (`budgetHours`) but their used and remaining figures are admin-only (usage routes are `403` for members).
3. **Money type and currency.** `decimal` rate, `numeric(12,2)`, 0 to 1,000,000 in cents. There is no organization-wide currency setting today, so the currency is a field on the project: an ISO 4217 code of three letters, stored in capitals, required whenever a rate is set. Reports never add money in different currencies together: amounts are listed per currency (`[{ currencyCode, amount }]`). There is no conversion.
4. **Client.** An optional free-text `Client` on the project (at most 200 characters, trimmed, empty means none). The by-client report groups by it ignoring case and shows the spelling of the first project by name. Making clients their own entity (contact, billing address, rename in one place) is the smallest design that supports the report only after the report proves useful, so it is left out.
5. **Budget semantics.** `BudgetHours` is a total for the whole life of the project, above 0 to 1,000,000 in hundredths. Used = the sum of `DurationMinutes` of non-deleted entries on the project in all time, so breaks are already excluded and entries on a deleted project still count for it. `remainingHours = budget - used` (negative when over), `overBudget = used > budget`. The budget **only warns**: saving an entry is never blocked. A period argument limits the period figures (minutes, billable amount) but never the budget figures.
6. **Billable amount.** A billable project's period minutes divided by 60 and multiplied by the rate, rounded to the cent (away from zero); null when the project is not billable or has no rate. A billable project without a rate still reports its billable minutes.
7. **Rate changes are not snapshotted.** Reports multiply today's rate by the hours, so changing a rate reprices time already logged, and an invoice already issued is not protected. This is acceptable while there is no invoicing; a rate history (a rate valid from a date) would be a later change. The change is audited (`project` `billing_updated`, with before and after).
8. **API shape.** Billing details change through their own route so the existing rename (`PUT projects/{id}`, name only) never clears them. `POST projects` also accepts the optional billing fields. All three usage routes are admin-only reads of live entries, in the organization's calendar (entry dates are local dates already, so no time zone arithmetic), with optional inclusive `from` and `to` dates (all time when both are left out).
9. **Where the report lives.** Timesheet owns projects, rates, and entries, and Report reads month snapshots that carry no rates, so by-project and by-client summaries are served live by the Timesheet service. Generated, signed XLSX/PDF documents of these summaries in Report are left for a later change (see Out Of Scope).

## API

All under `/api/v1/timesheets` through Gateway; Tenant Admin unless noted. Errors use `ServiceErrorResponse`.

- `POST projects` (name plus the optional `client`, `isBillable`, `hourlyRate`, `currencyCode`, `budgetHours`) returns `201` with the project; `400 VALIDATION_FAILED`, `403 FORBIDDEN`.
- `PUT projects/{id}/billing` with `{ client, isBillable, hourlyRate, currencyCode, budgetHours }` replaces all five (null clears); returns `200` with the project; `400 VALIDATION_FAILED`, `403`, `404 NOT_FOUND` (also another tenant's project or a deleted one).
- `GET projects/usage?from&to` returns `{ from, to, unassignedMinutes, projects: [row], totals: { periodMinutes, billableMinutes, amounts } }`; `400 INVALID_DATE` for a bad date, `from` after `to`, or a year outside 2000 to 2100.
- `GET projects/{id}/usage?from&to` returns `{ from, to, project: row, members: [{ ownerId, minutes }] }`.
- `GET clients/usage?from&to` returns `{ from, to, unassignedMinutes, clients: [{ client, projectCount, periodMinutes, billableMinutes, nonBillableMinutes, amounts }], totals }`.
- Row: `projectId, name, client, isBillable, hourlyRate, currencyCode, budgetHours, isDeleted, periodMinutes, billableMinutes, billableAmount, usedMinutes, usedHours, remainingHours, overBudget`.

## Phase A — API

- [x] A1. `Project` columns, mapping, migration `AddProjectBilling`.
- [x] A2. `TimesheetOperations`: `SaveProjectBilling`, billing checks on create, rates hidden from members in the catalog, `ProjectsUsage`, `ProjectUsageOf`, `ClientsUsage`; create and update of a project or category now return the whole record, not only its id.
- [x] A3. Controller routes with OpenAPI response types.
- [x] A4. Tests (`ProjectBillingTests`): validation of rate, currency, budget, and client; permissions; another tenant sees nothing and gets `404`; usage with a break, worked-through break, deleted entry, and an edited entry; periods, rounding, deleted projects, grouping by client ignoring case, currencies kept apart.
- [x] A5. Phase 3 smoke: billing, usage by project and client, member permissions, and the member catalog on the Compose test stack.

## Phase B — UI

- [x] B1. Administration → Projects: a billing form on each project (client, billable toggle, rate with currency, and hours budget), saved through `PUT projects/{id}/billing`; checks in `utils/billing.ts`.
- [x] B2. Reporting gets a **Projects and clients** panel (`ProjectUsage.vue`, Tenant Admins only): by project with hours used against the budget, a bar, and an over-budget warning, and a members list per project; by client with billable and non-billable hours and amounts per currency; a period picker (this month, last month, a chosen month, a custom range, all time). Shared formatting is in `shared/money.ts`.
- [x] B3. Playwright (`billing.spec.ts`) and docs (this note, [[Project Status]], [[Product Backlog]], [[Timesheet Project Index]]).

## Out Of Scope

- Generated XLSX, CSV, and PDF documents and scheduled runs of the by-project and by-client reports in the Report service, and showing them in the signed month PDF.
- A rate history or snapshotting rates into approved months.
- A client entity, per-member or per-category rates, currency conversion, invoices.
- A warning to the member when saving an entry that takes a project over budget (members cannot read used figures today).
- Budgets per period or in money.

## Known Limits

- Minutes per entry are stored when the entry is saved, so a later change of the organization's break does not change past entries (the same as every report today).
- The by-project report reads live entries, not the approved snapshots, so it can differ from a signed month if an approved month is later returned and edited.

- Rates are not snapshotted: changing a rate reprices time already logged (decision 7).
- A budget is for the whole life of the project, not per period.
- Amounts in different currencies are listed per currency and never summed; there is no conversion.
- Usage is served live by the Timesheet service, not by Report, so it is not the approved month snapshot.
- Generated XLSX, CSV, and PDF documents, and scheduled by-project and by-client reports, are not built.
- Members get no over-budget warning when they save an entry; used figures are admin-only.
- The UI lives in two places: the Reporting **Projects and clients** panel (read) and the Administration billing form (write).
- When the test stack is started with `-Frontend`, the Shell's remote iframe URLs must point at the test stack's own ports; the remote URLs are not rewritten automatically (see [[Test Stack Plan]]).

## Related

- [[Product Backlog]]
- [[Reporting Page Plan]]
- [[Test Stack Plan]]
- [[Timesheet Approval Plan]]
- [[Project Status]]
