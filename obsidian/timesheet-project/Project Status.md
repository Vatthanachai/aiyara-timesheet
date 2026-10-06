# Project Status

> Updated 6 October 2026. Phase 0 is complete; Phase 1 adds tenancy and the edge contracts described in [[Development Roadmap]].

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
  to issue an invitation; no public issue endpoint is exposed before Phase 2
  implements login and activation. New tenant IDs originate in Identity;
  invitation tenant IDs come from the stored invitation hash, not request
  headers. Tenant-owned queries return nothing without an established scope.
- Gateway routes the four APIs, strips caller-supplied tenant/user headers,
  uses Identity gRPC validation for protected routes, and provides CORS, rate
  limits, one Scalar portal, and links to each service's OpenAPI document.
  Identity's Phase 1 validation service deliberately rejects tokens until the
  Phase 2 identity flow exists.
- `Aiyara.Phase1.Tests` covers tenant isolation, create/invite/accept rules,
  and gRPC/messaging contract shapes. A Compose-backed check created a tenant
  and accepted a seeded invitation through Gateway without crossing tenants.
- Primary-agent validation on 6 October 2026: `dotnet build Aiyara.Timesheet.slnx`
  passed with 76 pre-existing warnings and no errors; `dotnet test
  Aiyara.Timesheet.slnx` passed 5/5; `docker compose --env-file .env.example
  -p aiyara-phase1-test config --quiet` passed; the full Compose stack passed
  `tests/platform/Smoke.ps1` (including the Redis failure/recovery check) and
  `tests/phase1/Smoke.ps1`; `git diff --check develop...HEAD` passed. The
  validation diff was reviewed for service ownership, tenant isolation,
  secrets, and build artifacts. Phase 2 owns authenticated request-scope
  resolution and the complete invitation/login flow.

## Not Yet Implemented

- Login, activation, token issuance/validation, and public authenticated
  invitation issuance (Phase 2).
- Time-entry, approval, and reporting domain models and endpoints.
- Frontend product screens, API client integration, and end-to-end flows.
- Frontend API integration and product workflows.
- Application clients for Redis, RabbitMQ, and RustFS beyond dependency probes.
- Domain and end-to-end test suites for later phases.

## Agreed Direction

The target solution is now defined as a multi-tenant timesheet platform with a Docker Compose local runtime. See [[Target Architecture]], [[Security And Tenant Model]], and [[Development Roadmap]] for the agreed design and delivery order.

## Related

- [[Timesheet Project Index]]
- [[Architecture]]
- [[Services And Projects]]
- [[Target Architecture]]
- [[Development Roadmap]]
