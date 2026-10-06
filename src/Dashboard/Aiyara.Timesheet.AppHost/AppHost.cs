using Scalar.Aspire;

var builder = DistributedApplication.CreateBuilder(args);

// Compose remains the single owner of local infrastructure. Register its
// endpoints as external resources so the Aspire dashboard documents the
// development topology without starting duplicate containers.
var postgresPort = builder.Configuration["POSTGRES_PORT"] ?? "5432";
var redisPort = builder.Configuration["REDIS_PORT"] ?? "6379";
var rabbitMqPort = builder.Configuration["RABBITMQ_AMQP_PORT"] ?? "5672";
var rustFsPort = builder.Configuration["RUSTFS_API_PORT"] ?? "9000";

var composePostgres = builder.AddExternalService("compose-postgres", $"tcp://localhost:{postgresPort}");
var composeRedis = builder.AddExternalService("compose-redis", $"tcp://localhost:{redisPort}");
var composeRabbitMq = builder.AddExternalService("compose-rabbitmq", $"amqp://localhost:{rabbitMqPort}");
var composeRustFs = builder.AddExternalService("compose-rustfs", $"http://localhost:{rustFsPort}")
    .WithHttpHealthCheck("/health");

var gatewayApi = builder.AddProject<Projects.Aiyara_Gateways_Api>("aiyara-gateways-api");

var identityApi = builder.AddProject<Projects.Aiyara_Identities_Api>("aiyara-identities-api")
    .WithReference(composePostgres)
    .WithReference(composeRedis)
    .WithEnvironment("Dependencies__Postgres__Host", "localhost")
    .WithEnvironment("Dependencies__Postgres__Port", postgresPort)
    .WithEnvironment("Dependencies__Redis__Host", "localhost")
    .WithEnvironment("Dependencies__Redis__Port", redisPort);

var timesheetApi = builder.AddProject<Projects.Aiyara_Timesheet_Api>("aiyara-timesheet-api")
    .WithReference(composePostgres)
    .WithReference(composeRedis)
    .WithEnvironment("Dependencies__Postgres__Host", "localhost")
    .WithEnvironment("Dependencies__Postgres__Port", postgresPort)
    .WithEnvironment("Dependencies__Redis__Host", "localhost")
    .WithEnvironment("Dependencies__Redis__Port", redisPort);

var reportApi = builder.AddProject<Projects.Aiyara_Report_Api>("aiyara-report-api")
    .WithReference(composePostgres)
    .WithEnvironment("Dependencies__Postgres__Host", "localhost")
    .WithEnvironment("Dependencies__Postgres__Port", postgresPort);

var notificationApi = builder.AddProject<Projects.Aiyara_Notifications_Api>("aiyara-notifications-api")
    .WithReference(composeRabbitMq)
    .WithEnvironment("Dependencies__RabbitMq__Host", "localhost")
    .WithEnvironment("Dependencies__RabbitMq__Port", rabbitMqPort);

var reportWorker = builder.AddProject<Projects.Aiyara_Report_Worker>("aiyara-report-worker")
    .WithReference(composeRabbitMq)
    .WithReference(composeRustFs)
    .WithEnvironment("Dependencies__RabbitMq__Host", "localhost")
    .WithEnvironment("Dependencies__RabbitMq__Port", rabbitMqPort)
    .WithEnvironment("Dependencies__RustFs__Host", "localhost")
    .WithEnvironment("Dependencies__RustFs__Port", rustFsPort);

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
