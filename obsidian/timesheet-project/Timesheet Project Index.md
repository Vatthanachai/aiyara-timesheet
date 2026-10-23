# Aiyara Timesheet Project

> Repository snapshot: 7 October 2026

## Purpose

`Aiyara.Timesheet` is an early-stage distributed timesheet application. The repository contains a Nuxt frontend, four ASP.NET Core API projects (Gateway, Identity, Timesheet, and Report), shared .NET components, and an Aspire AppHost for a subset of the local-development topology.

## Start Here

- [[Architecture]] — component map, orchestration boundary, and runtime relationships.
- [[Services And Projects]] — responsibilities and implementation state of every solution project.
- [[Project Status]] — verified capabilities, gaps, and suggested next work.
- [[Target Architecture]] — agreed target state for services, data, messaging, frontend, and local runtime.
- [[Security And Tenant Model]] — tenancy, roles, account lifecycle, PASETO, and password decisions.
- [[Organization Codes And Identity IDs]] — tenant login code format and Identity UUIDv8 allocation.
- [[Development Roadmap]] — dependency-ordered delivery plan and acceptance outcomes.
- [[Product Backlog]] — candidate work after Phase 5, in suggested order (not yet approved).
- [[Timesheet Approval Plan]] — members submit a month; Tenant Admins approve it or return it with a reason (phases A–B).
- [[Leave Types Plan]] — organization leave types, part-day leave, and yearly balances (phases A–B).
- [[Missing Entry Reminders Plan]] — daily and month-end emails about time not logged (phases A–B).
- [[Copy And Bulk Entry Plan]] — copy the previous working day or week, one entry on several days, and saved entry templates (phases A–B).
- [[Running Timer Plan]] — start and stop a server-kept timer from Today and Timesheet, stopping into a time entry (phases A–B).
- [[Billable Work And Budgets Plan]] — project client, billable flag, hourly rate, and hours budget, with hours used and the billable amount by project and client (phases A–B).
- [[Test Stack Plan]] — a separate Compose stack for validation, with its own ports and database, and the deploy step that rebuilds the main stack after merge (phases A–B).
- [[Full Api Controllers Plan]] — agreed controller, response, client, and validation changes.
- [[Platform Organization Management Plan]] — approved Platform Admin organization lifecycle and UI work.
- [[Platform Admin Console Plan]] — sys admin permission audit and the phased console plan (phases A–D, complete).
- [[Reporting Workspace Plan]] — reporting remote gaps and the R1–R3 delivery plan.
- [[Tenant Member Management Plan]] — how a Tenant Admin invites and manages members (phases A–C).
- [[Tenant Audit Log Plan]] — a Tenant Admin reads the organization's own member-management audit entries, filtered and paged, in an Administration Audit log tab (phases A–B).
- [[Member MFA Plan]] — Tenant Admins require an authenticator of their members: enroll at the next sign-in, recovery codes, and an audited admin reset (phases A–B).
- [[Working Days Plan]] — how a Tenant Admin sets the organization's working weekdays (phases A–B).
- [[Date And Time Format Plan]] — how a Tenant Admin chooses how dates and times appear everywhere (phases A–C).
- [[Home Page Plan]] — the Shell's signed-out welcome and the signed-in Today page (phases A–C).
- [[Timesheet Page Plan]] — the Timesheet remote as a month time card with a day panel (phases A–E).
- [[Reporting Page Plan]] — the Reporting remote's hours ledger, month strip, documents, and settings (phases A–D).
- [[Administration Page Plan]] — the Administration remote as the organization's own page, in five tabs, with the week drawn (phases A–C).
- [[Profile Page Plan]] — profile settings as your card, drawn live, beside the details form (phases A–B).
- [[Theme Modes Plan]] — light, dark, and system themes across the Shell and remotes, and depth: paper lifts, fields sink, buttons press (phases A–C).
- [[Form Fields Plan]] — every dropdown an autocomplete, date fields always dd/MM/yyyy, time fields always 24-hour (phases A–C).

## Repository Facts

- Solution: `Aiyara.Timesheet.slnx`
- Shared target framework: `.NET 10` with nullable reference types and implicit usings enabled.
- Frontend: Nuxt 4 / Vue 3, managed with Bun.
- Local orchestration: .NET Aspire AppHost, currently starting only the Identity API, Timesheet API, and frontend.
- Data platform: PostgreSQL is declared in the AppHost, but no API currently references it.

## Related

- [[Architecture]]
- [[Services And Projects]]
- [[Project Status]]
- [[Target Architecture]]
- [[Security And Tenant Model]]
- [[Development Roadmap]]
- [[Product Backlog]]
- [[Full Api Controllers Plan]]
