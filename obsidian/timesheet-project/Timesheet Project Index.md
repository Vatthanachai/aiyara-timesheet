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
- [[Full Api Controllers Plan]] — agreed controller, response, client, and validation changes.
- [[Platform Organization Management Plan]] — approved Platform Admin organization lifecycle and UI work.
- [[Platform Admin Console Plan]] — sys admin permission audit and the phased console plan (draft, awaiting decisions).
- [[Reporting Workspace Plan]] — reporting remote gaps and the R1–R3 delivery plan.
- [[Tenant Member Management Plan]] — how a Tenant Admin invites and manages members (phases A–C).
- [[Working Days Plan]] — how a Tenant Admin sets the organization's working weekdays (phases A–B).

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
- [[Full Api Controllers Plan]]
