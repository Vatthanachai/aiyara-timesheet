SELECT format('CREATE ROLE identity_app LOGIN PASSWORD %L', :'identity_password')
WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'identity_app') \gexec
SELECT format('ALTER ROLE identity_app PASSWORD %L', :'identity_password') \gexec

SELECT format('CREATE ROLE timesheet_app LOGIN PASSWORD %L', :'timesheet_password')
WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'timesheet_app') \gexec
SELECT format('ALTER ROLE timesheet_app PASSWORD %L', :'timesheet_password') \gexec

SELECT format('CREATE ROLE reporting_app LOGIN PASSWORD %L', :'reporting_password')
WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'reporting_app') \gexec
SELECT format('ALTER ROLE reporting_app PASSWORD %L', :'reporting_password') \gexec

SELECT format('CREATE ROLE notification_app LOGIN PASSWORD %L', :'notification_password')
WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'notification_app') \gexec
SELECT format('ALTER ROLE notification_app PASSWORD %L', :'notification_password') \gexec

SELECT 'CREATE DATABASE identity_db OWNER identity_app'
WHERE NOT EXISTS (SELECT 1 FROM pg_database WHERE datname = 'identity_db') \gexec
SELECT 'CREATE DATABASE timesheet_db OWNER timesheet_app'
WHERE NOT EXISTS (SELECT 1 FROM pg_database WHERE datname = 'timesheet_db') \gexec
SELECT 'CREATE DATABASE reporting_db OWNER reporting_app'
WHERE NOT EXISTS (SELECT 1 FROM pg_database WHERE datname = 'reporting_db') \gexec
SELECT 'CREATE DATABASE notification_db OWNER notification_app'
WHERE NOT EXISTS (SELECT 1 FROM pg_database WHERE datname = 'notification_db') \gexec

ALTER DATABASE identity_db OWNER TO identity_app;
ALTER DATABASE timesheet_db OWNER TO timesheet_app;
ALTER DATABASE reporting_db OWNER TO reporting_app;
ALTER DATABASE notification_db OWNER TO notification_app;

REVOKE ALL ON DATABASE identity_db FROM PUBLIC;
REVOKE ALL ON DATABASE timesheet_db FROM PUBLIC;
REVOKE ALL ON DATABASE reporting_db FROM PUBLIC;
REVOKE ALL ON DATABASE notification_db FROM PUBLIC;

GRANT CONNECT, TEMPORARY ON DATABASE identity_db TO identity_app;
GRANT CONNECT, TEMPORARY ON DATABASE timesheet_db TO timesheet_app;
GRANT CONNECT, TEMPORARY ON DATABASE reporting_db TO reporting_app;
GRANT CONNECT, TEMPORARY ON DATABASE notification_db TO notification_app;
