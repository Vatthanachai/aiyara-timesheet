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
issuance is available at `POST /api/v1/tenants/{tenantId}/invitations` with an
`X-Onboarding-Key` header. Tenant creation returns this seven-day, tenant-bound
key once; keep it private. Identity stores only its hash, verifies that it
belongs to the creating Tenant Admin, and never trusts a caller-supplied tenant
header. A missing, expired, or cross-tenant key receives 401. This bootstrap
capability is for Phase 1 onboarding; Phase 2 replaces it with login-based
Tenant Admin authorization. Once the creating administrator activates their
account, Identity revokes this key. The key proves possession of the
tenant-creation response, not ownership of `adminEmail`; a pending membership
cannot use normal Tenant Admin access before email activation.

Gateway publishes the consolidated development Scalar portal at `/scalar`.
Its `/api-docs/{identity|timesheet|reporting|notification}/openapi/v1.json`
routes link to each service's OpenAPI document. Protected `/api/v1/{service}`
proxy routes require Identity gRPC token validation, strip untrusted tenant/user
headers, and inject the validated tenant ID. Positive validation responses are
cached in Redis for at most one minute; Identity publishes account/session and
tenant-policy revocations. CORS origin comes from `FRONTEND_ORIGIN`; onboarding
and authentication requests are IP rate limited. Login and credential-email
requests also have per-tenant-account limits in Redis (without storing email
addresses in limiter keys).
For onboarding, the new tenant ID is generated by Identity and an invitation's
tenant ID is resolved from the stored hash, never from a caller-supplied header.
Tenant-owned Identity queries fail closed without an explicitly established
`TenantScope`. Authenticated request scopes resolve only from validated
principal claims; the bootstrap invitation route establishes its scope from the
verified onboarding key. An activated Tenant Admin issues invitations with a
Bearer token at `POST /api/v1/auth/tenants/{tenantId}/invitations`.

Run `dotnet test tests/Aiyara.Phase1.Tests/Aiyara.Phase1.Tests.csproj` for
tenant-isolation, onboarding, gRPC, and messaging contract tests. With Compose
running, `pwsh -File tests/phase1/Smoke.ps1` checks both onboarding routes,
single-use invitations, DB role isolation, service health, API docs, and the
protected-route authorization boundary. It creates uniquely named test tenants
and issues an invitation through Gateway; no database fixture is required.

## Phase 2 identity and local email

Identity exposes activation, login, refresh, logout, forgot-password, reset,
authenticated password change,
and tenant password-policy endpoints under `/api/v1/auth` through Gateway.
Tenant creation and invitation acceptance automatically send a one-time
temporary password. `POST /api/v1/auth/activation/request` accepts a tenant ID
and email to retry delivery. The temporary password expires after 24 hours; a
replacement invalidates the previous one. Complete activation with
`POST /api/v1/auth/activate` and `{"code":"...","password":"..."}`.
The reset request and completion endpoints are `/password/forgot` and
`/password/reset`. Login uses `{"tenantId":"...","email":"...","password":"..."}`;
it returns a 15-minute PASETO v4.public access token and a rotating opaque
refresh token. `PUT /api/v1/auth/tenants/{tenantId}/password-policy` requires
an active Tenant Admin Bearer token. A stricter policy forces a password change
before protected APIs accept new access tokens. A restricted login token can
call `POST /api/v1/auth/password/change` with
`{"currentPassword":"...","newPassword":"..."}`; this revokes prior sessions.
Forgot-password reset remains available if the current password is unknown.

For Compose, set `PASETO_SIGNING_SEED` to a base64-encoded random 32-byte seed
and `NOTIFICATION_INTERNAL_KEY` to an independent random value of at least 32
characters in an untracked `.env`; see `.env.example`. Keep the signing seed
stable across restarts to preserve sessions. Start the local SMTP inbox with
`docker compose --profile maildev up -d`; its web UI is on
`MAILDEV_WEB_PORT` (default 8080). If another SMTP test server is used, set
`SMTP_HOST`, `SMTP_PORT`, `SMTP_USE_TLS`, and optional `SMTP_USERNAME` /
`SMTP_PASSWORD`. Notification stores delivery outcome, recipient and template,
but not message body or temporary password. Optional
`BOOTSTRAP_PLATFORM_ADMIN_EMAIL` provisions a pending Platform Admin in tenant
`00000000-0000-0000-0000-000000000001`; its initial activation email is sent
automatically once Notification is healthy. If delivery is unavailable, the
same activation-request endpoint can retry; restarting Identity while the
Platform Admin is still pending issues and emails a replacement challenge.
No default Platform Admin
credential exists.

Run `dotnet test tests/Aiyara.Phase2.Tests/Aiyara.Phase2.Tests.csproj` for
password, token, activation, session, policy and cross-tenant revocation tests.
With Compose and MailDev running, `pwsh -File tests/phase2/Smoke.ps1` exercises
registration, invited-member activation, email delivery, login, refresh-token
replay, logout, reset, and forced policy migration through Gateway. Its MailDev
URL defaults to `http://127.0.0.1:1081` to avoid the common local 8080 conflict;
pass `-MailDevUrl http://127.0.0.1:8080` for the default Compose profile.
Pass `-BootstrapEmail address@example.test` to verify the optional Platform
Admin bootstrap message without displaying its one-time password.

## Phase 3 timesheet core

The Shell on `FRONTEND_PORT` provides Thai/English navigation and a session-based
login. Identity/Profile, Timesheet, and Administration run as separate remotes;
the Shell embeds them and sends the active access token using an exact-origin
`postMessage`. Configure the public ports in `.env` and use the Shell rather than
opening a remote directly. The Gateway accepts requests from these remote
origins. Access and refresh tokens are held in the Shell tab's session storage.

The Timesheet API is reached through Gateway at `/api/v1/timesheets`. It exposes
`/context`, `/catalog`, `/projects`, `/categories`, `/holidays`, `/tasks`,
`/entries`, `/leave`, and `/months/{year}/{month}/lock` and `.../snapshot`.
Gateway validates the Bearer token; Timesheet independently validates it with
Identity gRPC before establishing the tenant, account, role and timezone scope.
Project/category/holiday changes and month locking
require Tenant Admin; employees can edit only their own entries and leave in
the current tenant-local month. Past months and locked months are read only.
An end time earlier than the start time records an overnight entry. Entries,
leave, tasks and tenant catalog items are soft deleted. Audit records store the
actor and before/after data. Month locks create immutable per-employee source
snapshots and a durable Timesheet outbox event. A background publisher sends
confirmed, persistent events to the durable `reporting.timesheet-events.v1`
RabbitMQ queue, retrying unsent rows. Consumers must use the event ID for
idempotency. Phase 4 will consume those snapshots for generated reports.

`GET/PUT /api/v1/identity/profile/me` manages first and last name, job title
and an HTTPS photo URL. The admin remote also updates the existing tenant
password policy endpoint. Run `dotnet test tests/Aiyara.Phase3.Tests/Aiyara.Phase3.Tests.csproj`
for tenancy, duration, timezone and immutability checks. Production frontend
builds use `bun run build` in the Shell and each changed remote.
Run `pwsh -NoProfile -File tests/phase3/Smoke.ps1` against the Compose stack
with MailDev enabled to exercise profile, tenant catalog, time entry, leave,
access control, and month locking through Gateway.

## Phase 4 reporting (complete)

Reporting now exposes tenant-authorized definition, run, schedule, retention,
download, and signed-PDF upload APIs through Gateway. The worker consumes
durable RabbitMQ commands and month-lock events, generates PDF/XLSX from
Timesheet's immutable locked-month snapshots, and stores versioned objects in
RustFS. The monthly PDF uses Noto Sans Thai and includes work-hour, worked-day,
overtime-day, leave, and signature summaries. Identity gRPC supplies employee
names; the signed PDF versions and generated source snapshots are audited and
purged under tenant retention policy. Successful employee reports trigger a
report-ready email through Notifications, which records delivery outcomes.
Persistent schedule rows are dispatched by Quartz jobs in tenant-local time.
The Thai/English Reports remote supports
employee history/download and admin definitions, schedules, retention, and
signed uploads.

Run `dotnet test tests/Aiyara.Phase4.Tests/Aiyara.Phase4.Tests.csproj` for
calendar, tenant-isolation, snapshot, document-renderer, authenticated HTTP
report-request/signed-upload/download, audit, and notification retry checks.
Run `pwsh -File tests/phase4/Smoke.ps1` against the Compose stack with MailDev
enabled to exercise a real Identity-issued token, locked Timesheet snapshot,
worker PDF generation into RustFS, signed upload/download, and report-ready email.
