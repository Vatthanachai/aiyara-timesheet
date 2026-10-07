# Development Roadmap

> Delivery order is dependency-driven. Each phase must leave the Docker Compose stack runnable and add automated tests before the next phase expands its surface area.

## Phase 0 — Platform Baseline

1. Normalize solution/project references and add missing projects for Notification, Reporting worker, gRPC contracts, and frontend Shell/remotes.
2. Create Docker Compose for PostgreSQL databases, Redis, RabbitMQ, RustFS, existing MailDev, services, frontend, Prometheus, and Grafana. Supply secrets exclusively via ignored environment files or runtime secret stores.
3. Standardize configuration, health checks, OpenTelemetry, structured redacted logs, and service-level readiness dependencies.
4. Update Aspire AppHost to reference the Compose-backed development services without duplicating them; add Scalar support to AppHost.

**Exit outcome:** one command runs the local platform; every container has a health check and no credentials are committed.

## Phase 1 — Contracts, Tenancy, And Edge

1. Define versioned REST contracts at Gateway, protobuf contracts for Identity validation/profile lookup, and RabbitMQ event/command contracts with correlation and idempotency IDs.
2. Add per-service PostgreSQL databases, EF Core contexts, migrations, tenant-aware repositories/query filters, and service credentials.
3. Implement tenant creation, invitation acceptance, memberships, roles, and request tenant-resolution rules.
4. Implement Gateway routing, CORS, rate limiting, authorization integration, one Scalar portal, and links to every service's OpenAPI/Swagger endpoints.

**Exit outcome:** a user can create a tenant or accept an invitation without data leaking between tenants.

## Phase 2 — Identity And Notification

Implementation status (2026-10-06): Identity activation/login/reset and
authenticated password change, rotating refresh sessions, policy migration,
PASETO v4.public, gRPC validation, Redis cache/revocation and local SMTP
delivery are implemented and unit/Compose-smoke tested on
`feature/phase-two-identity-notification`. Primary validation and two-axis
review passed before the `develop` merge; this is not a release.

1. Replace `EncryptionService` password behavior with Argon2id PHC storage and a cryptographically secure temporary-password generator.
2. Implement tenant password policies, email-only login, activation, forced password reset, forgot-password, refresh-token rotation, session revocation, and the bootstrap Platform Admin flow.
3. Implement PASETO `v4.public`, signing-key configuration, Identity gRPC validation, Redis validation cache, and revocation events.
4. Implement Notification templates and MailDev SMTP adapter for local flows; record delivery outcomes without recording secrets.

**Exit outcome:** self-registration, invitation, activation, login, reset, logout, and forced policy migration work end-to-end through Gateway.

## Phase 3 — Timesheet Core

Implementation status (2026-10-07): tenant-scoped timesheet models and APIs,
account profiles, migrations, month snapshots, audit records, confirmed RabbitMQ
outbox delivery, and the Shell plus three product remotes are implemented.
Solution tests, frontend builds, Compose health, and Phase 2/3 Gateway smoke
tests passed before the `develop` merge.

1. Implement profile fields (first name, last name, photo, job title), tenant holidays, leave entries, shared projects/categories, personal tasks, and optional Kanban status.
2. Implement responsive table-first time entry: inline row editing, historical browsing, drag-in task tray, overnight time calculation, validation, and current-month-only mutation rules.
3. Add soft deletion, audit trail, month locking, and domain events for report generation.
4. Build Shell, Identity/Profile, Timesheet, and Admin remotes with Tailwind, shared design tokens, Thai/English i18n, mobile layouts, and accessible interactions.

**Exit outcome:** an Employee can manage profile and current-month time/leave entries on desktop or mobile; Tenant Admin can configure tenant data and password policy.

## Phase 4 — Reporting And Signed Documents

Implementation status (2026-10-07): reporting APIs, tenant-local persisted
schedule dispatch, durable RabbitMQ command/event consumers, PDF/XLSX rendering,
RustFS object versioning, signed monthly PDF uploads, retention/audit purging,
report-ready email delivery through Notifications, and the Thai/English Reports
remote are implemented and validated on `develop`. Reports read immutable locked-month
snapshots owned by Timesheet; employee names come from Identity gRPC. Calendar,
tenant isolation, snapshot parsing, and renderer tests pass, as do reporting
service builds, the frontend production build, Compose health, and an
unauthenticated Gateway boundary smoke. The Phase 4 suite now exercises the
production reporting auth middleware over HTTP with a test Identity validator,
report request creation, PDF rendering, signed upload, latest-version download,
audit recording, and transient notification retries. Live Compose E2E now also
passes with a temporary account and real Identity-issued PASETO: it locks actual
Timesheet data, has the worker render a PDF from its snapshot to RustFS, uploads
and downloads a signed PDF, and verifies the report-ready email in MailDev.
`tests/phase4/Smoke.ps1` reproduces this flow. RustFS bucket setup handles an
empty bucket-list response.

1. Model report definitions, schedule configuration, Quartz persistence, run state, report snapshots, RustFS object metadata, and retention configuration.
2. Configure tenant-timezone schedules: Monday weekly reports, closed-month reports at 00:15 on the following month's first day, and annual reports on 1 January.
3. Publish report commands to RabbitMQ; implement idempotent Reporting worker generation of PDF/XLSX, retry handling, and notifications.
4. Render the monthly employee PDF from the supplied prototype. Support upload and versioned retention of the externally signed PDF.
5. Build Reports remote and dashboards for employee and tenant-admin summaries.

**Exit outcome:** reports generate without blocking APIs, are stored in RustFS, and signed monthly PDFs can be retained and audited.

## Phase 5 — Quality, Operations, And Hardening

Implementation status (2026-10-07): Phase 5 is in progress. The initial
observability slice exports HTTP, .NET runtime, and readiness metrics from the
APIs and Report Worker to Prometheus over OTLP, and provisions a Grafana
platform overview dashboard. Phase 1–4 tests and Compose smoke tests provide
partial coverage, including onboarding, identity, timesheet, reporting, signed uploads,
tenant isolation, session revocation, and retry behavior. Business metrics for
authentication, RabbitMQ, Quartz, report runs, and RustFS still need to be
added. A dedicated clean-volume Compose run verified metric ingestion from all
six backend processes and Grafana dashboard provisioning; the platform smoke
test and all 40 solution tests passed. Compose has no trace collector/backend.
Retention/purge and secret-redaction behavior lack systematic automated
scenario coverage, and the developer runbook still needs migration and
troubleshooting procedures.

1. Add unit tests for domain logic and password/token components; integration tests for every service/database; contract tests for gRPC and RabbitMQ; and E2E tests for registration, activation, login/reset, time entry, report generation, and signed upload.
2. Add Prometheus metrics and Grafana dashboards for service health, requests, authentication, RabbitMQ, Quartz, report runs, and RustFS failures. Add trace collection when the chosen backend is enabled.
3. Exercise retention/purge, session revocation, tenant-isolation, recovery/retry, and secret-redaction scenarios.
4. Publish developer runbook: Compose startup, local email inspection, database migration, Scalar/Swagger discovery, test execution, and troubleshooting.

**Exit outcome:** the Compose environment is repeatable, observable, security-tested, and documented for team development.

## Acceptance Rules That Apply To Every Phase

- Development follows the repository's root `AGENTS.md`: every implementation starts on a Git Flow branch and the primary agent must validate passing checks before merge into `develop`.
- No service may read another service's database.
- Every new tenant-owned record and query enforces tenant isolation.
- Secrets never enter Git, logs, traces, test snapshots, API documentation, or Obsidian notes.
- API changes update OpenAPI/Scalar documentation and contract tests in the same change.
- New user-facing work is responsive and uses i18n keys; Thai is the default locale.
- A change is not complete until its relevant automated tests pass in the Compose-compatible environment.

## Related

- [[Timesheet Project Index]]
- [[Target Architecture]]
- [[Security And Tenant Model]]
- [[Project Status]]
