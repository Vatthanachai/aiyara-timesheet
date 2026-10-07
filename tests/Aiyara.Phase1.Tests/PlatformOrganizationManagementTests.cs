using Aiyara.Identities.Databases;
using Aiyara.Identities.Models.Tenancy;
using Aiyara.Identities.Services.Authentication;
using Aiyara.Identities.Services.Onboarding;
using Aiyara.Timesheet.Contracts.Onboarding.V1;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Aiyara.Phase1.Tests;

public sealed class PlatformOrganizationManagementTests
{
    [Fact]
    public async Task Platform_admin_can_edit_name_and_slug_without_changing_login_code()
    {
        await using var fixture = await Fixture.CreateAsync();
        var tenant = await fixture.Onboarding.CreateTenantAsync(
            new CreateTenantRequest("Original name", "original-slug", "admin@original.test"), default);

        var updated = await fixture.Service.UpdateAsync(tenant.TenantId, fixture.PlatformAdminId,
            0, new UpdatePlatformOrganizationRequest("New name", "new-slug", "Customer request"), default);

        Assert.Equal(tenant.TenantId, updated.TenantId);
        Assert.Equal(tenant.OrganizationCode, updated.OrganizationCode);
        Assert.Equal("New name", updated.Name);
        Assert.Equal("new-slug", updated.Slug);
        Assert.Equal("Active", updated.Status);
        var audit = await fixture.Db.PlatformOrganizationAudits.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(fixture.PlatformAdminId, audit.ActorAccountId);
        Assert.Equal(tenant.TenantId, audit.TenantId);
        Assert.Equal("organization.updated", audit.Action);
        Assert.Contains("Original name", audit.BeforeJson);
        Assert.Contains("New name", audit.AfterJson);
        Assert.Equal("Customer request", audit.Reason);
    }

    [Fact]
    public async Task Organization_update_rejects_duplicate_slug_and_non_platform_actor()
    {
        await using var fixture = await Fixture.CreateAsync();
        var first = await fixture.Onboarding.CreateTenantAsync(
            new CreateTenantRequest("First", "first", "admin@first.test"), default);
        var second = await fixture.Onboarding.CreateTenantAsync(
            new CreateTenantRequest("Second", "second", "admin@second.test"), default);

        var conflict = await Assert.ThrowsAsync<PlatformOrganizationException>(() =>
            fixture.Service.UpdateAsync(second.TenantId, fixture.PlatformAdminId, 0,
                new UpdatePlatformOrganizationRequest("Second", "FIRST", null), default));
        Assert.Equal(PlatformOrganizationFailure.Conflict, conflict.Failure);

        var forbidden = await Assert.ThrowsAsync<PlatformOrganizationException>(() =>
            fixture.Service.UpdateAsync(first.TenantId, Guid.NewGuid(), 0,
                new UpdatePlatformOrganizationRequest("Changed", "changed", null), default));
        Assert.Equal(PlatformOrganizationFailure.Unauthorized, forbidden.Failure);
        Assert.Equal("First", await fixture.Db.Tenants.IgnoreQueryFilters()
            .Where(x => x.Id == first.TenantId).Select(x => x.Name).SingleAsync());
    }

    [Fact]
    public async Task Suspending_and_reactivating_preserves_tenant_and_invalidates_policy_cache()
    {
        await using var fixture = await Fixture.CreateAsync();
        var tenant = await fixture.Onboarding.CreateTenantAsync(
            new CreateTenantRequest("Acme", "acme", "admin@acme.test"), default);

        var suspended = await fixture.Service.SetStatusAsync(tenant.TenantId,
            fixture.PlatformAdminId, 0, new SetPlatformOrganizationStatusRequest(true, "Past due"), default);
        Assert.Equal("Suspended", suspended.Status);
        Assert.Equal(TenantStatus.Suspended, await fixture.Db.Tenants.IgnoreQueryFilters()
            .Where(x => x.Id == tenant.TenantId).Select(x => x.Status).SingleAsync());

        var reactivated = await fixture.Service.SetStatusAsync(tenant.TenantId,
            fixture.PlatformAdminId, 0, new SetPlatformOrganizationStatusRequest(false, "Paid"), default);
        Assert.Equal("Active", reactivated.Status);
        Assert.Equal(tenant.OrganizationCode, reactivated.OrganizationCode);
        Assert.Equal(tenant.TenantId, reactivated.TenantId);
        Assert.Equal(2, fixture.Revocations.Published.Count);
        Assert.Equal(2, fixture.Revocations.Completed.Count);
        Assert.Equal(2, await fixture.Db.PlatformOrganizationAudits.IgnoreQueryFilters()
            .CountAsync(x => x.TenantId == tenant.TenantId));

        await fixture.Service.SetStatusAsync(tenant.TenantId, fixture.PlatformAdminId, 0,
            new SetPlatformOrganizationStatusRequest(false, null), default);
        Assert.Equal(3, fixture.Revocations.Completed.Count);
        Assert.Equal(2, await fixture.Db.PlatformOrganizationAudits.IgnoreQueryFilters()
            .CountAsync(x => x.TenantId == tenant.TenantId));
    }

    [Fact]
    public async Task Platform_admin_can_invite_an_additional_tenant_admin_without_storing_code()
    {
        await using var fixture = await Fixture.CreateAsync();
        var tenant = await fixture.Onboarding.CreateTenantAsync(
            new CreateTenantRequest("Acme", "acme", "admin@acme.test"), default);

        var issued = await fixture.Service.InviteAdministratorAsync(tenant.TenantId,
            fixture.PlatformAdminId, 0, new InvitePlatformOrganizationAdminRequest("second@acme.test"), default);

        Assert.Equal(tenant.TenantId, issued.TenantId);
        Assert.NotEmpty(issued.Code);
        fixture.Scope.TenantId = tenant.TenantId;
        var invitation = await fixture.Db.Invitations.SingleAsync();
        Assert.Equal("second@acme.test", invitation.Email);
        Assert.Equal(TenantRole.TenantAdmin, invitation.Role);
        Assert.Equal(fixture.PlatformAdminId, invitation.InvitedByAccountId);
        Assert.NotEqual(issued.Code, invitation.TokenHash);
    }

    [Fact]
    public async Task Platform_tenant_cannot_be_suspended_or_edited()
    {
        await using var fixture = await Fixture.CreateAsync();

        var exception = await Assert.ThrowsAsync<PlatformOrganizationException>(() =>
            fixture.Service.SetStatusAsync(PlatformAdminBootstrap.PlatformTenantId,
                fixture.PlatformAdminId, 0, new SetPlatformOrganizationStatusRequest(true, null), default));

        Assert.Equal(PlatformOrganizationFailure.ProtectedTenant, exception.Failure);
    }

    internal sealed class FakeRevocations : ISessionRevocationPublisher
    {
        public List<Guid> Published { get; } = [];
        public List<(Guid TenantId, long Version)> Completed { get; } = [];
        public Task PublishAccountAsync(Guid tenantId, Guid accountId, long sessionVersion,
            CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PublishTenantPolicyAsync(Guid tenantId, CancellationToken cancellationToken)
        {
            Published.Add(tenantId);
            return Task.CompletedTask;
        }
        public Task CompleteTenantPolicyAsync(Guid tenantId, long policyVersion,
            CancellationToken cancellationToken)
        {
            Completed.Add((tenantId, policyVersion));
            return Task.CompletedTask;
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        public IdentityDbContext Db { get; }
        public TenantScope Scope { get; }
        public OnboardingService Onboarding { get; }
        public PlatformOrganizationService Service { get; }
        public FakeRevocations Revocations { get; } = new();
        public Guid PlatformAdminId { get; } = Guid.NewGuid();

        private Fixture(SqliteConnection connection, IdentityDbContext db, TenantScope scope)
        {
            this.connection = connection;
            Db = db;
            Scope = scope;
            Onboarding = new OnboardingService(db, Scope);
            Service = new PlatformOrganizationService(db, Scope, Revocations);
        }

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var scope = new TenantScope();
            var db = new IdentityDbContext(new DbContextOptionsBuilder<IdentityDbContext>()
                .UseSqlite(connection).Options, scope);
            await db.Database.EnsureCreatedAsync();
            var fixture = new Fixture(connection, db, scope);
            var now = DateTime.UtcNow;
            db.Tenants.Add(new Tenant { Id = PlatformAdminBootstrap.PlatformTenantId,
                Name = "Aiyara Platform", Slug = "platform-system", OrganizationCode = "AIYA001",
                CreatedAtUtc = now });
            db.Accounts.Add(new Account { Id = fixture.PlatformAdminId,
                Email = "platform@aiyara.test", IsPlatformAdmin = true, CreatedAtUtc = now });
            db.Memberships.Add(new Membership { Id = Guid.NewGuid(),
                TenantId = PlatformAdminBootstrap.PlatformTenantId,
                AccountId = fixture.PlatformAdminId, Role = TenantRole.PlatformAdmin,
                Status = MembershipStatus.Active, CreatedAtUtc = now });
            await db.SaveChangesAsync();
            return fixture;
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
