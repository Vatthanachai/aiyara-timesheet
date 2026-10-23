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

### Running an API with `dotnet run`

Each API's `.http` file targets its `http` launch profile (Identity 5027,
Gateway 5255, Report 5160, Timesheet 5198). Identity, Report, and Timesheet stop
at startup without their connection strings and keys (for example
`Mfa:EncryptionKey` or `ConnectionStrings:ReportingDb`), and Gateway reports
`/health` 503 until it can reach Identity gRPC. `tests/local/HttpFiles.ps1`
starts each API that way with the settings Compose gives its container, mapped
to the host's published PostgreSQL, Redis, RabbitMQ, and RustFS ports, keeps
Identity running for the others' gRPC (`-IdentityGrpcPort`, default 18082), and
calls every request in the four `.http` files. Run it from the repository root
with Compose up and the solution built; logs go to `aiyara-http-files` in the
temp directory:

```powershell
dotnet build Aiyara.Timesheet.slnx
pwsh -NoProfile -File tests/local/HttpFiles.ps1
```

The local Identity shares the Compose databases but skips the Platform Admin
bootstrap. Stop any API you started from an IDE on those ports first.

## Test workflows

Run the focused end-to-end smoke tests with Compose running. Phase 1–4 smoke
tests use Gateway and MailDev; phase 4 runs a report to completion, stores it in
RustFS, tests signed upload/download, confirms the email notification, and
checks the persisted Notification delivery outcome. Pass `-ProjectName` to the
Phase 4 script when using a non-default Compose project.

Phase 2–4 read the delivered email from the mail catcher's web port
(`-MailDevUrl`, default `http://127.0.0.1:8080`) through `tests/MailInbox.ps1`:

- MailDev (this repo's `maildev` profile) needs nothing more; the scripts read
  its open `/email` list.
- aiyara-maildev requires an API token for `/api/v1/emails`. Create one on its
  API Tokens page and set `MAILDEV_API_TOKEN` in `.env` (or pass
  `-MailDevToken`, or set the `MAILDEV_API_TOKEN` environment variable). Its SMTP
  user credentials do not work for the REST API.

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

### Real-stack browser tests

A second Playwright project (`src/frontend/app/playwright.real.config.ts`, specs in
`tests/e2e-real`) drives the built Shell and remotes in Chromium against a throwaway test
stack instead of mocked APIs. It covers: a Platform Admin signing in with an authenticator and
creating an organization, its administrator activating from the MailDev email and signing in,
a Tenant Admin adding a project and an employee recording time against it (with the signed-in
device listed), bulk invitations with the audit log, and a member enrolling in MFA at sign-in.

```powershell
pwsh -NoProfile -File scripts/test-e2e-real.ps1             # reset, up, run, tear down
pwsh -NoProfile -File scripts/test-e2e-real.ps1 -Repeat 3   # three runs on one stack
pwsh -NoProfile -File scripts/test-e2e-real.ps1 -Spec member-mfa -KeepStack
```

The wrapper resets and starts the test stack with `-Frontend` (its own project name, volumes,
and loopback ports from `TEST_PORT_BASE`), sets `BOOTSTRAP_PLATFORM_ADMIN_EMAIL` so the stack
has a pending Platform Admin, runs `bun run test:e2e:real` with `E2E_REAL_BASE_URL`,
`E2E_REAL_GATEWAY_URL`, `E2E_REAL_MAILDEV_URL`, and `E2E_REAL_PLATFORM_ADMIN_EMAIL` set from
the stack, and resets the stack again unless `-KeepStack` is given. It starts no dev servers
and uses none of ports 3100-3105, so it can run beside the mocked suite; it never touches the
main stack. Reuse a kept stack with `-SkipReset`; the Platform Admin's authenticator secret is
saved in the git-ignored `tests/e2e-real/.state`, which the wrapper clears on a reset.

To run the specs against a stack you started by hand, set the four variables
(`scripts/test-stack.ps1 env -Frontend` prints the ports; open the Shell as `localhost`) and run
`bun run --cwd src/frontend/app test:e2e:real`. Each spec creates its own organization with
unique names, so specs and repeated runs do not depend on one another.

### Installable app and offline Timesheet

The Shell and the Timesheet remote ship a service worker (`@vite-pwa/nuxt`), and the Shell a web app manifest.
They exist only in a production build, so `nuxt dev` shows neither. Playwright builds production copies of the Shell
(`127.0.0.1:3106`) and the Timesheet remote (`127.0.0.1:3107`) for `tests/e2e/pwa.spec.ts`; the first run of the suite
therefore takes a minute longer. To try the app by hand, build and start both (`bun run build` then
`bun .output/server/index.mjs` with `NITRO_PORT` set), open the Shell in Chrome, reload once so the worker controls the
page, then use DevTools → Network → Offline.

`sw.js` and `manifest.webmanifest` are served `Cache-Control: no-cache` by Nitro route rules; hashed `_nuxt` files keep
their long cache. A new version waits until the page is reloaded (`registerType: 'prompt'`): the Shell shows a banner.
What the device keeps for offline use (IndexedDB `aiyara-timesheet-offline` on the Timesheet remote's origin) is deleted
at sign-out; to inspect or remove it by hand use DevTools → Application → IndexedDB. See the Mobile And Offline Plan note
in the Obsidian vault.

## Backup and restore

`scripts/backup-main.ps1` and `scripts/restore-main.ps1` back up and restore the PostgreSQL
databases and the RustFS buckets of a Compose stack. They default to the main stack (project
`aiyara-timesheet`); `-Project <name>` points them at a test stack instead
(`aiyara-test-xxxxxx`, see `scripts/test-stack.ps1 status`). They find the containers by their
Compose labels and take credentials from the containers' own environment, so they read no
`.env` and need no compose file.

### What is backed up

- Every application database in the `postgres` container (found by query, not hard-coded:
  today `identity_db`, `notification_db`, `reporting_db`, `timesheet_db`), one `pg_dump` custom
  format file each, plus `globals.sql` (roles, without passwords).
- Every RustFS bucket (`aiyara-reports`, and `aiyara-profile-photos` once a photo exists),
  copied object by object through the S3 API with a throwaway `minio/mc` container on the stack
  network. This was chosen over copying the `rustfs-data` volume because a volume copy of a
  running RustFS is not guaranteed consistent and depends on RustFS's on-disk format.
- `manifest.json` with the project, time, databases, buckets, and a SHA-256 of every file.

Not backed up: `.env` (database passwords, PASETO signing seed, MFA encryption key, internal
keys). Keep a copy somewhere safe. Without the same `PASETO_SIGNING_SEED` and
`MFA_ENCRYPTION_KEY`, restored sessions and enrolled authenticators do not work. Redis
(session cache, rate limits), RabbitMQ queues, Prometheus, and Grafana are also not backed up;
none holds the system of record.

### Back up

```powershell
pwsh -NoProfile -File scripts/backup-main.ps1 -DryRun   # list databases and buckets, write nothing
pwsh -NoProfile -File scripts/backup-main.ps1
```

The stack keeps running; the script only reads. Output goes to the git-ignored
`backups/<yyyyMMdd-HHmmss>/` (`-OutputRoot` changes the parent folder). Databases are dumped one
after another, so run it when the application is quiet if you need a cross-service snapshot.
Copy the folder off the machine; a backup next to the volume it protects does not survive
losing the disk.

### Restore

Restore overwrites data: every database in the backup is dropped and recreated, and every
bucket is made identical to the backup (objects added since are deleted).

1. Bring the stack up (`docker compose up -d`) with the `.env` the backup was taken under. A
   fresh volume is fine: the empty databases are replaced.
2. Check the backup and see the plan, without changing anything:

   ```powershell
   pwsh -NoProfile -File scripts/restore-main.ps1 -Backup backups/20261023-120000 -DryRun
   ```

3. Restore (`latest` is the default for `-Backup`). It refuses without `-Yes`:

   ```powershell
   pwsh -NoProfile -File scripts/restore-main.ps1 -Backup backups/20261023-120000 -Yes
   ```

   The script verifies every checksum first, stops the application containers (not postgres or
   rustfs), restores roles, databases, and buckets, starts the containers again, and waits
   until they are healthy.

### Verify

- The script ends with "Restore complete; N application containers are running and healthy."
- `docker compose ps` shows every service healthy and the gateway answers on
  `http://localhost:8081/health`.
- Sign in and open a known timesheet month and a previously exported report.
- Optional spot check:
  `docker compose exec postgres psql -U $POSTGRES_USER -d identity_db -c "select count(*) from refresh_sessions"`.

### Caveats

- Rehearse on a test stack first: `scripts/test-stack.ps1 up`, `smoke -Phase 2` to seed, back up
  with `-Project`, `reset`, `up`, restore with `-Yes`, then `reset`. Never test against the main
  stack.
- Roles are only created if missing; passwords stay as `db-provisioner` set them from `.env`.
- The restore restarts the application containers with `docker start`; it does not rebuild
  images. After restoring an old backup onto newer code, the services apply any newer
  migrations on start.
- Object keys are written to the backup folder as files, so a key that is not a valid file name
  on the host file system (for example containing `:` on Windows) would fail the backup.
  Current keys are plain paths.
- Backups contain real data (members, hours, reports); protect and delete them accordingly.

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
   profile when another instance owns those host ports (`docker ps` shows
   what is actually bound). If `tests/phase2/3/4/Smoke.ps1` throws
   "did not return MailDev's email list API" or "did not return valid JSON",
   either the catcher is aiyara-maildev and no `MAILDEV_API_TOKEN` is set, or
   something else answered on the configured port — set the token, pass the
   correct `-MailDevUrl`, or stop the conflicting service, rather than
   re-running the same command. "rejected the API token" means the token was
   revoked or mistyped.
6. If a messaging or gRPC contract changes, update the source in
   `src/Backend/contracts/Aiyara.Timesheet.Contracts` and its consuming
   producer/consumer tests before rebuilding.

## Safe handling of diagnostic output

Share service names, status codes, metric names, and exception types when
reporting an issue. Redact `.env` values, Authorization headers, PASETO and
refresh tokens, invitation/activation codes, email addresses, and report
payloads before copying logs or screenshots.
