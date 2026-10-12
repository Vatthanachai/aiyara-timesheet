using Aiyara.Identities.Databases;
using Aiyara.Identities.Models.Tenancy;
using Aiyara.Identities.Services.Authentication;
using Aiyara.Identities.Services.Onboarding;
using Aiyara.Timesheet.Contracts.Onboarding.V1;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Aiyara.Phase1.Tests;

/// <summary>Phase C of the Platform Admin console: the Platform Admin roster (C1) and
/// demoting or removing tenant administrators (C2).</summary>
public sealed class PlatformPeopleManagementTests
{
    [Fact]
    public async Task Inviting_a_platform_admin_creates_a_pending_audited_dedicated_account()
    {
        await using var fixture = await Fixture.CreateAsync();

        await fixture.Roster.InviteAsync(fixture.PlatformAdminId, 0,
            new InvitePlatformAdminRequest(" Second@Aiyara.test "), default);

        var roster = await fixture.Roster.ListAsync(fixture.PlatformAdminId, default);
        var invited = Assert.Single(roster, x => x.Email == "second@aiyara.test");
        Assert.Equal("PendingActivation", invited.Status);
        var account = await fixture.Db.Accounts.SingleAsync(x => x.Email == "second@aiyara.test");
        Assert.True(account.IsPlatformAdmin);
        var membership = await fixture.Db.Memberships.IgnoreQueryFilters().SingleAsync(x => x.AccountId == account.Id);
        Assert.Equal(PlatformAdminBootstrap.PlatformTenantId, membership.TenantId);
        Assert.Equal(TenantRole.PlatformAdmin, membership.Role);
        var audit = await fixture.Db.PlatformOrganizationAudits.IgnoreQueryFilters().SingleAsync();
        Assert.Equal("platform_admin.invited", audit.Action);
        Assert.Equal(PlatformAdminBootstrap.PlatformTenantId, audit.TenantId);
        Assert.Equal(fixture.PlatformAdminId, audit.ActorAccountId);
        Assert.Contains("second@aiyara.test", audit.AfterJson);
    }

    [Fact]
    public async Task Platform_admin_invitation_rejects_customer_accounts_existing_admins_and_outsiders()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Onboarding.CreateTenantAsync(
            new CreateTenantRequest("Acme", "acme", "owner@acme.test"), default);

        var customer = await Assert.ThrowsAsync<PlatformOrganizationException>(() =>
            fixture.Roster.InviteAsync(fixture.PlatformAdminId, 0, new InvitePlatformAdminRequest("owner@acme.test"), default));
        var existing = await Assert.ThrowsAsync<PlatformOrganizationException>(() =>
            fixture.Roster.InviteAsync(fixture.PlatformAdminId, 0, new InvitePlatformAdminRequest("platform@aiyara.test"), default));
        var invalid = await Assert.ThrowsAsync<PlatformOrganizationException>(() =>
            fixture.Roster.InviteAsync(fixture.PlatformAdminId, 0, new InvitePlatformAdminRequest("not-an-email"), default));
        var outsider = await Assert.ThrowsAsync<PlatformOrganizationException>(() =>
            fixture.Roster.InviteAsync(Guid.NewGuid(), 0, new InvitePlatformAdminRequest("new@aiyara.test"), default));

        Assert.Equal(PlatformOrganizationFailure.Conflict, customer.Failure);
        Assert.Equal(PlatformOrganizationFailure.Conflict, existing.Failure);
        Assert.Equal(PlatformOrganizationFailure.InvalidInput, invalid.Failure);
        Assert.Equal(PlatformOrganizationFailure.Unauthorized, outsider.Failure);
        Assert.False(await fixture.Db.Accounts.AnyAsync(x => x.Email == "new@aiyara.test"));
        Assert.False((await fixture.Db.Accounts.SingleAsync(x => x.Email == "owner@acme.test")).IsPlatformAdmin);
    }

    [Fact]
    public async Task Deactivating_a_platform_admin_ends_their_sessions_and_blocks_platform_access()
    {
        await using var fixture = await Fixture.CreateAsync();
        var second = await fixture.AddPlatformAdminAsync("second@aiyara.test");
        await fixture.AddRefreshSessionAsync(second, PlatformAdminBootstrap.PlatformTenantId);

        await fixture.Roster.SetStatusAsync(fixture.PlatformAdminId, 0,
            new SetPlatformAdminStatusRequest("second@aiyara.test", false, "Left the company"), default);

        var membership = await fixture.Db.Memberships.IgnoreQueryFilters().SingleAsync(x => x.AccountId == second);
        Assert.Equal(MembershipStatus.Deactivated, membership.Status);
        Assert.Equal(1, (await fixture.Db.Accounts.SingleAsync(x => x.Id == second)).SessionVersion);
        Assert.All(await fixture.Db.RefreshSessions.IgnoreQueryFilters().Where(x => x.AccountId == second).ToListAsync(),
            session => Assert.NotNull(session.RevokedAtUtc));
        Assert.Contains((PlatformAdminBootstrap.PlatformTenantId, second, 1L), fixture.Revocations.Accounts);
        var audit = await fixture.Db.PlatformOrganizationAudits.IgnoreQueryFilters().SingleAsync();
        Assert.Equal("platform_admin.deactivated", audit.Action);
        Assert.Equal("Left the company", audit.Reason);
        Assert.Contains("second@aiyara.test", audit.AfterJson);

        // The deactivated admin can no longer act on the platform.
        var blocked = await Assert.ThrowsAsync<PlatformOrganizationException>(() =>
            fixture.Roster.InviteAsync(second, 1, new InvitePlatformAdminRequest("third@aiyara.test"), default));
        Assert.Equal(PlatformOrganizationFailure.Unauthorized, blocked.Failure);
        Assert.Equal("Deactivated", (await fixture.Roster.ListAsync(fixture.PlatformAdminId, default)).Single(x => x.Email == "second@aiyara.test").Status);
    }

    [Fact]
    public async Task A_platform_admin_cannot_deactivate_themselves()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddPlatformAdminAsync("second@aiyara.test");

        var self = await Assert.ThrowsAsync<PlatformOrganizationException>(() =>
            fixture.Roster.SetStatusAsync(fixture.PlatformAdminId, 0,
                new SetPlatformAdminStatusRequest("PLATFORM@aiyara.test", false, null), default));

        Assert.Equal(PlatformOrganizationFailure.SelfChange, self.Failure);
        Assert.Equal(MembershipStatus.Active, (await fixture.Db.Memberships.IgnoreQueryFilters()
            .SingleAsync(x => x.AccountId == fixture.PlatformAdminId)).Status);
        Assert.Empty(await fixture.Db.PlatformOrganizationAudits.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task Reactivating_restores_access_or_restarts_activation_when_no_password_was_set()
    {
        await using var fixture = await Fixture.CreateAsync();
        var withPassword = await fixture.AddPlatformAdminAsync("active@aiyara.test", passwordHash: "hash");
        var neverActivated = await fixture.AddPlatformAdminAsync("pending@aiyara.test");
        foreach (var email in new[] { "active@aiyara.test", "pending@aiyara.test" })
            await fixture.Roster.SetStatusAsync(fixture.PlatformAdminId, 0,
                new SetPlatformAdminStatusRequest(email, false, null), default);

        foreach (var email in new[] { "active@aiyara.test", "pending@aiyara.test" })
            await fixture.Roster.SetStatusAsync(fixture.PlatformAdminId, 0,
                new SetPlatformAdminStatusRequest(email, true, "Returned"), default);

        var memberships = await fixture.Db.Memberships.IgnoreQueryFilters().ToDictionaryAsync(x => x.AccountId);
        Assert.Equal(MembershipStatus.Active, memberships[withPassword].Status);
        Assert.Equal(MembershipStatus.PendingActivation, memberships[neverActivated].Status);
        Assert.Equal(2, await fixture.Db.PlatformOrganizationAudits.IgnoreQueryFilters()
            .CountAsync(x => x.Action == "platform_admin.reactivated" && x.Reason == "Returned"));
    }

    [Fact]
    public async Task Bootstrap_does_not_revive_a_deactivated_platform_admin()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddPlatformAdminAsync("bootstrap@aiyara.test", passwordHash: "hash");
        await fixture.Roster.SetStatusAsync(fixture.PlatformAdminId, 0,
            new SetPlatformAdminStatusRequest("bootstrap@aiyara.test", false, null), default);

        var activationEmail = await new PlatformAdminBootstrap(fixture.Db, fixture.Scope)
            .EnsureAsync("bootstrap@aiyara.test", default);

        Assert.Null(activationEmail);
        Assert.Equal("Deactivated", (await fixture.Roster.ListAsync(fixture.PlatformAdminId, default))
            .Single(x => x.Email == "bootstrap@aiyara.test").Status);
    }

    [Fact]
    public async Task Demoting_a_tenant_admin_keeps_the_member_and_ends_their_sessions()
    {
        await using var fixture = await Fixture.CreateAsync();
        var tenant = await fixture.CreateOrganizationWithAdminsAsync("owner@acme.test", "second@acme.test");
        var second = await fixture.Db.Accounts.Where(x => x.Email == "second@acme.test").Select(x => x.Id).SingleAsync();
        await fixture.AddRefreshSessionAsync(second, tenant);

        await fixture.Organizations.DemoteAdministratorAsync(tenant, fixture.PlatformAdminId, 0,
            new ChangePlatformOrganizationAdminRequest("Second@Acme.test", "Role change"), default);

        var membership = await fixture.Db.Memberships.IgnoreQueryFilters().SingleAsync(x => x.AccountId == second);
        Assert.Equal(TenantRole.Employee, membership.Role);
        Assert.Equal(MembershipStatus.Active, membership.Status);
        Assert.Equal(1, (await fixture.Db.Accounts.SingleAsync(x => x.Id == second)).SessionVersion);
        Assert.All(await fixture.Db.RefreshSessions.IgnoreQueryFilters().Where(x => x.AccountId == second).ToListAsync(),
            session => Assert.NotNull(session.RevokedAtUtc));
        Assert.Contains((tenant, second, 1L), fixture.Revocations.Accounts);
        var audit = await fixture.Db.PlatformOrganizationAudits.IgnoreQueryFilters().SingleAsync();
        Assert.Equal("administrator.demoted", audit.Action);
        Assert.Equal(tenant, audit.TenantId);
        Assert.Equal("Role change", audit.Reason);
        Assert.Contains("TenantAdmin", audit.BeforeJson);
        Assert.Contains("Employee", audit.AfterJson);
    }

    [Fact]
    public async Task Removing_a_tenant_admin_deactivates_their_membership()
    {
        await using var fixture = await Fixture.CreateAsync();
        var tenant = await fixture.CreateOrganizationWithAdminsAsync("owner@acme.test", "second@acme.test");

        await fixture.Organizations.RemoveAdministratorAsync(tenant, fixture.PlatformAdminId, 0,
            new ChangePlatformOrganizationAdminRequest("second@acme.test", null), default);

        var second = await fixture.Db.Accounts.SingleAsync(x => x.Email == "second@acme.test");
        var membership = await fixture.Db.Memberships.IgnoreQueryFilters().SingleAsync(x => x.AccountId == second.Id);
        Assert.Equal(MembershipStatus.Deactivated, membership.Status);
        Assert.Equal(1, second.SessionVersion);
        Assert.Equal("administrator.removed", (await fixture.Db.PlatformOrganizationAudits.IgnoreQueryFilters().SingleAsync()).Action);
    }

    [Fact]
    public async Task The_last_active_tenant_admin_cannot_be_demoted_or_removed()
    {
        await using var fixture = await Fixture.CreateAsync();
        var tenant = await fixture.CreateOrganizationWithAdminsAsync("owner@acme.test");
        // A pending administrator does not count as an active one.
        var pendingId = Guid.NewGuid();
        fixture.Db.Accounts.Add(new Account { Id = pendingId, Email = "pending@acme.test", CreatedAtUtc = DateTime.UtcNow });
        fixture.Db.Memberships.Add(new Membership { Id = Guid.NewGuid(), TenantId = tenant, AccountId = pendingId,
            Role = TenantRole.TenantAdmin, Status = MembershipStatus.PendingActivation, CreatedAtUtc = DateTime.UtcNow });
        await fixture.Db.SaveChangesAsync();
        var auditCount = await fixture.Db.PlatformOrganizationAudits.IgnoreQueryFilters().CountAsync();

        var demote = await Assert.ThrowsAsync<PlatformOrganizationException>(() =>
            fixture.Organizations.DemoteAdministratorAsync(tenant, fixture.PlatformAdminId, 0,
                new ChangePlatformOrganizationAdminRequest("owner@acme.test", null), default));
        var remove = await Assert.ThrowsAsync<PlatformOrganizationException>(() =>
            fixture.Organizations.RemoveAdministratorAsync(tenant, fixture.PlatformAdminId, 0,
                new ChangePlatformOrganizationAdminRequest("owner@acme.test", null), default));

        Assert.Equal(PlatformOrganizationFailure.LastAdministrator, demote.Failure);
        Assert.Equal(PlatformOrganizationFailure.LastAdministrator, remove.Failure);
        var owner = await fixture.Db.Accounts.SingleAsync(x => x.Email == "owner@acme.test");
        var membership = await fixture.Db.Memberships.IgnoreQueryFilters().SingleAsync(x => x.AccountId == owner.Id);
        Assert.Equal((TenantRole.TenantAdmin, MembershipStatus.Active), (membership.Role, membership.Status));
        Assert.Equal(0, owner.SessionVersion);
        Assert.Equal(auditCount, await fixture.Db.PlatformOrganizationAudits.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task Administrator_changes_reject_unknown_admins_the_platform_tenant_and_outsiders()
    {
        await using var fixture = await Fixture.CreateAsync();
        var tenant = await fixture.CreateOrganizationWithAdminsAsync("owner@acme.test", "second@acme.test");

        var unknown = await Assert.ThrowsAsync<PlatformOrganizationException>(() =>
            fixture.Organizations.RemoveAdministratorAsync(tenant, fixture.PlatformAdminId, 0,
                new ChangePlatformOrganizationAdminRequest("nobody@acme.test", null), default));
        var platform = await Assert.ThrowsAsync<PlatformOrganizationException>(() =>
            fixture.Organizations.DemoteAdministratorAsync(PlatformAdminBootstrap.PlatformTenantId,
                fixture.PlatformAdminId, 0, new ChangePlatformOrganizationAdminRequest("platform@aiyara.test", null), default));
        var outsider = await Assert.ThrowsAsync<PlatformOrganizationException>(() =>
            fixture.Organizations.DemoteAdministratorAsync(tenant, Guid.NewGuid(), 0,
                new ChangePlatformOrganizationAdminRequest("second@acme.test", null), default));

        Assert.Equal(PlatformOrganizationFailure.NotFound, unknown.Failure);
        Assert.Equal(PlatformOrganizationFailure.ProtectedTenant, platform.Failure);
        Assert.Equal(PlatformOrganizationFailure.Unauthorized, outsider.Failure);
    }

    private sealed class RecordingRevocations : ISessionRevocationPublisher
    {
        public List<(Guid TenantId, Guid AccountId, long Version)> Accounts { get; } = [];
        public Task PublishAccountAsync(Guid tenantId, Guid accountId, long sessionVersion,
            CancellationToken cancellationToken)
        {
            Accounts.Add((tenantId, accountId, sessionVersion));
            return Task.CompletedTask;
        }
        public Task PublishTenantPolicyAsync(Guid tenantId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task CompleteTenantPolicyAsync(Guid tenantId, long policyVersion,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection? connection;
        private readonly bool usesPostgres;
        public IdentityDbContext Db { get; }
        public TenantScope Scope { get; }
        public OnboardingService Onboarding { get; }
        public PlatformOrganizationService Organizations { get; }
        public PlatformAdminRosterService Roster { get; }
        public RecordingRevocations Revocations { get; } = new();
        public Guid PlatformAdminId { get; } = Guid.NewGuid();

        private Fixture(SqliteConnection? connection, bool usesPostgres, IdentityDbContext db, TenantScope scope)
        {
            this.connection = connection;
            this.usesPostgres = usesPostgres;
            Db = db;
            Scope = scope;
            Onboarding = new OnboardingService(db, scope);
            Organizations = new PlatformOrganizationService(db, scope, Revocations);
            Roster = new PlatformAdminRosterService(db, scope, Organizations);
        }

        /// <summary>Uses in-memory SQLite by default. When AIYARA_TEST_POSTGRES holds a server
        /// connection string, each test instead gets a throwaway PostgreSQL database built from the
        /// real migrations, because SQLite translates some EF queries differently.</summary>
        public static async Task<Fixture> CreateAsync()
        {
            var scope = new TenantScope();
            Fixture fixture;
            if (Environment.GetEnvironmentVariable("AIYARA_TEST_POSTGRES") is { Length: > 0 } server)
            {
                var database = $"identity_people_test_{Guid.NewGuid():N}";
                var options = new DbContextOptionsBuilder<IdentityDbContext>()
                    .UseNpgsql(new Npgsql.NpgsqlConnectionStringBuilder(server) { Database = database }.ConnectionString)
                    .Options;
                var postgres = new IdentityDbContext(options, scope);
                await postgres.Database.MigrateAsync();
                fixture = new Fixture(null, usesPostgres: true, postgres, scope);
            }
            else
            {
                var connection = new SqliteConnection("Data Source=:memory:");
                await connection.OpenAsync();
                var sqlite = new IdentityDbContext(new DbContextOptionsBuilder<IdentityDbContext>()
                    .UseSqlite(connection).Options, scope);
                await sqlite.Database.EnsureCreatedAsync();
                fixture = new Fixture(connection, usesPostgres: false, sqlite, scope);
            }
            var db = fixture.Db;
            db.Tenants.Add(new Tenant { Id = PlatformAdminBootstrap.PlatformTenantId,
                Name = "Aiyara Platform", Slug = "platform-system", OrganizationCode = "AIYA001",
                CreatedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();
            await fixture.AddPlatformAdminAsync("platform@aiyara.test", fixture.PlatformAdminId, "hash");
            return fixture;
        }

        public async Task<Guid> AddPlatformAdminAsync(string email, Guid? id = null, string? passwordHash = null)
        {
            var accountId = id ?? Guid.NewGuid();
            Db.Accounts.Add(new Account { Id = accountId, Email = email, IsPlatformAdmin = true,
                PasswordHash = passwordHash, CreatedAtUtc = DateTime.UtcNow });
            Db.Memberships.Add(new Membership { Id = Guid.NewGuid(), TenantId = PlatformAdminBootstrap.PlatformTenantId,
                AccountId = accountId, Role = TenantRole.PlatformAdmin, Status = MembershipStatus.Active,
                CreatedAtUtc = DateTime.UtcNow });
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
            return accountId;
        }

        /// <summary>Creates an organization whose listed administrators are all active.</summary>
        public async Task<Guid> CreateOrganizationWithAdminsAsync(string ownerEmail, params string[] otherAdmins)
        {
            var created = await Onboarding.CreateTenantAsync(new CreateTenantRequest("Acme", "acme", ownerEmail), default);
            foreach (var email in otherAdmins)
            {
                var accountId = Guid.NewGuid();
                Db.Accounts.Add(new Account { Id = accountId, Email = email, CreatedAtUtc = DateTime.UtcNow });
                Db.Memberships.Add(new Membership { Id = Guid.NewGuid(), TenantId = created.TenantId,
                    AccountId = accountId, Role = TenantRole.TenantAdmin, Status = MembershipStatus.Active,
                    CreatedAtUtc = DateTime.UtcNow });
            }
            await Db.SaveChangesAsync();
            await Db.Memberships.IgnoreQueryFilters().Where(x => x.TenantId == created.TenantId)
                .ExecuteUpdateAsync(x => x.SetProperty(m => m.Status, MembershipStatus.Active));
            Db.ChangeTracker.Clear();
            return created.TenantId;
        }

        public async Task AddRefreshSessionAsync(Guid accountId, Guid tenantId)
        {
            Db.RefreshSessions.Add(new RefreshSession { Id = Guid.NewGuid(), AccountId = accountId,
                TenantId = tenantId, TokenHash = Guid.NewGuid().ToString("N"), SessionVersion = 0,
                CreatedAtUtc = DateTime.UtcNow, ExpiresAtUtc = DateTime.UtcNow.AddDays(1) });
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
        }

        public async ValueTask DisposeAsync()
        {
            if (usesPostgres) await Db.Database.EnsureDeletedAsync();
            await Db.DisposeAsync();
            if (connection is not null) await connection.DisposeAsync();
        }
    }
}
