#!/bin/sh
set -eu

# Each service owns one database. Application-specific roles and migrations are
# introduced alongside the corresponding service rather than shared here.
for database in identity_db timesheet_db reporting_db notification_db; do
  psql --username "$POSTGRES_USER" --dbname postgres --set ON_ERROR_STOP=1 \
    --command "CREATE DATABASE \"${database}\";"
done
