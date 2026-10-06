using Scalar.Aspire;

var builder = DistributedApplication.CreateBuilder(args);

// Compose remains the single owner of local infrastructure. Register its
// endpoints as external resources so the Aspire dashboard documents the
// development topology without starting duplicate containers.
var postgresPort = builder.Configuration["POSTGRES_PORT"] ?? "5432";
var redisPort = builder.Configuration["REDIS_PORT"] ?? "6379";
var rabbitMqPort = builder.Configuration["RABBITMQ_AMQP_PORT"] ?? "5672";
var rustFsPort = builder.Configuration["RUSTFS_API_PORT"] ?? "9000";

var identityDbPassword = builder.AddParameterFromConfiguration(
    "identity-db-password", "IDENTITY_DB_PASSWORD", secret: true);
var timesheetDbPassword = builder.AddParameterFromConfiguration(
    "timesheet-db-password", "TIMESHEET_DB_PASSWORD", secret: true);
var reportingDbPassword = builder.AddParameterFromConfiguration(
    "reporting-db-password", "REPORTING_DB_PASSWORD", secret: true);
var notificationDbPassword = builder.AddParameterFromConfiguration(
    "notification-db-password", "NOTIFICATION_DB_PASSWORD", secret: true);

var identityDb = builder.AddConnectionString("IdentityDb", ReferenceExpression.Create(
    $"Host=localhost;Port={postgresPort};Database=identity_db;Username=identity_app;Password={identityDbPassword}"));
var timesheetDb = builder.AddConnectionString("TimesheetDb", ReferenceExpression.Create(
    $"Host=localhost;Port={postgresPort};Database=timesheet_db;Username=timesheet_app;Password={timesheetDbPassword}"));
var reportingDb = builder.AddConnectionString("ReportingDb", ReferenceExpression.Create(
    $"Host=localhost;Port={postgresPort};Database=reporting_db;Username=reporting_app;Password={reportingDbPassword}"));
var notificationDb = builder.AddConnectionString("NotificationDb", ReferenceExpression.Create(
    $"Host=localhost;Port={postgresPort};Database=notification_db;Username=notification_app;Password={notificationDbPassword}"));

var composePostgres = builder.AddExternalService("compose-postgres", $"tcp://localhost:{postgresPort}");
var composeRedis = builder.AddExternalService("compose-redis", $"tcp://localhost:{redisPort}");
var composeRabbitMq = builder.AddExternalService("compose-rabbitmq", $"amqp://localhost:{rabbitMqPort}");
var composeRustFs = builder.AddExternalService("compose-rustfs", $"http://localhost:{rustFsPort}")
    .WithHttpHealthCheck("/health");

var gatewayApi = builder.AddProject<Projects.Aiyara_Gateways_Api>("aiyara-gateways-api");

var identityApi = builder.AddProject<Projects.Aiyara_Identities_Api>("aiyara-identities-api")
    .WithReference(composePostgres)
    .WithReference(composeRedis)
    .WithReference(identityDb)
    .WithEnvironment("Dependencies__Postgres__Host", "localhost")
    .WithEnvironment("Dependencies__Postgres__Port", postgresPort)
    .WithEnvironment("Dependencies__Redis__Host", "localhost")
    .WithEnvironment("Dependencies__Redis__Port", redisPort);

var timesheetApi = builder.AddProject<Projects.Aiyara_Timesheet_Api>("aiyara-timesheet-api")
    .WithReference(composePostgres)
    .WithReference(composeRedis)
    .WithReference(timesheetDb)
    .WithEnvironment("Dependencies__Postgres__Host", "localhost")
    .WithEnvironment("Dependencies__Postgres__Port", postgresPort)
    .WithEnvironment("Dependencies__Redis__Host", "localhost")
    .WithEnvironment("Dependencies__Redis__Port", redisPort);

var reportApi = builder.AddProject<Projects.Aiyara_Report_Api>("aiyara-report-api")
    .WithReference(composePostgres)
    .WithReference(reportingDb)
    .WithEnvironment("Dependencies__Postgres__Host", "localhost")
    .WithEnvironment("Dependencies__Postgres__Port", postgresPort);

var notificationApi = builder.AddProject<Projects.Aiyara_Notifications_Api>("aiyara-notifications-api")
    .WithReference(composeRabbitMq)
    .WithReference(composePostgres)
    .WithReference(notificationDb)
    .WithEnvironment("Dependencies__RabbitMq__Host", "localhost")
    .WithEnvironment("Dependencies__RabbitMq__Port", rabbitMqPort)
    .WithEnvironment("Dependencies__Postgres__Host", "localhost")
    .WithEnvironment("Dependencies__Postgres__Port", postgresPort);

var reportWorker = builder.AddProject<Projects.Aiyara_Report_Worker>("aiyara-report-worker")
    .WithReference(composeRabbitMq)
    .WithReference(composeRustFs)
    .WithEnvironment("Dependencies__RabbitMq__Host", "localhost")
    .WithEnvironment("Dependencies__RabbitMq__Port", rabbitMqPort)
    .WithEnvironment("Dependencies__RustFs__Host", "localhost")
    .WithEnvironment("Dependencies__RustFs__Port", rustFsPort);

gatewayApi
    .WithReference(identityApi)
    .WithReference(timesheetApi)
    .WithReference(reportApi)
    .WithReference(notificationApi)
    .WithEnvironment("Services__Identity__BaseUrl", identityApi.GetEndpoint("http"))
    .WithEnvironment("IdentityGrpc__Url", identityApi.GetEndpoint("https"))
    .WithEnvironment("ReverseProxy__Clusters__identity__Destinations__primary__Address",
        ReferenceExpression.Create($"{identityApi.GetEndpoint("http")}/"))
    .WithEnvironment("ReverseProxy__Clusters__timesheet__Destinations__primary__Address",
        ReferenceExpression.Create($"{timesheetApi.GetEndpoint("http")}/"))
    .WithEnvironment("ReverseProxy__Clusters__reporting__Destinations__primary__Address",
        ReferenceExpression.Create($"{reportApi.GetEndpoint("http")}/"))
    .WithEnvironment("ReverseProxy__Clusters__notification__Destinations__primary__Address",
        ReferenceExpression.Create($"{notificationApi.GetEndpoint("http")}/"));

// Docker Compose owns shared development infrastructure. Keep AppHost focused on
// starting/debugging application projects and exposing their development API docs.
builder.AddScalarApiReference("api-reference", options => options.AllowSelfSignedCertificates())
    .WithApiReference(gatewayApi, "https")
    .WithApiReference(identityApi, "https")
    .WithApiReference(timesheetApi, "https")
    .WithApiReference(reportApi, "https")
    .WithApiReference(notificationApi, "https");

var frontend = builder.AddJavaScriptApp("aiyara-timesheet-frontend", "../../Frontend/app")
    .WithBun()
    .WithHttpEndpoint(port: 3000, env: "PORT")
    .WithReference(identityApi)
    .WithReference(timesheetApi)
    .WithEnvironment("IDENTITY_URL", identityApi.GetEndpoint("https"))
    .WithEnvironment("TIMESHEET_URL", timesheetApi.GetEndpoint("https"))
    .WaitFor(identityApi)
    .WaitFor(timesheetApi)
    .WaitFor(notificationApi)
    .WaitFor(reportWorker);

await builder.Build().RunAsync();
