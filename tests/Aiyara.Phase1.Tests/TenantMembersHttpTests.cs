using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Aiyara.Identities.Api.Controllers;
using Aiyara.Identities.Databases;
using Aiyara.Identities.Models.Tenancy;
using Aiyara.Identities.Services.Authentication;
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
    private readonly Guid outsiderId = Guid.NewGuid();
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
        Assert.Equal([true, false, false], members.Select(x => x.GetProperty("isCurrentUser").GetBoolean()));
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

    [Fact]
    public async Task Member_changes_answer_with_the_documented_status_codes()
    {
        var admin = Token(adminId, acme, "TenantAdmin");

        using var self = await Send(HttpMethod.Put, $"/api/v1/members/{adminId}/role", admin, new { role = "Employee" });
        using var badRole = await Send(HttpMethod.Put, $"/api/v1/members/{employeeId}/role", admin, new { role = "Owner" });
        using var outsider = await Send(HttpMethod.Put, $"/api/v1/members/{outsiderId}/status", admin, new { active = false });
        using var noInvitation = await Send(HttpMethod.Delete, $"/api/v1/members/invitations/{Guid.NewGuid()}", admin);
        using var byEmployee = await Send(HttpMethod.Put, $"/api/v1/members/{employeeId}/role",
            Token(employeeId, acme, "Employee"), new { role = "TenantAdmin" });
        using var promoted = await Send(HttpMethod.Put, $"/api/v1/members/{employeeId}/role", admin, new { role = "TenantAdmin" });
        using var invitations = await Send(HttpMethod.Get, "/api/v1/members/invitations", admin);

        Assert.Equal((HttpStatusCode.Conflict, "CANNOT_CHANGE_SELF"), (self.StatusCode, await ErrorCode(self)));
        Assert.Equal(HttpStatusCode.BadRequest, badRole.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, outsider.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, noInvitation.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, byEmployee.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, promoted.StatusCode);
        Assert.Equal(HttpStatusCode.OK, invitations.StatusCode);
        Assert.Equal("no-store", promoted.Headers.CacheControl?.ToString());

        // The promotion ended the member's sessions, so their old token no longer works.
        using var staleAdmin = await Send(HttpMethod.Get, "/api/v1/members", Token(employeeId, acme, "TenantAdmin"));
        Assert.Equal(HttpStatusCode.Unauthorized, staleAdmin.StatusCode);
    }

    [Fact]
    public async Task Member_groups_answer_with_the_documented_status_codes()
    {
        var admin = Token(adminId, acme, "TenantAdmin");

        using var created = await Send(HttpMethod.Post, "/api/v1/members/groups", admin, new { name = "Outsource" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var createdJson = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var groupId = createdJson.RootElement.GetProperty("result").GetProperty("id").GetGuid();
        using var duplicate = await Send(HttpMethod.Post, "/api/v1/members/groups", admin, new { name = "outsource" });
        using var blank = await Send(HttpMethod.Post, "/api/v1/members/groups", admin, new { name = " " });
        using var assigned = await Send(HttpMethod.Put, $"/api/v1/members/{employeeId}/group", admin, new { groupId });
        using var self = await Send(HttpMethod.Put, $"/api/v1/members/{adminId}/group", admin, new { groupId });
        using var outsider = await Send(HttpMethod.Put, $"/api/v1/members/{outsiderId}/group", admin, new { groupId });
        using var inUse = await Send(HttpMethod.Delete, $"/api/v1/members/groups/{groupId}", admin);
        using var byEmployee = await Send(HttpMethod.Get, "/api/v1/members/groups", Token(employeeId, acme, "Employee"));
        using var groups = await Send(HttpMethod.Get, "/api/v1/members/groups", admin);
        using var directory = await Get(admin);

        Assert.Equal((HttpStatusCode.Conflict, "GROUP_NAME_TAKEN"), (duplicate.StatusCode, await ErrorCode(duplicate)));
        Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, assigned.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, self.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, outsider.StatusCode);
        Assert.Equal((HttpStatusCode.Conflict, "GROUP_IN_USE"), (inUse.StatusCode, await ErrorCode(inUse)));
        Assert.Equal(HttpStatusCode.Forbidden, byEmployee.StatusCode);
        using var groupsJson = JsonDocument.Parse(await groups.Content.ReadAsStringAsync());
        var group = Assert.Single(groupsJson.RootElement.GetProperty("result").EnumerateArray());
        Assert.Equal(("Outsource", 2), (group.GetProperty("name").GetString(), group.GetProperty("memberCount").GetInt32()));
        using var directoryJson = JsonDocument.Parse(await directory.Content.ReadAsStringAsync());
        Assert.Equal(["Outsource", "Outsource", null], directoryJson.RootElement.GetProperty("result").EnumerateArray()
            .Select(x => x.GetProperty("groupName").GetString()));

        // A group change is not a permission change, so the admin's own token keeps working.
        using var cleared = await Send(HttpMethod.Put, $"/api/v1/members/{adminId}/group", admin, new { groupId = (Guid?)null });
        Assert.Equal(HttpStatusCode.NoContent, cleared.StatusCode);
    }

    public async Task InitializeAsync()
    {
        await connection.OpenAsync();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddScoped<TenantScope>();
        builder.Services.AddDbContext<IdentityDbContext>(options => options.UseSqlite(connection));
        builder.Services.AddSingleton<IPasetoTokenService>(tokens);
        builder.Services.AddScoped<OnboardingService>();
        builder.Services.AddScoped<PlatformOrganizationService>();
        builder.Services.AddScoped<TenantMemberService>();
        builder.Services.AddSingleton<ISessionRevocationPublisher, NoRevocations>();
        builder.Services.AddSingleton<ICredentialNotificationSender, NoNotifications>();
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
            db.Accounts.AddRange(
                new Account { Id = adminId, Email = "admin@acme.test", FirstName = "Anong", LastName = "Admin", CreatedAtUtc = now,
                    PasswordHash = "hash", PasswordChangedAtUtc = now.AddSeconds(1) },
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

    private async Task<HttpResponseMessage> Send(HttpMethod method, string path, string token, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }

    private static async Task<string?> ErrorCode(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("errorCode").GetString();
    }

    private sealed class NoRevocations : ISessionRevocationPublisher
    {
        public Task PublishAccountAsync(Guid tenantId, Guid accountId, long sessionVersion,
            CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PublishTenantPolicyAsync(Guid tenantId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task CompleteTenantPolicyAsync(Guid tenantId, long policyVersion,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class NoNotifications : ICredentialNotificationSender
    {
        public Task SendAsync(string email, string template, string code, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private async Task<HttpResponseMessage> Get(string? token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/members");
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }
}
