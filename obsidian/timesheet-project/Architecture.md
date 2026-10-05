# Architecture

## Runtime Topology

```text
Nuxt frontend (port 3000)
  ├── service reference ──> Identity API
  └── service reference ──> Timesheet API

Aspire AppHost
  ├── starts the frontend with Bun
  ├── starts both APIs
  └── declares PostgreSQL + pgAdmin with a persistent data volume
```

The AppHost passes the APIs' HTTPS endpoint addresses to the frontend as `IDENTITY_URL` and `TIMESHEET_URL`, and waits for both APIs before starting it.

## APIs

Both APIs are ASP.NET Core Web projects that use the shared `Aiyara.Timesheet.ServiceDefault` project. That shared project configures:

- OpenTelemetry logging, metrics, and tracing; OTLP export is enabled only when `OTEL_EXPORTER_OTLP_ENDPOINT` is supplied.
- service discovery and resilient default HTTP clients;
- development-only `/health` readiness and `/alive` liveness endpoints.

Each API exposes controllers, OpenAPI in Development, HTTPS redirection, and authorization middleware. At this snapshot, both only provide the template `WeatherForecast` endpoint; no identity or timesheet domain endpoints are implemented.

## Planned Infrastructure vs. Wired Infrastructure

The AppHost declares PostgreSQL and pgAdmin, but neither API currently references the database resource or registers a database context. Its project file also includes Kafka, RabbitMQ, Redis, and Scalar Aspire packages, but `AppHost.cs` does not declare those resources.

## Related

- [[Timesheet Project Index]]
- [[Services And Projects]]
- [[Project Status]]
