# Aiyara Timesheet

## Local infrastructure

Docker Compose is the source of truth for the local platform. It starts Gateway,
Identity, Timesheet, Report, Notification, Report Worker, frontend, Prometheus,
and Grafana containers alongside PostgreSQL (with one database per service),
Redis, RabbitMQ, and RustFS. Only Gateway is published as a backend entry point;
the remaining backend services are reachable solely on the Compose network.
MailDev stays an optional profile because the agreed local MailDev instance
already owns its host ports. The pinned RustFS image targets x86-64 development
hosts.

1. Copy `.env.example` to `.env` and replace every `change-this-*` value. Never
   commit `.env` or reuse development secrets outside a local machine.
2. Start the core stack with `docker compose up -d`.
3. If a local MailDev is not already running, add the MailDev profile:
   `docker compose --profile maildev up -d`.
4. Run `pwsh -File tests/platform/Smoke.ps1` to verify every core container,
   each backend readiness endpoint, and each API's OpenAPI document. Add
   `-Start` to build and start the stack before checking it.
   Add `-CheckFailure` to verify that Identity remains alive but becomes
   unready when Redis is stopped; the test restarts Redis afterward.
The frontend and Gateway are reachable from the host through the ports set in `.env`.
Containers use the service DNS names (`postgres`, `redis`, `rabbitmq`, and
`rustfs`) on the `aiyara-timesheet` network. The current development MailDev
instance may remain external at `localhost:1025` (SMTP) and
`http://localhost:8080`; do not enable the `maildev` profile while it owns those
ports. This is the agreed local exception to Compose ownership; verify it before
starting the platform with `curl http://localhost:8080`.

`infrastructure/postgres/init-databases.sh` creates `identity_db`,
`timesheet_db`, `reporting_db`, and `notification_db` on the first initialization
of the PostgreSQL volume. The one-shot `db-provisioner` creates a separate
least-privilege login for each service and updates its password from the ignored
`.env` file on startup. Set all four `*_DB_PASSWORD` values before starting.
Remove the local `postgres-data` volume only when a full local database reset
is intentional.

Backend `/alive` checks only the process. Backend `/health` checks configured
dependencies; the four APIs with PostgreSQL databases also authenticate and
check database connectivity. Their EF migrations run at API startup.
When launching a backend directly, dependency hosts default to `localhost` and
their standard ports. Override `Dependencies__<Name>__Host` and
`Dependencies__<Name>__Port` if the dependencies use different endpoints.

The Aspire AppHost starts application projects for debugging and displays the
Compose infrastructure as external resources. Start Compose infrastructure
first when using AppHost. If Compose host ports differ from their defaults,
set `POSTGRES_PORT`, `REDIS_PORT`, `RABBITMQ_AMQP_PORT`, and `RUSTFS_API_PORT`
in the AppHost process environment to match `.env`.
Set `IDENTITY_DB_PASSWORD`, `TIMESHEET_DB_PASSWORD`, `REPORTING_DB_PASSWORD`,
and `NOTIFICATION_DB_PASSWORD` in that environment too.

## Phase 1 API and tenancy

Gateway is the public backend entry point. `POST /api/v1/tenants` accepts
`{"name":"Team","slug":"team","adminEmail":"admin@example.test"}` and returns
the tenant, account, and pending Tenant Admin membership IDs. A slug must be
unique. `POST /api/v1/invitations/accept` accepts `{"code":"..."}` and returns a
pending membership; codes expire after seven days and are single-use. The
Identity service stores only a SHA-256 hash of each invitation code. Invitation
issuance is limited to a tenant administrator in the Identity service. A
public, authenticated invitation-issuance route and account activation are
intentionally deferred until Phase 2 supplies login/token validation and email
delivery; no unauthenticated issuance route is exposed.

Gateway publishes the consolidated development Scalar portal at `/scalar`.
Its `/api-docs/{identity|timesheet|reporting|notification}/openapi/v1.json`
routes link to each service's OpenAPI document. Protected `/api/v1/{service}`
proxy routes require Identity gRPC token validation, strip untrusted tenant/user
headers, and inject the validated tenant ID. The Phase 1 Identity validation
implementation rejects all tokens until Phase 2 supplies real sessions. CORS
origin comes from `FRONTEND_ORIGIN`; onboarding requests are IP rate limited.

Run `dotnet test tests/Aiyara.Phase1.Tests/Aiyara.Phase1.Tests.csproj` for
tenant-isolation, onboarding, gRPC, and messaging contract tests. With Compose
running, `pwsh -File tests/phase1/Smoke.ps1` checks both onboarding routes,
single-use invitations, DB role isolation, service health, API docs, and the
protected-route authorization boundary. It creates only uniquely named test
tenants and an invitation fixture in Identity's own database.
