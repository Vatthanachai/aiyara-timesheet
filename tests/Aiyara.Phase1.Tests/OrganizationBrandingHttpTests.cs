using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Aiyara.Identities.Api;
using Aiyara.Identities.Api.Controllers;
using Aiyara.Identities.Databases;
using Aiyara.Identities.Models.Tenancy;
using Aiyara.Identities.Services.Onboarding;
using Aiyara.Timesheet.Component.Abstractions.Securities;
using Aiyara.Timesheet.Component.Abstractions.Securities.Options;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aiyara.Phase1.Tests;

/// <summary>The organization name and logo in the Shell header: members read them, only an
/// active Tenant Admin with a current password changes them, and every change is audited.</summary>
public sealed class OrganizationBrandingHttpTests : IAsyncLifetime
{
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+j5WQAAAAASUVORK5CYII=");
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly PasetoTokenService tokens = new(Options.Create(new PasetoSetting
    {
        Key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
    }));
    private readonly MemoryLogoStorage storage = new();
    private readonly Guid acme = Guid.NewGuid();
    private readonly Guid beta = Guid.NewGuid();
    private readonly Guid adminId = Guid.NewGuid();
    private readonly Guid employeeId = Guid.NewGuid();
    private readonly Guid betaAdminId = Guid.NewGuid();
    private WebApplication app = null!;
    private HttpClient client = null!;

    [Fact]
    public async Task Members_read_the_name_and_only_admins_rename_it()
    {
        using var read = await Send(HttpMethod.Get, "/api/v1/organization", Token(employeeId, acme, "Employee"));
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        var organization = await Result(read);
        Assert.Equal("Acme", organization.GetProperty("name").GetString());
        Assert.Equal("ACME123", organization.GetProperty("organizationCode").GetString());
        Assert.Equal(JsonValueKind.Null, organization.GetProperty("logoUrl").ValueKind);

        using var byEmployee = await Send(HttpMethod.Put, "/api/v1/organization", Token(employeeId, acme, "Employee"),
            JsonContent.Create(new { name = "Taken over" }));
        using var empty = await Send(HttpMethod.Put, "/api/v1/organization", Token(adminId, acme, "TenantAdmin"),
            JsonContent.Create(new { name = "   " }));
        using var tooLong = await Send(HttpMethod.Put, "/api/v1/organization", Token(adminId, acme, "TenantAdmin"),
            JsonContent.Create(new { name = new string('x', 201) }));
        using var renamed = await Send(HttpMethod.Put, "/api/v1/organization", Token(adminId, acme, "TenantAdmin"),
            JsonContent.Create(new { name = "  Acme Studio  " }));

        Assert.Equal(HttpStatusCode.Forbidden, byEmployee.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        Assert.Equal("Acme Studio", (await Result(renamed)).GetProperty("name").GetString());
        var audit = await Audits("organization.updated").SingleAsync();
        Assert.Equal(adminId, audit.ActorAccountId);
        Assert.Contains("Acme Studio", audit.AfterJson);
        Assert.Contains("\"Acme\"", audit.BeforeJson);
    }

    [Fact]
    public async Task Members_read_the_date_and_time_formats_and_only_admins_change_them()
    {
        var admin = Token(adminId, acme, "TenantAdmin");
        using var read = await Send(HttpMethod.Get, "/api/v1/organization", Token(employeeId, acme, "Employee"));
        var defaults = await Result(read);
        Assert.Equal(("dd/MM/yyyy", "HH:mm"),
            (defaults.GetProperty("dateFormat").GetString(), defaults.GetProperty("timeFormat").GetString()));

        using var byEmployee = await Send(HttpMethod.Put, "/api/v1/organization/formats", Token(employeeId, acme, "Employee"),
            JsonContent.Create(new { dateFormat = "dd/MM/bbbb", timeFormat = "h:mm a" }));
        using var badDate = await Send(HttpMethod.Put, "/api/v1/organization/formats", admin,
            JsonContent.Create(new { dateFormat = "dddd", timeFormat = "HH:mm" }));
        using var badTime = await Send(HttpMethod.Put, "/api/v1/organization/formats", admin,
            JsonContent.Create(new { dateFormat = "dd/MM/yyyy", timeFormat = "hh:mm:ss" }));
        using var missing = await Send(HttpMethod.Put, "/api/v1/organization/formats", admin, JsonContent.Create(new { }));
        using var saved = await Send(HttpMethod.Put, "/api/v1/organization/formats", admin,
            JsonContent.Create(new { dateFormat = "dd/MM/bbbb", timeFormat = "h:mm a" }));
        using var again = await Send(HttpMethod.Put, "/api/v1/organization/formats", admin,
            JsonContent.Create(new { dateFormat = "dd/MM/bbbb", timeFormat = "h:mm a" }));

        Assert.Equal(HttpStatusCode.Forbidden, byEmployee.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badDate.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badTime.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal("dd/MM/bbbb", (await Result(saved)).GetProperty("dateFormat").GetString());
        using var reread = await Send(HttpMethod.Get, "/api/v1/organization", Token(employeeId, acme, "Employee"));
        Assert.Equal("h:mm a", (await Result(reread)).GetProperty("timeFormat").GetString());
        using var otherTenant = await Send(HttpMethod.Get, "/api/v1/organization", Token(betaAdminId, beta, "TenantAdmin"));
        Assert.Equal("dd/MM/yyyy", (await Result(otherTenant)).GetProperty("dateFormat").GetString());
        // Saving the same formats again records nothing new.
        var audit = await Audits("organization.formats_updated").SingleAsync();
        Assert.Equal(adminId, audit.ActorAccountId);
        Assert.Contains("dd/MM/yyyy", audit.BeforeJson);
        Assert.Contains("h:mm a", audit.AfterJson);
    }

    [Fact]
    public async Task Admin_uploads_and_removes_a_logo_that_every_member_can_load()
    {
        var admin = Token(adminId, acme, "TenantAdmin");
        using var notImage = await Send(HttpMethod.Post, "/api/v1/organization/logo", admin, Upload("<svg></svg>"u8.ToArray()));
        using var byEmployee = await Send(HttpMethod.Post, "/api/v1/organization/logo",
            Token(employeeId, acme, "Employee"), Upload(Png));
        Assert.Equal(HttpStatusCode.BadRequest, notImage.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, byEmployee.StatusCode);
        Assert.Empty(storage.Objects);

        using var uploaded = await Send(HttpMethod.Post, "/api/v1/organization/logo", admin, Upload(Png));
        Assert.Equal(HttpStatusCode.OK, uploaded.StatusCode);
        var logoUrl = (await Result(uploaded)).GetProperty("logoUrl").GetString();
        Assert.StartsWith("/api/v1/identity/organization/logo?v=", logoUrl);

        using var logo = await Send(HttpMethod.Get, "/api/v1/organization/logo", Token(employeeId, acme, "Employee"));
        Assert.Equal(HttpStatusCode.OK, logo.StatusCode);
        Assert.Equal("image/png", logo.Content.Headers.ContentType?.MediaType);
        Assert.Equal(Png, await logo.Content.ReadAsByteArrayAsync());
        using var otherTenant = await Send(HttpMethod.Get, "/api/v1/organization/logo", Token(betaAdminId, beta, "TenantAdmin"));
        Assert.Equal(HttpStatusCode.NotFound, otherTenant.StatusCode);

        using var removed = await Send(HttpMethod.Delete, "/api/v1/organization/logo", admin);
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await Result(removed)).GetProperty("logoUrl").ValueKind);
        Assert.Empty(storage.Objects);
        using var gone = await Send(HttpMethod.Get, "/api/v1/organization/logo", admin);
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
        Assert.Equal(1, await Audits("organization.logo_updated").CountAsync());
        Assert.Equal(1, await Audits("organization.logo_removed").CountAsync());
    }

    [Fact]
    public async Task Stale_sessions_overdue_passwords_and_anonymous_callers_change_nothing()
    {
        using var anonymous = await Send(HttpMethod.Get, "/api/v1/organization", null);
        using var stale = await Send(HttpMethod.Put, "/api/v1/organization",
            Token(adminId, acme, "TenantAdmin", sessionVersion: 7), JsonContent.Create(new { name = "Stale" }));
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            await db.Accounts.Where(x => x.Id == adminId).ExecuteUpdateAsync(x =>
                x.SetProperty(a => a.PasswordChangedAtUtc, DateTime.UtcNow.AddDays(-400)));
        }
        using var overdue = await Send(HttpMethod.Post, "/api/v1/organization/logo",
            Token(adminId, acme, "TenantAdmin"), Upload(Png));

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, stale.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, overdue.StatusCode);
        Assert.Empty(storage.Objects);
        Assert.False(await Audits("organization.updated").AnyAsync());
    }

    public async Task InitializeAsync()
    {
        await connection.OpenAsync();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddScoped<TenantScope>();
        builder.Services.AddDbContext<IdentityDbContext>(options => options.UseSqlite(connection));
        builder.Services.AddSingleton<IPasetoTokenService>(tokens);
        builder.Services.AddScoped<AuthenticatedMemberResolver>();
        builder.Services.AddScoped<OrganizationBrandingService>();
        builder.Services.AddSingleton<IOrganizationLogoStorage>(storage);
        builder.Services.AddControllers().AddApplicationPart(typeof(OrganizationController).Assembly);
        app = builder.Build();
        app.MapControllers();

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            await db.Database.EnsureCreatedAsync();
            var now = DateTime.UtcNow;
            db.Tenants.AddRange(
                new Tenant { Id = acme, Name = "Acme", Slug = "acme", OrganizationCode = "ACME123", CreatedAtUtc = now },
                new Tenant { Id = beta, Name = "Beta", Slug = "beta", OrganizationCode = "BETA123", CreatedAtUtc = now });
            db.Accounts.AddRange(
                Account(adminId, "admin@acme.test", now), Account(employeeId, "staff@acme.test", now),
                Account(betaAdminId, "admin@beta.test", now));
            db.Memberships.AddRange(
                Member(acme, adminId, TenantRole.TenantAdmin), Member(acme, employeeId, TenantRole.Employee),
                Member(beta, betaAdminId, TenantRole.TenantAdmin));
            await db.SaveChangesAsync();
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

    private static Account Account(Guid id, string email, DateTime now) => new()
    {
        Id = id, Email = email, CreatedAtUtc = now, PasswordHash = "hash", PasswordChangedAtUtc = now.AddSeconds(1)
    };

    private static Membership Member(Guid tenantId, Guid accountId, TenantRole role) => new()
    {
        Id = Guid.NewGuid(), TenantId = tenantId, AccountId = accountId, Role = role,
        Status = MembershipStatus.Active, CreatedAtUtc = DateTime.UtcNow
    };

    private string Token(Guid userId, Guid tenantId, string role, long sessionVersion = 0) =>
        tokens.GenerateToken(new PasetoTokenClaims
        {
            UserId = userId, TenantId = tenantId, TokenId = Guid.NewGuid().ToString(), Email = "caller@test",
            Role = role, SessionVersion = sessionVersion
        }).Value;

    private static MultipartFormDataContent Upload(byte[] bytes)
    {
        var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(bytes), "logo", "logo.png");
        return form;
    }

    private async Task<HttpResponseMessage> Send(HttpMethod method, string path, string? token, HttpContent? body = null)
    {
        using var request = new HttpRequestMessage(method, path) { Content = body };
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }

    private static async Task<JsonElement> Result(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("result");

    private IQueryable<PlatformOrganizationAudit> Audits(string action)
    {
        var scope = app.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IdentityDbContext>().PlatformOrganizationAudits
            .IgnoreQueryFilters().Where(x => x.Action == action);
    }

    private sealed class MemoryLogoStorage : IOrganizationLogoStorage
    {
        public Dictionary<string, ProfilePhotoContent> Objects { get; } = [];

        public Task<string> PutLogoAsync(Guid tenantId, byte[] content, string contentType, CancellationToken cancellationToken)
        {
            var key = $"organizations/{tenantId:N}/logo";
            Objects[key] = new ProfilePhotoContent(content, contentType);
            return Task.FromResult(key);
        }

        public Task<ProfilePhotoContent?> GetLogoAsync(string key, CancellationToken cancellationToken) =>
            Task.FromResult(Objects.GetValueOrDefault(key));

        public Task DeleteLogoAsync(string key, CancellationToken cancellationToken)
        {
            Objects.Remove(key);
            return Task.CompletedTask;
        }
    }
}
