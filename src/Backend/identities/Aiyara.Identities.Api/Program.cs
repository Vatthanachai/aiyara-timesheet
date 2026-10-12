using Aiyara.Identities.Databases;
using Aiyara.Identities.Services.Onboarding;
using Aiyara.Identities.Services.Authentication;
using Aiyara.Identities.Api;
using Aiyara.Timesheet.Component.Abstractions.Controllers;
using Aiyara.Timesheet.Component.Abstractions.Securities;
using Aiyara.Timesheet.Component.Abstractions.Securities.Options;
using Aiyara.Timesheet.Contracts.Onboarding.V1;
using Microsoft.EntityFrameworkCore;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddTcpDependencyHealthCheck("Postgres", 5432);
builder.AddTcpDependencyHealthCheck("Redis", 6379);
builder.AddTcpDependencyHealthCheck("RustFs", 9000);
builder.Services.AddScoped<TenantScope>();
builder.Services.AddDbContext<IdentityDbContext>(options => options.UseNpgsql(
    builder.Configuration.GetConnectionString("IdentityDb")
    ?? throw new InvalidOperationException("ConnectionStrings:IdentityDb is required.")));
builder.AddDatabaseHealthCheck<WebApplicationBuilder, IdentityDbContext>("identity-db");
builder.Services.AddScoped<OnboardingService>();
builder.Services.AddScoped<PlatformOrganizationService>();
builder.Services.AddScoped<PlatformConsoleService>();
builder.Services.AddScoped<Aiyara.Identities.Api.Controllers.PlatformActorResolver>();
builder.Services.AddScoped<AuthenticationService>();
builder.Services.AddScoped<PlatformAdminBootstrap>();
builder.Services.AddSingleton(ProfilePhotoStorage.CreateClient(builder.Configuration));
builder.Services.AddSingleton<IProfilePhotoStorage, ProfilePhotoStorage>();
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
    options.OnRejected = (context, ct) => new ValueTask(SafeApiErrors.WriteErrorAsync(
        context.HttpContext, 429, "RATE_LIMITED", ct));
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 60, Window = TimeSpan.FromMinutes(1), QueueLimit = 0
        }));
});

// Add services to the container.

builder.Services.AddControllers();
builder.Services.AddSafeApiErrors();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();
app.UseExceptionHandler();

app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value;
    if (path is null || !path.StartsWith("/api/v1/auth/", StringComparison.OrdinalIgnoreCase))
    {
        await next(context);
        return;
    }

    try
    {
        await next(context);
    }
    finally
    {
        var action = path[13..] switch
        {
            "activation/request" => "activation_request",
            "activate" => "activation_complete",
            "login" => "login",
            "refresh" => "refresh",
            "logout" => "logout",
            "password/forgot" => "password_reset_request",
            "password/reset" => "password_reset_complete",
            "password/change" => "password_change",
            _ => "other"
        };
        AuthenticationMetrics.Record(action,
            context.Response.StatusCode < StatusCodes.Status400BadRequest ? "success" : "failure");
    }
});

var signingSeed = app.Configuration["Paseto:Key"];
if (signingSeed is null || !Convert.TryFromBase64String(signingSeed,
    new byte[32], out var signingSeedLength) || signingSeedLength != 32)
    throw new InvalidOperationException("Paseto:Key must be a base64-encoded 32-byte seed.");
if (app.Configuration["Notification:InternalKey"] is not { Length: >= 32 })
    throw new InvalidOperationException("Notification:InternalKey must contain at least 32 characters.");

using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.MigrateAsync();
    var platformAdminEmail = await scope.ServiceProvider.GetRequiredService<PlatformAdminBootstrap>()
        .EnsureAsync(app.Configuration["Bootstrap:PlatformAdminEmail"],
            CancellationToken.None);
    if (platformAdminEmail is not null)
        await TrySendActivationAsync(scope.ServiceProvider.GetRequiredService<AuthenticationService>(),
            PlatformAdminBootstrap.PlatformTenantId, platformAdminEmail,
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

app.Run();

static async Task TrySendActivationAsync(AuthenticationService authentication,
    Guid tenantId, string email, CancellationToken cancellationToken)
{
    try
    {
        await authentication.RequestActivationAsync(new RequestCredentialEmail(tenantId, email),
            cancellationToken);
    }
    catch (AuthenticationException exception) when
        (exception.Failure is AuthenticationFailure.Unavailable or
            AuthenticationFailure.RateLimited)
    {
        // The activation request endpoint remains available after delivery recovers.
    }
}
