using Aiyara.Identities.Databases;
using System.Security.Claims;
using Aiyara.Identities.Services.Onboarding;
using Aiyara.Timesheet.Contracts.Onboarding.V1;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Aiyara.Phase1.Tests;

public sealed class OnboardingTests
{
    [Fact]
    public async Task Tenant_queries_fail_closed_and_never_return_another_tenant()
    {
        await using var fixture = await Fixture.CreateAsync();
        var first = await fixture.Service.CreateTenantAsync(
            new CreateTenantRequest("First", "first", "admin@first.test"), default);
        var second = await fixture.Service.CreateTenantAsync(
            new CreateTenantRequest("Second", "second", "admin@second.test"), default);

        Assert.Empty(await fixture.Db.Tenants.AsNoTracking().ToListAsync());
        Assert.Empty(await fixture.Db.Memberships.AsNoTracking().ToListAsync());
        Assert.Empty(await fixture.Db.OnboardingCapabilities.AsNoTracking().ToListAsync());
        fixture.Scope.TenantId = first.TenantId;
        Assert.Equal(first.TenantId, Assert.Single(await fixture.Db.Tenants.AsNoTracking().ToListAsync()).Id);
        Assert.Equal(first.MembershipId, Assert.Single(await fixture.Db.Memberships.AsNoTracking().ToListAsync()).Id);
        Assert.Single(await fixture.Db.OnboardingCapabilities.AsNoTracking().ToListAsync());
        fixture.Scope.TenantId = second.TenantId;
        Assert.Equal(second.TenantId, Assert.Single(await fixture.Db.Tenants.AsNoTracking().ToListAsync()).Id);
        Assert.Equal(second.MembershipId, Assert.Single(await fixture.Db.Memberships.AsNoTracking().ToListAsync()).Id);
        Assert.Single(await fixture.Db.OnboardingCapabilities.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Invitation_requires_tenant_admin_and_can_only_be_accepted_once()
    {
        await using var fixture = await Fixture.CreateAsync();
        var tenant = await fixture.Service.CreateTenantAsync(
            new CreateTenantRequest("First", "first", "admin@first.test"), default);
        var other = await fixture.Service.CreateTenantAsync(
            new CreateTenantRequest("Second", "second", "admin@second.test"), default);

        await Assert.ThrowsAsync<OnboardingException>(() => fixture.Service.IssueInvitationAsync(
            tenant.TenantId, other.AccountId, new IssueInvitationRequest("new@first.test", "Employee"), default));

        var issued = await fixture.Service.IssueInvitationAsync(tenant.TenantId, tenant.AccountId,
            new IssueInvitationRequest("new@first.test", "Employee"), default);
        Assert.NotEmpty(issued.Code);
        Assert.DoesNotContain(issued.Code, await fixture.Db.Invitations
            .IgnoreQueryFilters().Select(x => x.TokenHash).SingleAsync());

        fixture.Scope.TenantId = null;
        var accepted = await fixture.Service.AcceptInvitationAsync(
            new AcceptInvitationRequest(issued.Code), default);
        Assert.Equal(tenant.TenantId, accepted.TenantId);
        Assert.Equal("PendingActivation", accepted.Status);
        await Assert.ThrowsAsync<OnboardingException>(() => fixture.Service.AcceptInvitationAsync(
            new AcceptInvitationRequest(issued.Code), default));

        fixture.Scope.TenantId = other.TenantId;
        Assert.DoesNotContain(await fixture.Db.Memberships.AsNoTracking().ToListAsync(),
            x => x.Id == accepted.MembershipId);
    }

    [Fact]
    public async Task Duplicate_slug_and_invalid_invitation_are_rejected()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Service.CreateTenantAsync(
            new CreateTenantRequest("First", "first", "admin@first.test"), default);
        var duplicate = await Assert.ThrowsAsync<OnboardingException>(() => fixture.Service.CreateTenantAsync(
            new CreateTenantRequest("Duplicate", "FIRST", "other@test.test"), default));
        Assert.Equal(OnboardingFailure.Conflict, duplicate.Failure);
        var invalid = await Assert.ThrowsAsync<OnboardingException>(() => fixture.Service.AcceptInvitationAsync(
            new AcceptInvitationRequest("unknown"), default));
        Assert.Equal(OnboardingFailure.InvalidInvitation, invalid.Failure);
    }

    [Fact]
    public async Task Onboarding_key_authorizes_only_its_tenant_and_expires()
    {
        await using var fixture = await Fixture.CreateAsync();
        var first = await fixture.Service.CreateTenantAsync(
            new CreateTenantRequest("First", "first", "admin@first.test"), default);
        var second = await fixture.Service.CreateTenantAsync(
            new CreateTenantRequest("Second", "second", "admin@second.test"), default);
        var request = new IssueInvitationRequest("invite@first.test", "Employee");

        Assert.NotEmpty(first.OnboardingKey);
        Assert.DoesNotContain(first.OnboardingKey, first.ToString());
        Assert.DoesNotContain(first.OnboardingKey,
            await fixture.Db.OnboardingCapabilities.IgnoreQueryFilters()
                .Where(x => x.TenantId == first.TenantId)
                .Select(x => x.KeyHash).SingleAsync());
        var missing = await Assert.ThrowsAsync<OnboardingException>(() =>
            fixture.Service.IssueInvitationWithKeyAsync(first.TenantId, null, request, default));
        Assert.Equal(OnboardingFailure.Unauthorized, missing.Failure);
        var crossTenant = await Assert.ThrowsAsync<OnboardingException>(() =>
            fixture.Service.IssueInvitationWithKeyAsync(second.TenantId,
                first.OnboardingKey, request, default));
        Assert.Equal(OnboardingFailure.Unauthorized, crossTenant.Failure);

        var issued = await fixture.Service.IssueInvitationWithKeyAsync(first.TenantId,
            first.OnboardingKey, request, default);
        Assert.Equal(first.TenantId, issued.TenantId);
        Assert.DoesNotContain(issued.Code, issued.ToString());
        Assert.DoesNotContain(issued.Code, new AcceptInvitationRequest(issued.Code).ToString());
        fixture.Scope.TenantId = first.TenantId;
        var capability = await fixture.Db.OnboardingCapabilities.SingleAsync();
        capability.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
        await fixture.Db.SaveChangesAsync();
        var expired = await Assert.ThrowsAsync<OnboardingException>(() =>
            fixture.Service.IssueInvitationWithKeyAsync(first.TenantId,
                first.OnboardingKey, request, default));
        Assert.Equal(OnboardingFailure.Unauthorized, expired.Failure);
    }

    [Fact]
    public void Request_tenant_scope_requires_an_authenticated_tenant_claim()
    {
        var tenantId = Guid.NewGuid();
        var scope = new TenantScope();
        TenantScopeResolver.Resolve(new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("tenant_id", tenantId.ToString())])), scope);
        Assert.Null(scope.TenantId);

        TenantScopeResolver.Resolve(new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("tenant_id", "not-a-guid")], "test")), scope);
        Assert.Null(scope.TenantId);

        TenantScopeResolver.Resolve(new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("tenant_id", tenantId.ToString())], "test")), scope);
        Assert.Equal(tenantId, scope.TenantId);

        TenantScopeResolver.Resolve(new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("tenant_id", "not-a-guid")], "test")), scope);
        Assert.Null(scope.TenantId);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        public IdentityDbContext Db { get; }
        public TenantScope Scope { get; }
        public OnboardingService Service { get; }

        private Fixture(SqliteConnection connection, IdentityDbContext db, TenantScope scope)
        {
            this.connection = connection;
            Db = db;
            Scope = scope;
            Service = new OnboardingService(db, scope);
        }

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var scope = new TenantScope();
            var options = new DbContextOptionsBuilder<IdentityDbContext>()
                .UseSqlite(connection).Options;
            var db = new IdentityDbContext(options, scope);
            await db.Database.EnsureCreatedAsync();
            return new Fixture(connection, db, scope);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
