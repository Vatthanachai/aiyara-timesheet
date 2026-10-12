using Aiyara.Identities.Databases;
using Aiyara.Identities.Models.Tenancy;
using Aiyara.Identities.Services.Authentication;
using Aiyara.Identities.Services.Onboarding;
using Aiyara.Timesheet.Contracts.Onboarding.V1;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Aiyara.Phase1.Tests;

public sealed class PlatformConsoleServiceTests : IAsyncLifetime
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private IdentityDbContext db = null!;
    private TenantScope scope = null!;
    private PlatformOrganizationService organizations = null!;
    private PlatformConsoleService console = null!;
    private readonly Guid platformAdminId = Guid.NewGuid();

    [Fact]
    public async Task Overview_counts_customer_organizations_by_status_and_missing_active_admin()
    {
        var acme = await CreateAsync("Acme", "acme", "admin@acme.test");
        var beta = await CreateAsync("Beta", "beta", "admin@beta.test");
        await CreateAsync("Gamma", "gamma", "admin@gamma.test");
        await ActivateAdminAsync(acme.TenantId);
        await organizations.SetStatusAsync(beta.TenantId, platformAdminId, 0,
            new SetPlatformOrganizationStatusRequest(true, "Past due"), default);

        var overview = await console.GetOverviewAsync(default);

        Assert.Equal(3, overview.OrganizationCount);
        Assert.Equal(2, overview.ActiveCount);
        Assert.Equal(1, overview.SuspendedCount);
        Assert.Equal(2, overview.AwaitingAdministratorCount);
        Assert.Equal(3, overview.NewestOrganizations.Count);
        Assert.DoesNotContain(overview.NewestOrganizations, x => x.OrganizationCode == "AIYA001");
        Assert.Equal(4, overview.RecentActivity.Count);
        Assert.Equal("organization.suspended", overview.RecentActivity[0].Action);
    }

    [Fact]
    public async Task Organization_detail_reports_counts_and_admins_but_hides_the_platform_tenant()
    {
        var acme = await CreateAsync("Acme", "acme", "admin@acme.test");
        await ActivateAdminAsync(acme.TenantId);
        db.Accounts.Add(new Account { Id = Guid.NewGuid(), Email = "staff@acme.test", CreatedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();
        db.Memberships.Add(new Membership { Id = Guid.NewGuid(), TenantId = acme.TenantId,
            AccountId = (await db.Accounts.SingleAsync(x => x.Email == "staff@acme.test")).Id,
            Role = TenantRole.Employee, Status = MembershipStatus.PendingActivation, CreatedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var detail = await console.GetOrganizationAsync(acme.TenantId, default);

        Assert.Equal("Acme", detail.Name);
        Assert.Equal("Active", detail.Status);
        Assert.Equal(1, detail.ActiveMemberCount);
        Assert.Equal(1, detail.PendingMemberCount);
        Assert.Equal([new PlatformOrganizationAdminSummary("admin@acme.test", "Active")], detail.Admins);
        var hidden = await Assert.ThrowsAsync<PlatformOrganizationException>(() =>
            console.GetOrganizationAsync(PlatformAdminBootstrap.PlatformTenantId, default));
        Assert.Equal(PlatformOrganizationFailure.NotFound, hidden.Failure);
    }

    [Fact]
    public async Task Audit_list_filters_by_organization_action_actor_and_date_and_pages_newest_first()
    {
        var acme = await CreateAsync("Acme", "acme", "admin@acme.test");
        var beta = await CreateAsync("Beta", "beta", "admin@beta.test");
        await organizations.UpdateAsync(acme.TenantId, platformAdminId, 0,
            new UpdatePlatformOrganizationRequest("Acme Renamed", "acme", "Rebrand"), default);

        var all = await console.ListAuditAsync(new PlatformAuditQuery(null, null, null, null, null), default);
        var forAcme = await console.ListAuditAsync(new PlatformAuditQuery(acme.TenantId, null, null, null, null), default);
        var updates = await console.ListAuditAsync(new PlatformAuditQuery(null, null, "organization.updated", null, null), default);
        var byActor = await console.ListAuditAsync(new PlatformAuditQuery(null, "PLATFORM@", null, null, null), default);
        var byOther = await console.ListAuditAsync(new PlatformAuditQuery(null, "someone-else", null, null, null), default);
        var future = await console.ListAuditAsync(new PlatformAuditQuery(null, null, null, DateTime.UtcNow.AddMinutes(1), null), default);
        var firstPage = await console.ListAuditAsync(new PlatformAuditQuery(null, null, null, null, null, 1, 2), default);
        var secondPage = await console.ListAuditAsync(new PlatformAuditQuery(null, null, null, null, null, 2, 2), default);

        Assert.Equal(3, all.TotalCount);
        Assert.Equal("organization.updated", all.Items[0].Action);
        Assert.Equal("Acme Renamed", all.Items[0].OrganizationName);
        Assert.Equal(acme.OrganizationCode, all.Items[0].OrganizationCode);
        Assert.Equal("platform@aiyara.test", all.Items[0].ActorEmail);
        Assert.Equal("Rebrand", all.Items[0].Reason);
        Assert.Equal(2, forAcme.TotalCount);
        Assert.All(forAcme.Items, x => Assert.Equal(acme.TenantId, x.TenantId));
        Assert.Single(updates.Items);
        Assert.Equal(3, byActor.TotalCount);
        Assert.Equal(0, byOther.TotalCount);
        Assert.Equal(0, future.TotalCount);
        Assert.Equal(2, firstPage.Items.Count);
        Assert.Single(secondPage.Items);
        Assert.Equal(beta.TenantId, firstPage.Items[1].TenantId);
        Assert.Equal(acme.TenantId, secondPage.Items[0].TenantId);
        Assert.Equal("organization.created", secondPage.Items[0].Action);
        var invalid = await Assert.ThrowsAsync<PlatformOrganizationException>(() => console.ListAuditAsync(
            new PlatformAuditQuery(null, null, null, DateTime.UtcNow, DateTime.UtcNow.AddDays(-1)), default));
        Assert.Equal(PlatformOrganizationFailure.InvalidInput, invalid.Failure);
    }

    public async Task InitializeAsync()
    {
        await connection.OpenAsync();
        scope = new TenantScope();
        db = new IdentityDbContext(new DbContextOptionsBuilder<IdentityDbContext>()
            .UseSqlite(connection).Options, scope);
        await db.Database.EnsureCreatedAsync();
        var now = DateTime.UtcNow;
        db.Tenants.Add(new Tenant { Id = PlatformAdminBootstrap.PlatformTenantId, Name = "Aiyara Platform",
            Slug = "platform-system", OrganizationCode = "AIYA001", CreatedAtUtc = now });
        db.Accounts.Add(new Account { Id = platformAdminId, Email = "platform@aiyara.test",
            IsPlatformAdmin = true, CreatedAtUtc = now });
        db.Memberships.Add(new Membership { Id = Guid.NewGuid(), TenantId = PlatformAdminBootstrap.PlatformTenantId,
            AccountId = platformAdminId, Role = TenantRole.PlatformAdmin,
            Status = MembershipStatus.Active, CreatedAtUtc = now });
        await db.SaveChangesAsync();
        organizations = new PlatformOrganizationService(db, scope,
            new PlatformOrganizationManagementTests.FakeRevocations());
        console = new PlatformConsoleService(db, organizations);
    }

    public async Task DisposeAsync()
    {
        await db.DisposeAsync();
        await connection.DisposeAsync();
    }

    private async Task<PlatformOrganizationUpdateResult> CreateAsync(string name, string slug, string email)
    {
        var created = await organizations.CreateAsync(platformAdminId, 0,
            new CreatePlatformOrganizationRequest(name, slug, email), default);
        // Distinct timestamps keep newest-first ordering deterministic.
        await Task.Delay(5);
        return created;
    }

    private async Task ActivateAdminAsync(Guid tenantId)
    {
        var membership = await db.Memberships.IgnoreQueryFilters().SingleAsync(x =>
            x.TenantId == tenantId && x.Role == TenantRole.TenantAdmin);
        membership.Status = MembershipStatus.Active;
        await db.SaveChangesAsync();
    }
}
