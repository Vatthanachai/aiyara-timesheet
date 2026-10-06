# Project Status

> Updated 6 October 2026. The Phase 0 Compose baseline is tracked in [[Development Roadmap]].

## Phase 0 Platform Baseline

- The Compose topology includes backend APIs, Report Worker, frontend Shell and
  remotes, PostgreSQL, Redis, RabbitMQ, RustFS, optional MailDev, Prometheus,
  and Grafana. Every container has a health check.
- APIs and Report Worker share structured JSON logging, redaction support,
  OpenTelemetry instrumentation, liveness, and dependency reachability checks.
- AppHost represents Compose infrastructure as external resources and provides
  dependency endpoints to locally launched application projects. It also hosts
  the Scalar API reference.
- `tests/platform/Smoke.ps1` verifies the runnable Compose stack and API docs.
  TCP readiness probes establish reachability; authenticated protocol probes
  belong with the Phase 1 client integration.
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

## Not Yet Implemented

- Authentication/authorization behavior beyond `UseAuthorization()`.
- Identity, user, time-entry, approval, gateway, or reporting domain models and endpoints.
- Database registration, entity mappings, migrations, or API-to-PostgreSQL resource wiring.
- Frontend product screens, API client integration, and end-to-end flows.
- Gateway routing, frontend API integration, and product workflows.
- Application clients for PostgreSQL, Redis, RabbitMQ, and RustFS.
- Domain and end-to-end test suites for later phases.

## Agreed Direction

The target solution is now defined as a multi-tenant timesheet platform with a Docker Compose local runtime. See [[Target Architecture]], [[Security And Tenant Model]], and [[Development Roadmap]] for the agreed design and delivery order.

## Related

- [[Timesheet Project Index]]
- [[Architecture]]
- [[Services And Projects]]
- [[Target Architecture]]
- [[Development Roadmap]]
