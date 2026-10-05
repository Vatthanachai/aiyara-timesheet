# Aiyara Timesheet

## Local infrastructure

Docker Compose is the source of truth for the local platform. It starts Gateway,
Identity, Timesheet, Report, and frontend containers alongside PostgreSQL (with
one database per service), Redis, RabbitMQ, and RustFS. MailDev and the
observability stack are optional profiles. The pinned RustFS image targets
x86-64 development hosts.

1. Copy `.env.example` to `.env` and replace every `change-this-*` value. Never
   commit `.env` or reuse development secrets outside a local machine.
2. Start the core stack with `docker compose up -d`.
3. If a local MailDev is not already running, add the MailDev profile:
   `docker compose --profile maildev up -d`.
4. Start Prometheus and Grafana when needed with:
   `docker compose --profile observability up -d`.

The frontend and APIs are reachable from the host through the ports set in `.env`.
Containers use the service DNS names (`postgres`, `redis`, `rabbitmq`, and
`rustfs`) on the `aiyara-timesheet` network. The current development MailDev
instance may remain external at `localhost:1025` (SMTP) and
`http://localhost:8080`; do not enable the `maildev` profile while it owns those
ports.

`infrastructure/postgres/init-databases.sh` creates `identity_db`,
`timesheet_db`, `reporting_db`, and `notification_db` on the first initialization
of the PostgreSQL volume. Remove the local `postgres-data` volume only when a
full local database reset is intentional.
