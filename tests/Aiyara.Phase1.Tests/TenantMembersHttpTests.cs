using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Aiyara.Identities.Api.Controllers;
using Aiyara.Identities.Databases;
using Aiyara.Identities.Models.Tenancy;
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

public sealed class TenantMembersHttpTests : IAsyncLifetime
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly PasetoTokenService tokens = new(Options.Create(new PasetoSetting
    {
        Key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
    }));
    private readonly Guid acme = Guid.NewGuid();
    private readonly Guid beta = Guid.NewGuid();
    private readonly Guid adminId = Guid.NewGuid();
    private readonly Guid employeeId = Guid.NewGuid();
    private WebApplication app = null!;
    private HttpClient client = null!;

    [Fact]
    public async Task Tenant_admin_lists_only_their_organizations_members_by_name()
    {
        using var response = await Get(Token(adminId, acme, "TenantAdmin"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var members = json.RootElement.GetProperty("result").EnumerateArray().ToList();
        Assert.Equal(["Anong", "Somchai", ""], members.Select(x => x.GetProperty("firstName").GetString()));
        Assert.Equal(["TenantAdmin", "Employee", "Employee"], members.Select(x => x.GetProperty("role").GetString()));
        Assert.Equal("PendingActivation", members[2].GetProperty("status").GetString());
        Assert.DoesNotContain(members, x => x.GetProperty("email").GetString() == "outsider@beta.test");
        Assert.DoesNotContain("passwordHash", json.RootElement.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Employees_and_anonymous_callers_cannot_list_members()
    {
        using var employee = await Get(Token(employeeId, acme, "Employee"));
        using var anonymous = await Get(null);
        using var wrongTenant = await Get(Token(adminId, beta, "TenantAdmin"));

        Assert.Equal(HttpStatusCode.Forbidden, employee.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, wrongTenant.StatusCode);
    }

    public async Task InitializeAsync()
    {
        await connection.OpenAsync();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddScoped<TenantScope>();
        builder.Services.AddDbContext<IdentityDbContext>(options => options.UseSqlite(connection));
        builder.Services.AddSingleton<IPasetoTokenService>(tokens);
        builder.Services.AddControllers().AddApplicationPart(typeof(ProfileController).Assembly);
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
            var pendingId = Guid.NewGuid();
            var outsiderId = Guid.NewGuid();
            db.Accounts.AddRange(
                new Account { Id = adminId, Email = "admin@acme.test", FirstName = "Anong", LastName = "Admin", CreatedAtUtc = now },
                new Account { Id = employeeId, Email = "somchai@acme.test", FirstName = "Somchai", LastName = "Staff", CreatedAtUtc = now },
                new Account { Id = pendingId, Email = "new@acme.test", CreatedAtUtc = now },
                new Account { Id = outsiderId, Email = "outsider@beta.test", FirstName = "Aaron", CreatedAtUtc = now });
            db.Memberships.AddRange(
                Member(acme, adminId, TenantRole.TenantAdmin, MembershipStatus.Active),
                Member(acme, employeeId, TenantRole.Employee, MembershipStatus.Active),
                Member(acme, pendingId, TenantRole.Employee, MembershipStatus.PendingActivation),
                Member(beta, outsiderId, TenantRole.TenantAdmin, MembershipStatus.Active));
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

    private static Membership Member(Guid tenantId, Guid accountId, TenantRole role, MembershipStatus status) => new()
    {
        Id = Guid.NewGuid(), TenantId = tenantId, AccountId = accountId, Role = role, Status = status,
        CreatedAtUtc = DateTime.UtcNow
    };

    private string Token(Guid userId, Guid tenantId, string role) => tokens.GenerateToken(new PasetoTokenClaims
    {
        UserId = userId, TenantId = tenantId, TokenId = Guid.NewGuid().ToString(), Email = "caller@test",
        Role = role, SessionVersion = 0
    }).Value;

    private async Task<HttpResponseMessage> Get(string? token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/members");
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }
}
