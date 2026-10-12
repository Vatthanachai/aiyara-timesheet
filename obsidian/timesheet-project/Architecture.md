# Architecture

> Refreshed 12 October 2026 against `develop` at `7d0b911`. Describes the current repository state; see [[Target Architecture]] for the agreed design this converges toward.

## Runtime Topology

```text
Docker Compose (source of truth for local infrastructure)
  ├── PostgreSQL (identity_db | timesheet_db | reporting_db | notification_db)
  ├── Redis, RabbitMQ, RustFS, Prometheus, Grafana, Jaeger
  └── MailDev (optional profile; the agreed local instance usually runs outside Compose)

Gateway API (public entry point; YARP reverse proxy + Identity gRPC token validation)
  ├── Identity API    ── identity_db
  ├── Timesheet API   ── timesheet_db
  ├── Report API      ── reporting_db
  └── Notification API ── notification_db

Report Worker (Quartz schedules, RabbitMQ consumers, PDF/XLSX generation, RustFS)

Nuxt Shell (port 3000)
  └── embeds four remotes as iframes with postMessage session hand-off:
      identity-profile, timesheet, reporting, administration

Aspire AppHost (developer-experience companion, not an infrastructure owner)
  ├── starts: Gateway, Identity, Timesheet, Report, Notification APIs,
  │           Report Worker, and the Nuxt Shell (via Bun)
  ├── registers Compose Postgres/Redis/RabbitMQ/RustFS as external resources
  │   and wires each API's Dependencies__* configuration to them
  └── aggregates all five APIs' OpenAPI documents behind one Scalar reference
```

AppHost does not start the four frontend remotes; Docker Compose is the only
place that runs the full topology end to end. AppHost requires Compose
infrastructure to already be running (see the readme's startup order) since it
only references those containers, never creates them.

## APIs

All five backend APIs (Gateway, Identity, Timesheet, Report, Notification) and
the Report Worker reference the shared `Aiyara.Timesheet.ServiceDefault`
project, which configures:

- OpenTelemetry logging, metrics, and tracing, exported over OTLP to
  Prometheus/Jaeger;
- service discovery and resilient default HTTP clients;
- `/health` readiness and `/alive` liveness endpoints.

Every business HTTP endpoint across all five APIs — including `/internal/*`
routes — is an ASP.NET Core MVC controller inheriting
`Aiyara.Timesheet.Component.Abstractions.Controllers.BaseController`. Each
action returns a `ServiceOkResponse<T>`, `ServiceErrorResponse`, or
`ServiceFileResponse` through `ReturnResponseWithHttpStatus`, and documents its
real success/error shapes with `[ProducesResponseType]`. The template
`WeatherForecast` controllers were removed during the full-controller
migration (`feature/full-api-controllers`). See [[Full Api Controllers Plan]].

Gateway additionally owns a YARP reverse proxy (routing to Identity, Timesheet,
Report, and Notification) and an `IdentityGrpcAuthenticationHandler` that
validates Bearer tokens against Identity's gRPC validation service before a
protected route is proxied.

## Infrastructure Ownership

Docker Compose owns all local infrastructure: PostgreSQL, Redis, RabbitMQ,
RustFS, Prometheus, Grafana, Jaeger, and (on the `maildev` profile) MailDev.
AppHost deliberately does not declare any of these as Aspire-managed
resources — it registers them as `AddExternalService` references so the
Aspire dashboard documents the topology without starting duplicate
containers, matching the target direction in [[Target Architecture]].

## Related

- [[Timesheet Project Index]]
- [[Services And Projects]]
- [[Project Status]]
- [[Full Api Controllers Plan]]
