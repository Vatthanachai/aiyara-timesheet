var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithPgAdmin(options => options.WithImage("9.17"))
    .WithDataVolume();

var identityApi = builder.AddProject<Projects.Aiyara_Identities_Api>("aiyara-identities-api");

var timesheetApi = builder.AddProject<Projects.Aiyara_Timesheet_Api>("aiyara-timesheet-api");

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
