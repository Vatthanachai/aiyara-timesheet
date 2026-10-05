# Architecture

## Runtime Topology

```text
Nuxt frontend (port 3000)
  ├── service reference ──> Identity API
  └── service reference ──> Timesheet API

Aspire AppHost
  ├── starts the frontend with Bun
  ├── starts the Identity and Timesheet APIs
  └── declares PostgreSQL + pgAdmin with a persistent data volume

Gateway API (standalone; not started by AppHost)
Report API  (standalone; not started by AppHost)
```

The AppHost passes the Identity and Timesheet APIs' HTTPS endpoint addresses to the frontend as `IDENTITY_URL` and `TIMESHEET_URL`, and waits for both APIs before starting it. Gateway and Report are included in the solution but are not AppHost project references, frontend dependencies, or declared service resources.

## APIs

All four APIs are ASP.NET Core Web projects with controllers, development-only OpenAPI, HTTPS redirection, and authorization middleware. Each currently exposes only the template `WeatherForecast` endpoint.

Identity and Timesheet additionally reference the shared `Aiyara.Timesheet.ServiceDefault` project, which configures:

- OpenTelemetry logging, metrics, and tracing; OTLP export is enabled only when `OTEL_EXPORTER_OTLP_ENDPOINT` is supplied.
- service discovery and resilient default HTTP clients;
- development-only `/health` readiness and `/alive` liveness endpoints.

Gateway and Report do not currently use the shared service defaults, so they do not receive its telemetry, service-discovery, HTTP-resilience, or health-endpoint configuration.

## Planned Infrastructure vs. Wired Infrastructure

The AppHost declares PostgreSQL and pgAdmin, but no API currently references the database resource or registers a database context. Its project file also includes Kafka, RabbitMQ, Redis, and Scalar Aspire packages, but `AppHost.cs` does not declare those resources.

## Related

- [[Timesheet Project Index]]
- [[Services And Projects]]
- [[Project Status]]
