using Scalar.Aspire;

var builder = DistributedApplication.CreateBuilder(args);

var identityApi = builder.AddProject<Projects.Aiyara_Identities_Api>("aiyara-identities-api");

var timesheetApi = builder.AddProject<Projects.Aiyara_Timesheet_Api>("aiyara-timesheet-api");

// Docker Compose owns shared development infrastructure. Keep AppHost focused on
// starting/debugging application projects and exposing their development API docs.
builder.AddScalarApiReference("api-reference", options => options.AllowSelfSignedCertificates())
    .WithApiReference(identityApi, "https")
    .WithApiReference(timesheetApi, "https");

var frontend = builder.AddJavaScriptApp("aiyara-timesheet-frontend", "../../frontend/app")
    .WithBun()
    .WithHttpEndpoint(port: 3000, env: "PORT")
    .WithReference(identityApi)
    .WithReference(timesheetApi)
    .WithEnvironment("IDENTITY_URL", identityApi.GetEndpoint("https"))
    .WithEnvironment("TIMESHEET_URL", timesheetApi.GetEndpoint("https"))
    .WaitFor(identityApi)
    .WaitFor(timesheetApi);

await builder.Build().RunAsync();
