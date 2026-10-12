# Services And Projects

> Refreshed 12 October 2026 against `develop` at `7d0b911`.

## Executable Projects

| Project | Role | Current state |
| --- | --- | --- |
| `Aiyara.Timesheet.AppHost` | Aspire local-development orchestrator | Starts Gateway, Identity, Timesheet, Report, and Notification APIs, the Report Worker, and the Nuxt Shell; references Compose Postgres/Redis/RabbitMQ/RustFS as external resources and aggregates every API's OpenAPI document behind one Scalar reference. |
| `Aiyara.Gateways.Api` | Gateway HTTP API | Public entry point. Onboarding controller plus YARP reverse proxy, Identity gRPC token validation, Redis validation cache, CORS, and rate limiting are wired. |
| `Aiyara.Identities.Api` | Identity service HTTP API | Tenant/invitation onboarding, activation/login/refresh/logout/reset/password-change, tenant password policy, platform organization lifecycle, profile (incl. RustFS photo storage), and the Identity gRPC validation service are wired. |
| `Aiyara.Timesheet.Api` | Timesheet service HTTP API | Tenant-scoped projects/categories/holidays, personal tasks, time entries, leave, month locking/snapshots, audit trail, and the internal snapshot API for Reporting are wired. |
| `Aiyara.Report.Api` | Reporting HTTP API | Tenant-scoped definitions, report runs, schedules, downloads, signed PDF uploads, retention controls, and Identity gRPC token validation are wired. |
| `Aiyara.Report.Worker` | Reporting background worker | Quartz dispatch/purge jobs, durable RabbitMQ consumers, immutable Timesheet snapshot reads, PDF/XLSX generation, RustFS storage, and report-ready email dispatch are wired. |
| `Aiyara.Notifications.Api` | Notification service HTTP API | Internal credential-email and report-ready endpoints, MailDev SMTP delivery, and delivery-outcome persistence are wired. |
| `src/Frontend/app` | Shell user interface | Thai/English navigation, session storage, sys-admin organization management, and iframe embedding of the four remotes. Orchestrated by AppHost and Compose. |
| `remotes/identity-profile`, `remotes/timesheet`, `remotes/reporting`, `remotes/administration` | Remote user interfaces | Route-based Nuxt remotes embedded by the Shell; started by Compose only (not by AppHost). |

Every business HTTP endpoint across the five APIs, including `/internal/*`
routes, is now an MVC controller inheriting `BaseController`; see
[[Full Api Controllers Plan]].

## Backend Module Layout

The `identities`, `timesheet`, and `reports` areas each still declare
placeholder `Handlers`, `Models`, `Services`/`Servives`, and `Utilities`
projects from the original scaffold. All of them are empty (no `.cs` files)
and referenced by no other project; the business logic that would have lived
there sits directly in each `*.Api` project instead (for example
`TimesheetOperations.cs`, `ReportOperations.cs`, and
`Aiyara.Identities.Servives/Onboarding/OnboardingService.cs`, which is not
empty and holds real onboarding logic). Reporting stores schedules in the
tenant-scoped Reporting database and Quartz dispatches due work. Generated
documents use locked Timesheet snapshots, Identity profile lookup, and
RustFS. The spelling `Servives` is present only in the identities directory;
its assembly/project file remains `Aiyara.Identities.Services`.

## Shared Utilities

| Project | Intended capability |
| --- | --- |
| `Aiyara.Timesheet.ServiceDefault` | Aspire service defaults: telemetry, health checks, discovery, resilient HTTP. Referenced by all five APIs and the Report Worker. |
| `Aiyara.Timesheet.Component.Data` | EF Core, Npgsql/PostgreSQL, NetTopologySuite, unit-of-work and base-context abstractions. |
| `Aiyara.Timesheet.Component.Abstractions` | `BaseController`, `ServiceResponse` types, safe exception handling, validation error mapping, PASETO/Argon2id security, Swagger helpers, email templates, and logging. |

## Naming Notes

Several existing paths use `Componenet` while assembly names use `Component`;
the identities service directory is named `Aiyara.Identities.Servives`. These
are current repository names and should be treated carefully if renamed,
because solution and project references must be updated together.

## Related

- [[Timesheet Project Index]]
- [[Architecture]]
- [[Project Status]]
- [[Full Api Controllers Plan]]
