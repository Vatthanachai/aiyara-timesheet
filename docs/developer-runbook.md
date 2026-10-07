# Developer Runbook

This runbook describes the local Docker Compose workflow for Aiyara Timesheet.
The root `readme.md` remains the quick-start page; this document covers startup,
migrations, validation, observability, and recovery steps.

## Prerequisites and secrets

- Docker Desktop with Compose v2, .NET SDK 10, PowerShell 7, and Bun.
- Copy `.env.example` to `.env`, then replace every `change-this-*` value with
  a local value. Keep `.env` out of Git and avoid pasting secret values into
  logs, issues, or screenshots.
- Check that the ports listed in `.env` are available. MailDev is optional;
  the default setup can use a MailDev instance already listening on SMTP
  `1025` and web `8080`.

## Start and verify the platform

From the repository root:

```powershell
docker compose config --quiet
docker compose up -d --build
docker compose ps --all
pwsh -NoProfile -File tests/platform/Smoke.ps1
```

The platform smoke test checks Compose configuration, database provisioning,
container health, backend readiness, and each API's OpenAPI endpoint. To have
the script build and start the stack, pass `-Start`. To exercise Identity's
unready behavior when Redis is unavailable, pass `-CheckFailure`; the script
restarts Redis when that check finishes.

The database provisioner creates a separate least-privilege role for each
service. PostgreSQL creates the service databases when its data volume is first
initialized. The four API processes apply their EF Core migrations on startup;
inspect their logs if migration fails:

```powershell
docker compose logs --tail 100 identities-api timesheet-api reports-api notifications-api
```

Do not remove a database volume to work around a credential mismatch. First
check the matching `*_DB_PASSWORD` values in `.env` and the provisioner logs.
A local database reset is destructive and should only be done when its data is
disposable and the exact Compose volume has been identified.

## Local application and API tools

- Frontend: `http://localhost:3000` by default (`FRONTEND_PORT` in `.env`).
- Gateway and consolidated Scalar portal: `http://localhost:8081/scalar` by default (`GATEWAY_API_PORT`).
- Grafana: `http://localhost:3001` by default (`GRAFANA_PORT`).
- Prometheus: `http://localhost:9090` by default (`PROMETHEUS_PORT`); the host binding is loopback-only.
- Jaeger traces: `http://localhost:16686` by default (`JAEGER_UI_PORT`); the local all-in-one backend keeps traces in memory.
- MailDev web UI: `http://localhost:8080` by default (`MAILDEV_WEB_PORT`) when an instance is running.

Scalar links to the Identity, Timesheet, Reporting, and Notification OpenAPI
documents. `/alive` reports process liveness; `/health` reports dependency
readiness. Only Gateway is published as a backend entry point. Reach other
backend services through Compose DNS from containers on the private network.

Grafana provisions the Prometheus datasource and **Aiyara Platform Overview**
dashboard. It includes backend readiness, HTTP rates and latency, runtime
memory, report-run statuses, authentication outcomes, the report RabbitMQ
queue, Quartz executions, and RustFS operations. The Phase 5 smoke test checks
the metric series, a trace received by Jaeger, and dashboard provisioning:

```powershell
pwsh -NoProfile -File tests/phase5/Smoke.ps1
```

## Test workflows

Run the focused end-to-end smoke tests with Compose running. Phase 1–4 smoke
tests use Gateway and MailDev; phase 4 runs a report to completion, stores it in
RustFS, tests signed upload/download, confirms the email notification, and
checks the persisted Notification delivery outcome. Pass `-ProjectName` to the
Phase 4 script when using a non-default Compose project.

```powershell
pwsh -NoProfile -File tests/phase1/Smoke.ps1
pwsh -NoProfile -File tests/phase2/Smoke.ps1
pwsh -NoProfile -File tests/phase3/Smoke.ps1
pwsh -NoProfile -File tests/phase4/Smoke.ps1
pwsh -NoProfile -File tests/phase5/Smoke.ps1
```

Run unit and HTTP integration tests, then build the frontend when its sources
change:

```powershell
dotnet test Aiyara.Timesheet.slnx --no-restore
bun install --cwd src/frontend/app
bun run --cwd src/frontend/app build
bun run --cwd src/frontend/app test:e2e:install
bun run --cwd src/frontend/app test:e2e
```

Playwright starts the Nuxt app on `127.0.0.1:3100` and stubs only the
authentication HTTP responses. These browser tests verify session persistence,
logout, and failed-login behavior without creating backend accounts.

The phase smoke scripts create test tenants, accounts, timesheets, and reports
in the configured local databases. Use disposable local data when repeating
the complete sequence.

## Troubleshooting

1. Check `docker compose ps --all` and `docker compose logs --tail 100 <service>`.
2. For an unhealthy API, inspect its `/health` response and logs for the named
   dependency. Verify service DNS and credentials from inside the Compose
   network; container-to-container traffic uses service names and container
   ports, not host-published ports.
3. If database provisioning exits unsuccessfully, inspect
   `docker compose logs db-provisioner` and confirm all four database passwords
   match the local configuration. Existing PostgreSQL volumes retain their
   initialized database state.
4. If Prometheus has no new series, check that the backend is healthy and can
   reach `http://prometheus:9090/api/v1/otlp/v1/metrics` on the Compose network.
   OTLP metric export and Prometheus scrape intervals can delay new samples.
5. If MailDev is unavailable, confirm an SMTP service is listening at the
   configured `SMTP_HOST` and `SMTP_PORT`, and that the test script's
   `-MailDevUrl` matches its web port. Avoid starting the optional MailDev
   profile when another instance owns those host ports.
6. If a messaging or gRPC contract changes, update the source in
   `src/Backend/contracts/Aiyara.Timesheet.Contracts` and its consuming
   producer/consumer tests before rebuilding.

## Safe handling of diagnostic output

Share service names, status codes, metric names, and exception types when
reporting an issue. Redact `.env` values, Authorization headers, PASETO and
refresh tokens, invitation/activation codes, email addresses, and report
payloads before copying logs or screenshots.
