#!/bin/sh
set -eu

psql --host postgres --username "$POSTGRES_USER" --dbname postgres \
  --set ON_ERROR_STOP=1 \
  --variable identity_password="$IDENTITY_DB_PASSWORD" \
  --variable timesheet_password="$TIMESHEET_DB_PASSWORD" \
  --variable reporting_password="$REPORTING_DB_PASSWORD" \
  --variable notification_password="$NOTIFICATION_DB_PASSWORD" \
  --file /scripts/provision-service-roles.sql
