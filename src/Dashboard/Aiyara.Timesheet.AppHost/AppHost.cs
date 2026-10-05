using Scalar.Aspire;

var builder = DistributedApplication.CreateBuilder(args);

// Compose remains the single owner of local infrastructure. Register its
// endpoints as external resources so the Aspire dashboard documents the
// development topology without starting duplicate containers.
var composePostgres = builder.AddExternalService("compose-postgres", "tcp://localhost:5432");
var composeRedis = builder.AddExternalService("compose-redis", "tcp://localhost:6379");
var composeRabbitMq = builder.AddExternalService("compose-rabbitmq", "amqp://localhost:5672");
var composeRustFs = builder.AddExternalService("compose-rustfs", "http://localhost:9000")
    .WithHttpHealthCheck("/health");

var gatewayApi = builder.AddProject<Projects.Aiyara_Gateways_Api>("aiyara-gateways-api");

var identityApi = builder.AddProject<Projects.Aiyara_Identities_Api>("aiyara-identities-api");

var timesheetApi = builder.AddProject<Projects.Aiyara_Timesheet_Api>("aiyara-timesheet-api");

var reportApi = builder.AddProject<Projects.Aiyara_Report_Api>("aiyara-report-api");

var notificationApi = builder.AddProject<Projects.Aiyara_Notifications_Api>("aiyara-notifications-api");

var reportWorker = builder.AddProject<Projects.Aiyara_Report_Worker>("aiyara-report-worker")
    .WaitFor(reportApi);

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
