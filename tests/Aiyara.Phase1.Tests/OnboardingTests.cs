using Aiyara.Identities.Databases;
using Aiyara.Identities.Models.Tenancy;
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
    public async Task Tenant_creation_assigns_unique_seven_character_organization_codes()
    {
        await using var fixture = await Fixture.CreateAsync();
        var first = await fixture.Service.CreateTenantAsync(
            new CreateTenantRequest("Acme Corporation", "acme-corp", "admin@acme.test"), default);
        var second = await fixture.Service.CreateTenantAsync(
            new CreateTenantRequest("Acme Consulting", "acme-consulting", "admin2@acme.test"), default);

        Assert.Matches("^ACME[0-9]{3}$", first.OrganizationCode);
        Assert.Matches("^ACME[0-9]{3}$", second.OrganizationCode);
        Assert.NotEqual(first.OrganizationCode, second.OrganizationCode);
        Assert.Equal(first.OrganizationCode, await fixture.Db.Tenants.IgnoreQueryFilters()
            .Where(x => x.Id == first.TenantId).Select(x => x.OrganizationCode).SingleAsync());
    }

    [Fact]
    public async Task Identity_ids_use_component_v8_with_random_region_and_shard_values()
    {
        await using var fixture = await Fixture.CreateAsync();
        var tenant = await fixture.Service.CreateTenantAsync(
            new CreateTenantRequest("Guid test", "guid-test", "admin@guid.test"), default);
        foreach (var id in new[] { tenant.TenantId, tenant.AccountId, tenant.MembershipId })
        {
            var bytes = id.ToByteArray();
            Assert.InRange(bytes[0], (byte)1, (byte)9);
            Assert.InRange(bytes[1], (byte)1, (byte)9);
            Assert.Equal(8, bytes[6] >> 4);
            Assert.Equal(2, bytes[8] >> 6);
        }
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

        Assert.False(string.IsNullOrEmpty(first.OnboardingKey));
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
    public async Task Platform_organization_list_searches_admins_and_excludes_platform_tenant()
    {
        await using var fixture = await Fixture.CreateAsync();
        var first = await fixture.Service.CreateTenantAsync(
            new CreateTenantRequest("First Studio", "first-studio", "admin@first.test"), default);
        var second = await fixture.Service.CreateTenantAsync(
            new CreateTenantRequest("Second Studio", "second-studio", "admin@second.test"), default);
        fixture.Db.Tenants.Add(new Tenant
        {
            Id = Guid.NewGuid(), Slug = "platform-system", Name = "Platform System",
            OrganizationCode = "SYS0001", CreatedAtUtc = DateTime.UtcNow
        });
        fixture.Scope.TenantId = first.TenantId;
        var firstAdmin = await fixture.Db.Memberships.SingleAsync();
        firstAdmin.Status = MembershipStatus.Active;
        await fixture.Db.SaveChangesAsync();
        fixture.Scope.TenantId = null;

        var service = new PlatformOrganizationService(fixture.Db, fixture.Scope,
            new PlatformOrganizationManagementTests.FakeRevocations());
        var filtered = await service.ListAsync("ADMIN@SECOND.TEST", 1, 25, default);
        var item = Assert.Single(filtered.Items);
        Assert.Equal(second.TenantId, item.TenantId);
        Assert.Equal("PendingActivation", Assert.Single(item.Admins).Status);
        Assert.DoesNotContain(filtered.Items, tenant => tenant.Slug == "platform-system");

        var paged = await service.ListAsync(null, 999, 1, default);
        Assert.Equal(2, paged.Page);
        Assert.Equal(2, paged.TotalCount);
        Assert.Single(paged.Items);
        Assert.Contains(paged.Items, tenant => tenant.TenantId == first.TenantId);
        Assert.DoesNotContain(paged.Items, tenant => tenant.TenantId == second.TenantId);
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
