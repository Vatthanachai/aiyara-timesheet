using Aiyara.Identities.Databases;
using Aiyara.Identities.Services.Onboarding;
using Aiyara.Identities.Services.Authentication;
using Aiyara.Timesheet.Component.Abstractions.Securities;
using Aiyara.Timesheet.Component.Abstractions.Securities.Options;
using Aiyara.Timesheet.Contracts.Onboarding.V1;
using Microsoft.EntityFrameworkCore;
using System.Threading.RateLimiting;

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
builder.Services.AddScoped<AuthenticationService>();
builder.Services.AddScoped<PlatformAdminBootstrap>();
builder.Services.AddSingleton<ISessionRevocationPublisher, RedisSessionRevocationPublisher>();
builder.Services.AddSingleton<ICredentialAttemptLimiter, RedisCredentialAttemptLimiter>();
builder.Services.Configure<PasswordGenerateSetting>(builder.Configuration.GetSection("PasswordGeneration"));
builder.Services.Configure<PasetoSetting>(builder.Configuration.GetSection("Paseto"));
builder.Services.AddSingleton<IEncryptionService, EncryptionService>();
builder.Services.AddSingleton<IPasetoTokenService, PasetoTokenService>();
builder.Services.AddHttpClient<ICredentialNotificationSender, NotificationCredentialSender>(client =>
    client.BaseAddress = new Uri(builder.Configuration["Notification:BaseUrl"]
        ?? "http://localhost:57087"));
builder.Services.AddGrpc();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 60, Window = TimeSpan.FromMinutes(1), QueueLimit = 0
        }));
});

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

var signingSeed = app.Configuration["Paseto:Key"];
if (signingSeed is null || !Convert.TryFromBase64String(signingSeed,
    new byte[32], out var signingSeedLength) || signingSeedLength != 32)
    throw new InvalidOperationException("Paseto:Key must be a base64-encoded 32-byte seed.");
if (app.Configuration["Notification:InternalKey"] is not { Length: >= 32 })
    throw new InvalidOperationException("Notification:InternalKey must contain at least 32 characters.");

using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<PlatformAdminBootstrap>()
        .EnsureAsync(app.Configuration["Bootstrap:PlatformAdminEmail"],
            CancellationToken.None);
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
app.UseRateLimiter();

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
    OnboardingService service, AuthenticationService authentication,
    CancellationToken cancellationToken) =>
{
    try
    {
        var result = await service.CreateTenantAsync(request, cancellationToken);
        await TrySendActivationAsync(authentication, result.TenantId, request.AdminEmail,
            cancellationToken);
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
    OnboardingService service, IdentityDbContext db, AuthenticationService authentication,
    CancellationToken cancellationToken) =>
{
    try
    {
        var result = await service.AcceptInvitationAsync(request, cancellationToken);
        var email = await db.Accounts.Where(x => x.Id == result.AccountId)
            .Select(x => x.Email).SingleAsync(cancellationToken);
        await TrySendActivationAsync(authentication, result.TenantId, email,
            cancellationToken);
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
    OnboardingService service, ICredentialNotificationSender notifications,
    CancellationToken cancellationToken) =>
{
    try
    {
        var key = context.Request.Headers["X-Onboarding-Key"].ToString();
        var result = await service.IssueInvitationWithKeyAsync(tenantId, key, request,
            cancellationToken);
        await TrySendInvitationAsync(notifications, request.Email, result.Code,
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

var auth = app.MapGroup("/api/v1/auth").WithTags("Authentication")
    .RequireRateLimiting("auth");
auth.MapPost("/activation/request", async (RequestCredentialEmail request,
    AuthenticationService service, CancellationToken cancellationToken) =>
    await RunAsync(async () => { await service.RequestActivationAsync(request, cancellationToken);
        return Results.Accepted(); }));
auth.MapPost("/activate", async (CompleteCredentialChallenge request,
    AuthenticationService service, CancellationToken cancellationToken) =>
    await RunAsync(async () => { await service.CompleteActivationAsync(request, cancellationToken);
        return Results.NoContent(); }));
auth.MapPost("/login", async (LoginRequest request, AuthenticationService service,
    HttpContext context, CancellationToken cancellationToken) =>
    await RunAsync(async () => { var result = await service.LoginAsync(request, cancellationToken);
        context.Response.Headers.CacheControl = "no-store"; return Results.Ok(result); }));
auth.MapPost("/refresh", async (RefreshRequest request, AuthenticationService service,
    HttpContext context, CancellationToken cancellationToken) =>
    await RunAsync(async () => { var result = await service.RefreshAsync(request, cancellationToken);
        context.Response.Headers.CacheControl = "no-store"; return Results.Ok(result); }));
auth.MapPost("/logout", async (LogoutRequest request, AuthenticationService service,
    CancellationToken cancellationToken) =>
    await RunAsync(async () => { await service.LogoutAsync(request, cancellationToken);
        return Results.NoContent(); }));
auth.MapPost("/password/forgot", async (RequestCredentialEmail request,
    AuthenticationService service, CancellationToken cancellationToken) =>
    await RunAsync(async () => { await service.RequestPasswordResetAsync(request, cancellationToken);
        return Results.Accepted(); }));
auth.MapPost("/password/reset", async (CompleteCredentialChallenge request,
    AuthenticationService service, CancellationToken cancellationToken) =>
    await RunAsync(async () => { await service.CompletePasswordResetAsync(request, cancellationToken);
        return Results.NoContent(); }));
auth.MapPost("/password/change", async (ChangePasswordRequest request,
    HttpContext context, AuthenticationService service, IPasetoTokenService tokens,
    CancellationToken cancellationToken) =>
    await RunAsync(async () =>
    {
        var authorization = context.Request.Headers.Authorization.ToString();
        var claims = authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? tokens.ValidateToken(authorization[7..].Trim()) : null;
        if (claims is null) return Results.Unauthorized();
        await service.ChangePasswordAsync(claims.TenantId, claims.UserId,
            claims.SessionVersion, request, cancellationToken);
        return Results.NoContent();
    }));
auth.MapPut("/tenants/{tenantId:guid}/password-policy", async (Guid tenantId,
    TenantPasswordPolicyRequest request, HttpContext context,
    AuthenticationService service, IPasetoTokenService tokens,
    CancellationToken cancellationToken) =>
    await RunAsync(async () =>
    {
        var authorization = context.Request.Headers.Authorization.ToString();
        var claims = authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? tokens.ValidateToken(authorization[7..].Trim()) : null;
        if (claims is null || claims.TenantId != tenantId || claims.Role != "TenantAdmin" ||
            claims.MustChangePassword)
            return Results.Unauthorized();
        await service.UpdateTenantPolicyAsync(tenantId, claims.UserId,
            claims.SessionVersion, request, cancellationToken);
        return Results.NoContent();
    }));
auth.MapPost("/tenants/{tenantId:guid}/invitations", async (
    Guid tenantId, IssueInvitationRequest request, HttpContext context,
    OnboardingService service, IPasetoTokenService tokens,
    ICredentialNotificationSender notifications, CancellationToken cancellationToken) =>
{
    var authorization = context.Request.Headers.Authorization.ToString();
    var claims = authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
        ? tokens.ValidateToken(authorization[7..].Trim()) : null;
    if (claims is null || claims.TenantId != tenantId || claims.Role != "TenantAdmin" ||
        claims.MustChangePassword) return Results.Unauthorized();
    try
    {
        var result = await service.IssueInvitationAuthorizedAsync(tenantId,
            claims.UserId, claims.SessionVersion, request, cancellationToken);
        await TrySendInvitationAsync(notifications, request.Email, result.Code,
            cancellationToken);
        context.Response.Headers.CacheControl = "no-store";
        return Results.Json(result, statusCode: StatusCodes.Status201Created);
    }
    catch (OnboardingException exception)
    {
        var status = exception.Failure switch
        {
            OnboardingFailure.Unauthorized => 401,
            OnboardingFailure.Conflict => 409,
            _ => 400
        };
        return Results.Problem(exception.Message, statusCode: status);
    }
});

app.Run();

static async Task<IResult> RunAsync(Func<Task<IResult>> action)
{
    try { return await action(); }
    catch (AuthenticationException exception)
    {
        var status = exception.Failure switch
        {
            AuthenticationFailure.InvalidCredentials => 401,
            AuthenticationFailure.Conflict => 409,
            AuthenticationFailure.RateLimited => 429,
            AuthenticationFailure.Unavailable => 503,
            _ => 400
        };
        return Results.Problem(exception.Message, statusCode: status);
    }
}

static async Task TrySendInvitationAsync(ICredentialNotificationSender sender,
    string email, string code, CancellationToken cancellationToken)
{
    try { await sender.SendAsync(email, "invitation", code, cancellationToken); }
    catch (AuthenticationException exception) when
        (exception.Failure == AuthenticationFailure.Unavailable)
    {
        // The one-time code remains in the response for a local/manual handoff.
    }
}

static async Task TrySendActivationAsync(AuthenticationService authentication,
    Guid tenantId, string email, CancellationToken cancellationToken)
{
    try
    {
        await authentication.RequestActivationAsync(new RequestCredentialEmail(tenantId, email),
            cancellationToken);
    }
    catch (AuthenticationException exception) when
        (exception.Failure == AuthenticationFailure.Unavailable)
    {
        // The activation request endpoint remains available for retrying delivery.
    }
}
