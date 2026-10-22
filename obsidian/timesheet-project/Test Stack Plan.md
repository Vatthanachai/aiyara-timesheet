# Test Stack Plan

> Drafted and implemented 22 October 2026 on `feature/test-compose`. Validation (Smoke.ps1, and later Playwright against real services) no longer runs on the user's own Compose stack: every agent can bring up its own throwaway stack, and the main stack is rebuilt only after a change is merged into `develop`.

## Audit: What Existed On `develop`

| Capability | State |
|---|---|
| Compose stack | One `docker-compose.yml`, project `aiyara-timesheet`, fixed host ports (8081, 3000-3005, 5432, 6379, 5672, 9000), named volumes `postgres-data` and the rest. It is the user's running app and holds their data. |
| Smoke tests | `tests/phase2-4/Smoke.ps1` already take `-GatewayUrl` and `-MailDevUrl`, but defaulted to the main stack. They read mail from `.env`'s `MAILDEV_API_TOKEN`, which would point at the user's mail catcher. Phase 4 also runs `docker compose` against the main file for a database check. |
| Playwright | `src/Frontend/app/playwright.config.ts` starts its own dev servers on 3100-3105 and does not call Compose. |
| Parallel Agents rule | One agent held "the Docker slot" at a time, normally the validator. |

## Decisions

1. **`docker-compose.test.yml`** reuses every service of `docker-compose.yml` through `extends`, so the main file stays the single definition and is not edited. Overrides: loopback-only ports, `restart: "no"`, `OTEL_SDK_DISABLED`, MailDev always on, and the stack's own network and volumes.
2. **Own name, network, volumes, database.** The project name is `TEST_STACK_NAME` (default `aiyara-test-<6 hex>` from the worktree path). Compose prefixes the network and the four volumes with it, so the main `aiyara-timesheet_postgres-data` is never referenced. The script refuses the main name.
3. **Ports from one base,** `TEST_PORT_BASE` (default 20000 + a per-worktree multiple of 100, so several worktrees run side by side with no setup): gateway +1, MailDev web +2, Postgres +4, frontend +10, remotes +12 to +15. All bound to `127.0.0.1`. The script refuses a base that overlaps 3100-3105 and any host port already in use.
4. **Included:** Gateway, Identity, Timesheet, Report API and worker, Notification, Postgres, Redis, RabbitMQ, RustFS, MailDev (phases 1-4 need all of them). **Left out:** Prometheus, Jaeger, Grafana (only `tests/phase5` uses them). The Nuxt shell and remotes sit behind the `frontend` profile (`test-stack.ps1 up -Frontend`).
5. **`scripts/test-stack.ps1`** with `up` (build from this worktree, `--wait` for health, print URLs), `down` (keep volumes), `reset` (delete volumes), `status`, `smoke -Phase N`, and `env`. Secrets are random, written once to the git-ignored `.env.test`, and never taken from the main `.env`.
6. **Smoke scripts take the target from the environment.** `TIMESHEET_GATEWAY_URL` and `TIMESHEET_MAILDEV_URL` set the parameter defaults (unchanged default for the main stack); `TIMESHEET_ENV_FILE` stops `Resolve-MailDevToken` from reading the main `.env`; phase 4 also honours `TIMESHEET_PROJECT_NAME` and `TIMESHEET_COMPOSE_FILE` for its database check. `test-stack.ps1 smoke` sets all of them.
7. **`scripts/deploy-main.ps1`** is the only path to the main stack after validation. It refuses unless it runs in the main checkout (not a linked worktree), on `develop`, with a clean tree and a `.env`; it targets only `docker-compose.yml` as project `aiyara-timesheet`, never removes volumes, and never touches a test stack. `-Services` limits the rebuild, `-DryRun` checks only, and it asks for confirmation unless `-Yes`. Only the primary agent runs it.
8. **Process:** each agent may run its own test stack; the main compose is rebuilt only by the primary agent after merge into `develop` (see AGENTS.md).

## Phase A: Stack And Scripts

- [x] A1. `docker-compose.test.yml` with `extends`, ports, profiles, volumes.
- [x] A2. `scripts/test-stack.ps1` (up, down, reset, status, smoke, env) and `.env.test` ignored by git.
- [x] A3. Smoke scripts for phases 2-4 and `tests/MailInbox.ps1` accept the test stack through environment variables.
- [x] A4. `scripts/deploy-main.ps1` with branch, clean-tree, and main-checkout guards.
- [x] A5. Validated: both Compose files pass `config --quiet`; the stack came up healthy; phases 2, 3, and 4 smoke passed against it; main containers and volumes unchanged before and after.

## Phase B: Process And Docs

- [x] B1. AGENTS.md: validation gate item 5 and the Parallel Agents section.
- [x] B2. readme section, this note, Project Status, Product Backlog, and Index links.

## Known Limits

- `deploy-main.ps1` was dry-run only from a worktree (where it correctly refuses); it was not run in the main checkout, by design.
- The main stack's `db-provisioner` leaves an anonymous Postgres volume after each `down`; the test file keeps it off disk with `tmpfs`. The main file was not changed.
- The first `up` builds every image, which is slow; later builds use the Docker cache.
- Playwright still uses its own dev servers and mocks; pointing it at the test gateway (and `-Frontend`) is future work.

## Out Of Scope

- Continuous integration (backlog item 11, deferred).
- Running observability (`tests/phase5`) on the test stack.
- Changing the main stack's behaviour, ports, or volumes.

## Related

- [[Product Backlog]]
- [[Project Status]]
- [[Timesheet Approval Plan]]
