# Aiyara Timesheet Project

> Repository snapshot: 5 October 2026

## Purpose

`Aiyara.Timesheet` is an early-stage distributed timesheet application. The repository contains a Nuxt frontend, four ASP.NET Core API projects (Gateway, Identity, Timesheet, and Report), shared .NET components, and an Aspire AppHost for a subset of the local-development topology.

## Start Here

- [[Architecture]] — component map, orchestration boundary, and runtime relationships.
- [[Services And Projects]] — responsibilities and implementation state of every solution project.
- [[Project Status]] — verified capabilities, gaps, and suggested next work.
- [[Target Architecture]] — agreed target state for services, data, messaging, frontend, and local runtime.
- [[Security And Tenant Model]] — tenancy, roles, account lifecycle, PASETO, and password decisions.
- [[Development Roadmap]] — dependency-ordered delivery plan and acceptance outcomes.

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
