# Services And Projects

## Executable Projects

| Project | Role | Current state |
| --- | --- | --- |
| `Aiyara.Timesheet.AppHost` | Aspire local-development orchestrator | Starts PostgreSQL/pgAdmin, Identity API, Timesheet API, and Nuxt frontend. |
| `Aiyara.Gateways.Api` | Gateway HTTP API | Template controller only; included in the solution but not orchestrated by AppHost. |
| `Aiyara.Identities.Api` | Identity service HTTP API | Template controller only; references shared service defaults and is orchestrated by AppHost. |
| `Aiyara.Timesheet.Api` | Timesheet service HTTP API | Template controller only; references shared service defaults and is orchestrated by AppHost. |
| `Aiyara.Report.Api` | Reporting HTTP API | Tenant-scoped definitions, report runs, schedules, downloads, signed PDF uploads, retention controls, and Identity gRPC token validation are wired. |
| `Aiyara.Report.Worker` | Reporting background worker | Quartz dispatch/purge jobs, durable RabbitMQ consumers, immutable Timesheet snapshot reads, PDF/XLSX generation, RustFS storage, and report-ready email dispatch are wired. |
| `remotes/reporting` | Reports user interface | Thai/English employee history/download and tenant-admin configuration, schedules, retention, and signed uploads. |
| `src/Frontend/app` | User interface | Standard Nuxt starter; scripts support dev, build, generate, and preview. |

## Backend Module Layout

The `identities`, `timesheet`, and `reports` areas each have projects for `Api`, `Databases`, `Handlers`, `Models`, `Services`/`Servives`, and `Utilities`.

Reporting stores schedules in the tenant-scoped Reporting database and Quartz dispatches due work. Generated documents use locked Timesheet snapshots, Identity profile lookup, and RustFS. Authenticated end-to-end generation/upload tests and notification retry coverage remain pending. Other placeholder modules still await their domain behavior. The spelling `Servives` is present only in the identities directory; its assembly/project file remains `Aiyara.Identities.Services`.

## Shared Utilities

| Project | Intended capability |
| --- | --- |
| `Aiyara.Timesheet.ServiceDefault` | Aspire service defaults: telemetry, health checks, discovery, resilient HTTP. |
| `Aiyara.Timesheet.Component.Data` | EF Core, Npgsql/PostgreSQL, NetTopologySuite, unit-of-work and base-context abstractions. |
| `Aiyara.Timesheet.Component.Abstractions` | Cross-cutting controllers, requests, errors, validation, security/PASETO, Swagger, email, logging, and helper abstractions. |

## Naming Notes

Several existing paths use `Componenet` while assembly names use `Component`; the identities service directory is named `Aiyara.Identities.Servives`. These are current repository names and should be treated carefully if renamed, because solution and project references must be updated together.

## Related

- [[Timesheet Project Index]]
- [[Architecture]]
- [[Project Status]]
