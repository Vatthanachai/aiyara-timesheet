using Aiyara.Identities.Databases;
using Aiyara.Identities.Services.Onboarding;
using Aiyara.Timesheet.Contracts.Onboarding.V1;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddTcpDependencyHealthCheck("Postgres", 5432);
builder.AddTcpDependencyHealthCheck("Redis", 6379);
builder.Services.AddScoped<TenantScope>();
builder.Services.AddDbContext<IdentityDbContext>(options => options.UseNpgsql(
    builder.Configuration.GetConnectionString("IdentityDb")
    ?? throw new InvalidOperationException("ConnectionStrings:IdentityDb is required.")));
builder.AddDatabaseHealthCheck<WebApplicationBuilder, IdentityDbContext>("identity-db");
builder.Services.AddScoped<OnboardingService>();
builder.Services.AddGrpc();

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.MigrateAsync();
}

app.MapDefaultEndpoints();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();
app.Use(async (context, next) =>
{
    TenantScopeResolver.Resolve(context.User,
        context.RequestServices.GetRequiredService<TenantScope>());
    await next();
});

app.MapControllers();
app.MapGrpcService<IdentityValidationGrpcService>();

var onboarding = app.MapGroup("/api/v1").WithTags("Onboarding");
onboarding.MapPost("/tenants", async (CreateTenantRequest request, HttpContext context,
    OnboardingService service,
    CancellationToken cancellationToken) =>
{
    try
    {
        var result = await service.CreateTenantAsync(request, cancellationToken);
        context.Response.Headers.CacheControl = "no-store";
        return Results.Json(result, statusCode: StatusCodes.Status201Created);
    }
    catch (OnboardingException exception)
    {
        var status = exception.Failure == OnboardingFailure.Conflict ? 409 : 400;
        return Results.Problem(exception.Message, statusCode: status);
    }
})
.WithName("CreateTenantV1")
.Produces<CreateTenantResponse>(StatusCodes.Status201Created)
.ProducesProblem(StatusCodes.Status400BadRequest)
.ProducesProblem(StatusCodes.Status409Conflict);

onboarding.MapPost("/invitations/accept", async (AcceptInvitationRequest request,
    OnboardingService service, CancellationToken cancellationToken) =>
{
    try
    {
        var result = await service.AcceptInvitationAsync(request, cancellationToken);
        return Results.Ok(result);
    }
    catch (OnboardingException exception)
    {
        var status = exception.Failure == OnboardingFailure.Conflict ? 409 : 400;
        return Results.Problem(exception.Message, statusCode: status);
    }
})
.WithName("AcceptInvitationV1")
.Produces<AcceptInvitationResponse>(StatusCodes.Status200OK)
.ProducesProblem(StatusCodes.Status400BadRequest)
.ProducesProblem(StatusCodes.Status409Conflict);

onboarding.MapPost("/tenants/{tenantId:guid}/invitations", async (
    Guid tenantId, IssueInvitationRequest request, HttpContext context,
    OnboardingService service, CancellationToken cancellationToken) =>
{
    try
    {
        var key = context.Request.Headers["X-Onboarding-Key"].ToString();
        var result = await service.IssueInvitationWithKeyAsync(tenantId, key, request,
            cancellationToken);
        context.Response.Headers.CacheControl = "no-store";
        return Results.Json(result, statusCode: StatusCodes.Status201Created);
    }
    catch (OnboardingException exception)
    {
        var status = exception.Failure switch
        {
            OnboardingFailure.Unauthorized => StatusCodes.Status401Unauthorized,
            OnboardingFailure.Conflict => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest
        };
        return Results.Problem(exception.Message, statusCode: status);
    }
})
.WithName("IssueInvitationV1")
.WithDescription("Requires a valid tenant-bound X-Onboarding-Key. The key is returned once at tenant creation and expires after seven days.")
.Produces<IssueInvitationResponse>(StatusCodes.Status201Created)
.ProducesProblem(StatusCodes.Status400BadRequest)
.ProducesProblem(StatusCodes.Status401Unauthorized)
.ProducesProblem(StatusCodes.Status409Conflict);

app.Run();
