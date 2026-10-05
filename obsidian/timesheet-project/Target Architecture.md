# Target Architecture

> Agreed design, 5 October 2026. This describes the intended implementation, not the current repository state.

## System Shape

```text
Browser
  └── Frontend Shell
        ├── Identity/Profile remote
        ├── Timesheet remote
        ├── Reports remote
        └── Admin remote
              │ REST
              ▼
          Gateway (public entry point)
              ├── Identity service ── gRPC token validation
              ├── Timesheet service
              ├── Reporting service
              └── Notification service

RabbitMQ ── commands/events ──> Reporting worker + Notification worker
Redis ── token-validation cache and distributed cache
RustFS ── generated reports and signed-document uploads
PostgreSQL instance ── identity_db | timesheet_db | reporting_db | notification_db
```

Gateway is the only public backend entry point. Services do not query each other's database. REST is used from frontend to Gateway, gRPC is used for synchronous internal operations, and RabbitMQ is used for asynchronous commands and events.

## Services And Ownership

| Service | Owns | Key responsibilities |
| --- | --- | --- |
| Gateway | Public HTTP edge only | Routing, CORS, rate limiting, API versioning, authentication hand-off, and Scalar documentation portal. |
| Identity | `identity_db` | Tenants, memberships, roles, profiles, invitations, account activation, password policy, PASETO, refresh sessions, and token-validation gRPC API. |
| Timesheet | `timesheet_db` | Projects/categories, personal tasks, Kanban state, time entries, leave entries, holidays, audit trail, and month locking. |
| Reporting | `reporting_db` | Report definitions, Quartz schedules, report-run state, immutable snapshots, exports, signed-document metadata, and retention jobs. |
| Notification | `notification_db` | Email delivery records and templates for account, invitation, password, and report notifications. |

Each service has its own PostgreSQL database and migration history, while Docker Compose hosts them in one local PostgreSQL instance. No cross-database reads are allowed.

## Multi-Tenancy And Time

- Every tenant-owned record has `tenant_id`; query filters and authorization must enforce it at the service boundary.
- A new tenant defaults to `Asia/Bangkok`; Tenant Admin can change it. Timestamps are stored in UTC and converted for UI, report periods, Quartz schedules, and month-edit rules.
- A registrant either creates a tenant (becoming Tenant Admin) or joins an existing tenant only through an invitation link/code.
- Initial roles are Platform Admin, Tenant Admin, and Employee.

## Frontend

The frontend is a separately deployed micro-frontend system: a Shell owns navigation, session handling, shared design tokens, i18n, and route composition. Route-based remotes own Identity/Profile, Timesheet, Reports, and Admin. Use Tailwind and Thai as the default locale while supporting Thai/English through translation keys.

Timesheet remains table-first and responsive. A task tray lets users drag shared project/category tasks or personal tasks into a day to prefill the entry. Kanban is an optional task view, not the source of truth for recorded time. An entry supports date, start/end time, task name, detail, category/project, and notes; an end time earlier than the start time means an overnight entry, limited to 24 hours.

## Reports And Documents

- Weekly reports run every Monday; monthly reports run at 00:15 on the first day of the next month for the closed month; annual reports run 1 January. Quartz cron and timezone are database-backed per tenant.
- Report generation is a RabbitMQ command handled by a background worker. Runs are idempotent and retry-safe.
- Monthly timesheet PDFs are immutable snapshots formatted from the supplied prototype and are signed outside the system. An administrator uploads the signed PDF back to RustFS with metadata.
- Weekly/monthly/yearly and performance reports support PDF and XLSX. Performance uses recorded hours, work days, leave, entry completeness, project/category allocation, and overtime; it does not invent a KPI score.
- RustFS objects have version, checksum, and metadata. Retention defaults to seven years and is tenant-configurable; purge removes the database metadata and object while recording an audit event.

## Local Runtime And Operations

Docker Compose is the source of truth for local full-stack execution. It includes application containers, PostgreSQL, Redis, RabbitMQ, RustFS, the existing MailDev SMTP service, Prometheus, Grafana, and necessary health checks. Aspire AppHost is a developer-experience companion: it must not create duplicate infrastructure and should expose Scalar support for API exploration.

All services expose OpenAPI JSON. Gateway offers one Scalar portal linking service definitions; individual services retain Swagger UI as a compatibility endpoint. OpenTelemetry, structured redacted logs, Prometheus metrics, Grafana dashboards, and health endpoints are mandatory. Distributed tracing can use Jaeger or Tempo when added to Compose.

## Related

- [[Timesheet Project Index]]
- [[Architecture]]
- [[Security And Tenant Model]]
- [[Development Roadmap]]
