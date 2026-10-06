using System.Threading.RateLimiting;
using Aiyara.Timesheet.Contracts.Onboarding.V1;
using Scalar.AspNetCore;
using Aiyara.Timesheet.Contracts.Identity.V1;
using Grpc.Net.Client;
using Yarp.ReverseProxy.Transforms;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddTransforms(transformBuilder =>
    {
        transformBuilder.AddRequestHeaderRemove("X-Tenant-Id");
        transformBuilder.AddRequestHeaderRemove("X-User-Id");
        transformBuilder.AddRequestHeaderRemove("X-Onboarding-Key");
        if (transformBuilder.Route.AuthorizationPolicy == "TenantMember")
        {
            transformBuilder.AddRequestTransform(context =>
            {
                var tenantId = context.HttpContext.User.FindFirst("tenant_id")?.Value;
                if (tenantId is not null)
                {
                    context.ProxyRequest.Headers.TryAddWithoutValidation("X-Tenant-Id", tenantId);
                }
                return ValueTask.CompletedTask;
            });
        }
    });
builder.Services.AddSingleton(_ => GrpcChannel.ForAddress(
    builder.Configuration["IdentityGrpc:Url"] ?? "http://localhost:8082"));
builder.Services.AddSingleton(provider =>
    new IdentityValidationService.IdentityValidationServiceClient(provider.GetRequiredService<GrpcChannel>()));
builder.Services.AddSingleton<RedisIdentityValidationCache>();
builder.Services.AddAuthentication("IdentityGrpc")
    .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions,
        IdentityGrpcAuthenticationHandler>("IdentityGrpc", _ => { });
builder.Services.AddSingleton(_ => new IdentityOnboardingClient(new HttpClient
{
    BaseAddress = new Uri(builder.Configuration["Services:Identity:BaseUrl"]
        ?? throw new InvalidOperationException("Services:Identity:BaseUrl is required."))
}));
builder.Services.AddCors(options => options.AddPolicy("frontend", policy => policy
    .WithOrigins(builder.Configuration["Cors:FrontendOrigin"] ?? "http://localhost:3000")
    .AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("onboarding", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 20,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 60,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));
});
builder.Services.AddAuthorization(options => options.AddPolicy("TenantMember", policy =>
    policy.RequireAuthenticatedUser().RequireClaim("tenant_id")));

var app = builder.Build();

app.MapDefaultEndpoints();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI();
    app.MapScalarApiReference(options => options
        .AddDocument("gateway", "Gateway", "/openapi/v1.json")
        .AddDocument("identity", "Identity", "/api-docs/identity/openapi/v1.json")
        .AddDocument("timesheet", "Timesheet", "/api-docs/timesheet/openapi/v1.json")
        .AddDocument("reporting", "Reporting", "/api-docs/reporting/openapi/v1.json")
        .AddDocument("notification", "Notification", "/api-docs/notification/openapi/v1.json"));
}

app.UseCors("frontend");
app.UseRateLimiter();
app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

var onboarding = app.MapGroup("/api/v1").WithTags("Onboarding")
    .RequireCors("frontend").RequireRateLimiting("onboarding");
onboarding.MapPost("/tenants", (CreateTenantRequest request, IdentityOnboardingClient client,
    HttpContext context, CancellationToken cancellationToken) =>
{
    context.Response.Headers.CacheControl = "no-store";
    return client.CreateTenantAsync(request, cancellationToken);
})
    .WithName("GatewayCreateTenantV1")
    .Produces<CreateTenantResponse>(StatusCodes.Status201Created)
    .ProducesProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status409Conflict)
    .ProducesProblem(StatusCodes.Status502BadGateway);
onboarding.MapPost("/tenants/{tenantId:guid}/invitations", (
    Guid tenantId, IssueInvitationRequest request, HttpContext context,
    IdentityOnboardingClient client, CancellationToken cancellationToken) =>
{
    context.Response.Headers.CacheControl = "no-store";
    return client.IssueInvitationAsync(tenantId, request,
        context.Request.Headers["X-Onboarding-Key"].ToString(), cancellationToken);
})
    .WithName("GatewayIssueInvitationV1")
    .WithDescription("Requires the tenant-bound X-Onboarding-Key returned once when the tenant was created. The key expires after seven days.")
    .Produces<IssueInvitationResponse>(StatusCodes.Status201Created)
    .ProducesProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status401Unauthorized)
    .ProducesProblem(StatusCodes.Status409Conflict)
    .ProducesProblem(StatusCodes.Status502BadGateway);
onboarding.MapPost("/invitations/accept", (AcceptInvitationRequest request,
    IdentityOnboardingClient client, CancellationToken cancellationToken) =>
    client.AcceptInvitationAsync(request, cancellationToken))
    .WithName("GatewayAcceptInvitationV1")
    .Produces<AcceptInvitationResponse>(StatusCodes.Status200OK)
    .ProducesProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status409Conflict)
    .ProducesProblem(StatusCodes.Status502BadGateway);

app.MapReverseProxy();

app.Run();
