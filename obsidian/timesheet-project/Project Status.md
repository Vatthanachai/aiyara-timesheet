# Project Status

> Updated 7 October 2026. Phases 0–3 are complete; Phase 4 reporting remains in progress. See [[Development Roadmap]].

## Phase 0 Platform Baseline

- The Compose topology includes backend APIs, Report Worker, frontend Shell and
  remotes, PostgreSQL, Redis, RabbitMQ, RustFS, optional MailDev, Prometheus,
  and Grafana. Long-running containers have health checks; Phase 1's one-shot
  database provisioner is checked for successful completion.
- APIs and Report Worker share structured JSON logging, redaction support,
  OpenTelemetry instrumentation, liveness, and dependency reachability checks.
- AppHost represents Compose infrastructure as external resources and provides
  dependency endpoints to locally launched application projects. It also hosts
  the Scalar API reference.
- `tests/platform/Smoke.ps1` verifies the runnable Compose stack and API docs.
  Phase 0 TCP readiness probes establish reachability; Phase 1 adds
  authenticated database connectivity checks.
- Validation on 6 October 2026: solution restore/build and Compose configuration
  passed; all 17 core containers became healthy; backend health and OpenAPI
  paths passed the smoke test. With Redis stopped, Identity returned 200 from
  `/alive` and 503 from `/health`, then recovered after Redis restarted.
  AppHost published a manifest containing the external Compose endpoints and
  Scalar reference. The solution build has 76 existing warnings and no errors.

## Implemented Foundation

- .NET solution structure, central build configuration, and central package version management.
- Aspire AppHost that launches the backend applications and a Bun-powered Nuxt application.
- Compose PostgreSQL with persistent storage and separate service databases.
- Shared operational defaults applied to all backend applications.
- Gateway and Report API projects, each exposing the template `WeatherForecast` controller.
- Cross-cutting utility types for data access, API conventions, security/PASETO, Swagger, logging, and email.

## Phase 1 — Contracts, Tenancy, And Edge

- Versioned Gateway onboarding REST contracts, Identity validation/profile
  lookup protobuf contract, and RabbitMQ envelope/event/command types with
  message, correlation, idempotency, and tenant identifiers.
- Separate PostgreSQL service logins, EF contexts and initial migrations for
  Identity, Timesheet, Reporting, and Notification. Identity's tenant-owned
  entities fail closed under tenant query filters; direct service credentials
  cannot connect to another service database.
- Tenant creation and one-time invitation acceptance create pending memberships
  with Tenant Admin or Employee role. Identity requires a tenant administrator
  to issue an invitation. Phase 1's seven-day onboarding key is returned once
  at tenant creation, stored only as a hash, and authorizes a tenant-bound
  invitation route until Phase 2 replaces it with login-based authorization.
  It proves possession of the creation response, not ownership of the submitted
  admin email; the membership remains pending until activation.
  New tenant IDs originate in Identity;
  invitation tenant IDs come from the stored invitation hash, not request
  headers. Tenant-owned queries return nothing without an established scope.
- Gateway routes the four APIs, strips caller-supplied tenant/user headers,
  uses Identity gRPC validation for protected routes, and provides CORS, rate
  limits, one Scalar portal, and links to each service's OpenAPI document.
  Identity's Phase 1 validation service deliberately rejects tokens until the
  Phase 2 identity flow exists.
- `Aiyara.Phase1.Tests` covers tenant isolation, create/invite/accept rules,
  and gRPC/messaging contract shapes. A Compose-backed check creates two tenants,
  issues and accepts an invitation through Gateway, and rejects cross-tenant keys.
- Primary-agent validation on 6 October 2026: `dotnet build Aiyara.Timesheet.slnx`
  passed with 76 pre-existing warnings and no errors; `dotnet test
  Aiyara.Timesheet.slnx` passed 5/5; `docker compose --env-file .env.example
  -p aiyara-phase1-test config --quiet` passed; the full Compose stack passed
  `tests/platform/Smoke.ps1` (including the Redis failure/recovery check) and
  `tests/phase1/Smoke.ps1`; `git diff --check develop...HEAD` passed. The
  validation diff was reviewed for service ownership, tenant isolation,
  secrets, and build artifacts. Phase 2 owns token-backed request-scope
  resolution, activation, login, and email delivery.
- Phase 1 completion follow-up on 6 October 2026: the seed-only invitation
  fixture was replaced with the tenant-bound onboarding-key issuance API.
  `dotnet test Aiyara.Timesheet.slnx` now passes 7/7; the rebuilt Compose
  Identity/Gateway stack passes `tests/phase1/Smoke.ps1` for create → issue →
  accept, missing/cross-tenant keys, spoofed tenant headers, invitation replay,
  DB role isolation, protected routes, and OpenAPI discovery. Identity's
  onboarding-capability migration reports no pending model changes.

## Phase 2 — Identity And Notification

- Identity provides email activation, Argon2id password storage, tenant password
  policies, PASETO v4.public login, rotating refresh tokens, password reset and
  authenticated password change. Tenant creation and invitation acceptance
  automatically request activation email delivery; a newly bootstrapped
  Platform Admin does the same when Notification is ready.
- Gateway validates protected tokens through Identity gRPC and caches positive
  results in Redis by token ID and session version. Revocation and stricter
  policy changes invalidate sessions; the gRPC policy version prevents an
  in-flight pre-policy validation from being cached after the change. Redis
  limits credential attempts per
  tenant/account alongside the edge IP limit.
- Notification sends credential templates through local MailDev SMTP and stores
  delivery outcomes without recording the secret. Phase 2 unit tests pass
  11/11; Compose smoke covers registration, invitation, activation, login,
  refresh replay, logout, reset, policy migration, and rate limiting.
- Primary-agent validation on 6 October 2026: solution build passed with no
  errors (74 existing utility warnings); Phase 1/2 tests passed 18/18;
  Identity migrations have no pending model changes; Compose configuration,
  Phase 1 and Phase 2 smoke tests, container health, diff whitespace checks,
  and Obsidian links passed. The feature diff was reviewed against the roadmap
  and repository standards before merge into `develop`.

## Phase 3 — Timesheet Core (complete)

- Identity stores editable first/last name, HTTPS photo URL, and job title;
  its profile API verifies tenant membership and current session version.
- Timesheet owns tenant-filtered projects, categories, holidays, personal tasks,
  leave and time entries. Employees can edit only their own current-month data
  using the tenant timezone validated by Identity in both Gateway and Timesheet.
  Soft deletion, audit data, confirmed RabbitMQ month-change events, month
  locking, and immutable per-person month snapshots are implemented with
  migrations and Phase 3 unit tests.
- The Shell embeds Identity/Profile, Timesheet and Administration remotes.
  Thai/English controls, responsive time table, inline editing, task drag-in,
  leave management and tenant catalog/policy screens are implemented.
- Primary validation on 7 October 2026: solution tests 34/34, four changed
  frontend production builds, Compose configuration and all 17 service health
  checks, Phase 2 and Phase 3 Gateway smoke tests, direct Timesheet header
  spoof rejection, RabbitMQ delivery and outbox publish state, and
  `git diff --check` passed. Report-event consumption belongs to Phase 4.

## Not Yet Implemented

- Phase 4 has started with reporting data models and calendar rules; Quartz,
  RabbitMQ dispatch, PDF/XLSX generation, RustFS storage, signed upload, and
  Reports UI remain pending. Phase 3 time-entry snapshots are a prerequisite
  for meaningful generated reports.
- Approval workflows and reporting generation endpoints.
- Automated frontend end-to-end flows.
- Application clients for RabbitMQ and RustFS beyond dependency probes.
- Domain and end-to-end test suites for later phases.

## Agreed Direction

The target solution is now defined as a multi-tenant timesheet platform with a Docker Compose local runtime. See [[Target Architecture]], [[Security And Tenant Model]], and [[Development Roadmap]] for the agreed design and delivery order.

## Related

- [[Timesheet Project Index]]
- [[Architecture]]
- [[Services And Projects]]
- [[Target Architecture]]
- [[Development Roadmap]]
