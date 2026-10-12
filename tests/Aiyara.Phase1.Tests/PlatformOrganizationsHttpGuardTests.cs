using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Aiyara.Identities.Api.Controllers;
using Aiyara.Identities.Databases;
using Aiyara.Identities.Models.Tenancy;
using Aiyara.Identities.Services.Authentication;
using Aiyara.Identities.Services.Onboarding;
using Aiyara.Timesheet.Component.Abstractions.Securities;
using Aiyara.Timesheet.Component.Abstractions.Securities.Options;
using Aiyara.Timesheet.Contracts.Onboarding.V1;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aiyara.Phase1.Tests;

/// <summary>Exercises PlatformOrganizationsController's own token checks over HTTP, independent
/// of Gateway's role policy, so a misrouted or forged request still cannot reach platform data.</summary>
public sealed class PlatformOrganizationsHttpGuardTests : IAsyncLifetime
{
    private const string Route = "/api/v1/platform/organizations";
    private static readonly Guid PlatformTenant = PlatformAdminBootstrap.PlatformTenantId;
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly PasetoTokenService tokens = new(Options.Create(new PasetoSetting
    {
        Key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
    }));
    private WebApplication app = null!;
    private HttpClient client = null!;
    private Guid platformAdminId;
    private Guid customerTenantId;
    private Guid tenantAdminId;

    [Fact]
    public async Task Only_an_active_platform_admin_token_reaches_platform_routes()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, await ListAs(null));
        Assert.Equal(HttpStatusCode.OK, await ListAs(Token(platformAdminId, PlatformTenant, "PlatformAdmin")));

        Assert.Equal(HttpStatusCode.Unauthorized,
            await ListAs(Token(tenantAdminId, customerTenantId, "TenantAdmin")));
        Assert.Equal(HttpStatusCode.Unauthorized,
            await ListAs(Token(tenantAdminId, customerTenantId, "PlatformAdmin")));
        Assert.Equal(HttpStatusCode.Unauthorized,
            await ListAs(Token(tenantAdminId, PlatformTenant, "PlatformAdmin")));
        Assert.Equal(HttpStatusCode.Unauthorized,
            await ListAs(Token(platformAdminId, customerTenantId, "PlatformAdmin")));
        Assert.Equal(HttpStatusCode.Unauthorized,
            await ListAs(Token(platformAdminId, PlatformTenant, "PlatformAdmin", mustChangePassword: true)));
        Assert.Equal(HttpStatusCode.Unauthorized,
            await ListAs(Token(platformAdminId, PlatformTenant, "PlatformAdmin", sessionVersion: 7)));
        Assert.Equal(HttpStatusCode.Unauthorized, await ListAs("v4.public.not-a-real-token"));
    }

    [Theory]
    [InlineData("/api/v1/platform/overview")]
    [InlineData("/api/v1/platform/audit")]
    [InlineData("/api/v1/platform/organizations/{tenant}")]
    public async Task Console_read_routes_require_a_platform_admin(string template)
    {
        var path = template.Replace("{tenant}", customerTenantId.ToString());

        Assert.Equal(HttpStatusCode.Unauthorized, (await Send(HttpMethod.Get, null, null, path)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Send(HttpMethod.Get,
            Token(tenantAdminId, customerTenantId, "TenantAdmin"), null, path)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Get,
            Token(platformAdminId, PlatformTenant, "PlatformAdmin"), null, path)).StatusCode);
    }

    [Fact]
    public async Task Deactivated_platform_membership_loses_access_immediately()
    {
        var token = Token(platformAdminId, PlatformTenant, "PlatformAdmin");
        Assert.Equal(HttpStatusCode.OK, await ListAs(token));

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var membership = await db.Memberships.IgnoreQueryFilters()
                .SingleAsync(x => x.AccountId == platformAdminId);
            membership.Status = MembershipStatus.PendingActivation;
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.Unauthorized, await ListAs(token));
    }

    [Fact]
    public async Task Tenant_admin_cannot_create_organizations_through_the_platform_route()
    {
        var body = new CreatePlatformOrganizationRequest("Intruder", "intruder", "x@intruder.test");

        var denied = await Send(HttpMethod.Post, Token(tenantAdminId, customerTenantId, "TenantAdmin"), body);
        var created = await Send(HttpMethod.Post, Token(platformAdminId, PlatformTenant, "PlatformAdmin"),
            body with { Slug = "allowed" });

        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal("no-store", created.Headers.CacheControl?.ToString());
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        Assert.False(await db.Tenants.IgnoreQueryFilters().AnyAsync(x => x.Slug == "intruder"));
        Assert.True(await db.Tenants.IgnoreQueryFilters().AnyAsync(x => x.Slug == "allowed"));
    }

    public async Task InitializeAsync()
    {
        await connection.OpenAsync();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddScoped<TenantScope>();
        builder.Services.AddDbContext<IdentityDbContext>(options => options.UseSqlite(connection));
        builder.Services.AddSingleton<IPasetoTokenService>(tokens);
        builder.Services.AddSingleton<ISessionRevocationPublisher,
            PlatformOrganizationManagementTests.FakeRevocations>();
        builder.Services.AddSingleton<ICredentialNotificationSender, NullNotifications>();
        builder.Services.AddScoped(services => new PlatformOrganizationService(
            services.GetRequiredService<IdentityDbContext>(), services.GetRequiredService<TenantScope>(),
            services.GetRequiredService<ISessionRevocationPublisher>()));
        builder.Services.AddScoped<PlatformConsoleService>();
        builder.Services.AddScoped<PlatformActorResolver>();
        builder.Services.AddControllers()
            .AddApplicationPart(typeof(PlatformOrganizationsController).Assembly);
        app = builder.Build();
        app.MapControllers();

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            await db.Database.EnsureCreatedAsync();
            var now = DateTime.UtcNow;
            platformAdminId = Guid.NewGuid();
            db.Tenants.Add(new Tenant { Id = PlatformTenant, Name = "Aiyara Platform",
                Slug = "platform-system", OrganizationCode = "AIYA001", CreatedAtUtc = now });
            db.Accounts.Add(new Account { Id = platformAdminId, Email = "platform@aiyara.test",
                IsPlatformAdmin = true, CreatedAtUtc = now });
            db.Memberships.Add(new Membership { Id = Guid.NewGuid(), TenantId = PlatformTenant,
                AccountId = platformAdminId, Role = TenantRole.PlatformAdmin,
                Status = MembershipStatus.Active, CreatedAtUtc = now });
            await db.SaveChangesAsync();
            var tenant = await new OnboardingService(db, scope.ServiceProvider.GetRequiredService<TenantScope>())
                .CreateTenantAsync(new CreateTenantRequest("Acme", "acme", "admin@acme.test"), default);
            customerTenantId = tenant.TenantId;
            tenantAdminId = tenant.AccountId;
        }

        await app.StartAsync();
        client = app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        client.Dispose();
        await app.DisposeAsync();
        await connection.DisposeAsync();
    }

    private string Token(Guid userId, Guid tenantId, string role, long sessionVersion = 0,
        bool mustChangePassword = false) => tokens.GenerateToken(new PasetoTokenClaims
        {
            UserId = userId, TenantId = tenantId, TokenId = Guid.NewGuid().ToString(),
            Email = "caller@test", Role = role, SessionVersion = sessionVersion,
            MustChangePassword = mustChangePassword
        }).Value;

    private async Task<HttpStatusCode> ListAs(string? token) =>
        (await Send(HttpMethod.Get, token, null)).StatusCode;

    private async Task<HttpResponseMessage> Send(HttpMethod method, string? token, object? body,
        string path = Route)
    {
        using var request = new HttpRequestMessage(method, path);
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }

    private sealed class NullNotifications : ICredentialNotificationSender
    {
        public Task SendAsync(string email, string template, string code,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
