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
The frontend and Gateway are reachable from the host through the ports set in `.env`.
Containers use the service DNS names (`postgres`, `redis`, `rabbitmq`, and
`rustfs`) on the `aiyara-timesheet` network. The current development MailDev
instance may remain external at `localhost:1025` (SMTP) and
`http://localhost:8080`; do not enable the `maildev` profile while it owns those
ports. This is the agreed local exception to Compose ownership; verify it before
starting the platform with `curl http://localhost:8080`.

`infrastructure/postgres/init-databases.sh` creates `identity_db`,
`timesheet_db`, `reporting_db`, and `notification_db` on the first initialization
of the PostgreSQL volume. Remove the local `postgres-data` volume only when a
full local database reset is intentional.
