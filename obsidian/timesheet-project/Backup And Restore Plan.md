# Backup And Restore Plan

> Drafted and implemented 23 October 2026 on `feature/backup-restore`, item 12 of [[Product Backlog]]. Until now the only copy of the user's data was the Docker volumes `postgres-data` and `rustfs-data` of the main stack.

## Audit: What Existed On `develop`

| Capability | State |
|---|---|
| Data stores | PostgreSQL 17 (four databases, one per service, owned by per-service roles that `db-provisioner` creates from `.env`) and RustFS (S3-compatible; buckets `aiyara-reports` and `aiyara-profile-photos`, created by the services). Redis, RabbitMQ, Prometheus, and Grafana hold no system of record. |
| Backup | None. No script, no runbook text. |
| Test stack | `scripts/test-stack.ps1` gives a throwaway stack to rehearse on (see [[Test Stack Plan]]). |

## Decisions

1. **Two scripts and one helper:** `scripts/backup-main.ps1`, `scripts/restore-main.ps1`, and the dot-sourced `scripts/backup-common.ps1`. Both take `-Project` (default `aiyara-timesheet`, the main stack), so the same scripts are rehearsed on a test stack. Containers are found by Compose labels; credentials come from the containers' own environment. No `.env` or compose file is read.
2. **PostgreSQL:** databases are discovered with a query (everything that is not a template or `postgres`), dumped with `pg_dump -Fc` inside the container and copied out with `docker cp` (a PowerShell pipe would corrupt binary output). `pg_dumpall --globals-only --no-role-passwords` adds the roles; passwords are never stored and stay as `db-provisioner` set them.
3. **RustFS through the S3 API,** with a throwaway `minio/mc` container on the stack network (`mc mirror`). A copy of the `rustfs-data` volume was rejected: a running RustFS is not guaranteed consistent on disk, a restore would need the service stopped and the volume replaced, and it ties the backup to RustFS's on-disk format. The S3 mirror is logical, works while the stack runs, and restores into any S3-compatible store. Credentials reach the container through its environment, not the command line.
4. **Layout and manifest:** `backups/<yyyyMMdd-HHmmss>/` (git-ignored) with `postgres/`, `rustfs/<bucket>/`, and `manifest.json` (project, time, databases, buckets with object counts, SHA-256 and size of every file). The manifest is written last, so a folder without one is an interrupted backup and restore refuses it.
5. **Restore is destructive and gated:** it verifies every checksum before touching anything, prints a warning naming what is overwritten, refuses without `-Yes`, and `-DryRun` stops after verification. It stops the application containers (not postgres or rustfs), applies roles, runs `pg_restore --clean --if-exists --create` per database, mirrors each bucket with `--overwrite --remove` (so the bucket equals the backup), restarts the stopped containers, and waits until they are healthy.
6. **Not backed up:** `.env`. Without the same `PASETO_SIGNING_SEED` and `MFA_ENCRYPTION_KEY`, restored sessions and enrolled authenticators fail. The runbook says so.

## Phase A: Scripts

- [x] A1. `backup-main.ps1` with `-Project`, `-OutputRoot`, `-McImage`, `-DryRun`.
- [x] A2. `restore-main.ps1` with `-Backup` (default `latest`), `-Project`, `-Yes`, `-DryRun`, checksum verification, and a health wait.
- [x] A3. `backups/` added to `.gitignore`.
- [x] A4. Validated on a test stack: Smoke phase 2 as seed plus a marker object in `aiyara-reports`; backup; `reset` and `up` (empty databases); restore with `-Yes`; `refresh_sessions` went 14, 0, 14, the marker object came back, all databases are owned by their service roles, all containers healthy, and Smoke phase 2 passed again. Restore without `-Yes` refused; a tampered `globals.sql` was rejected by the checksum check.

## Phase B: Docs

- [x] B1. "Backup and restore" section in `docs/developer-runbook.md`: what is backed up, how to run, restore steps, verification, caveats.
- [x] B2. This note, Product Backlog, Project Status, and Index links.

## Known Limits

- Databases are dumped one after another, not as one snapshot; services writing between dumps can leave databases a few seconds apart.
- Object keys become file names under the backup folder; a key that is not a valid file name on the host would fail the backup. Object metadata beyond what `mc mirror` carries is not kept.
- Restore restarts containers with `docker start` and does not rebuild images or re-run `db-provisioner`.
- Never run against the main stack during development; it was validated on a test stack only.

## Out Of Scope

- Scheduled backups, retention and pruning, encryption of the backup folder, and off-machine copies.
- Backing up `.env`, Redis, RabbitMQ, Prometheus, or Grafana.
- Point-in-time recovery (WAL archiving).

## Related

- [[Product Backlog]]
- [[Project Status]]
- [[Test Stack Plan]]
