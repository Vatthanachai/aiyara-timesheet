using Aiyara.Identities.Databases;
using Aiyara.Identities.Models.Tenancy;
using Aiyara.Identities.Services.Authentication;
using Aiyara.Identities.Services.Onboarding;
using Aiyara.Timesheet.Contracts.Onboarding.V1;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Aiyara.Phase1.Tests;

/// <summary>Phase B of the Tenant Member Management Plan: a Tenant Admin's member directory,
/// pending invitations, role and status changes, and activation re-sends.</summary>
public sealed class TenantMemberManagementTests
{
    [Fact]
    public async Task Directory_marks_the_caller_and_never_shows_another_organizations_members()
    {
        await using var fixture = await Fixture.CreateAsync();
        var employee = await fixture.AddMemberAsync(fixture.TenantId, "zoe@acme.test", TenantRole.Employee);
        var other = await fixture.CreateTenantAsync("Globex", "globex", "owner@globex.test");
        await fixture.AddMemberAsync(other.TenantId, "spy@globex.test", TenantRole.Employee);

        var members = await fixture.Members.ListMembersAsync(fixture.TenantId, fixture.AdminId, 0, default);

        Assert.Equal(["owner@acme.test", "zoe@acme.test"], members.Select(x => x.Email).Order());
        Assert.True(Assert.Single(members, x => x.Id == fixture.AdminId).IsCurrentUser);
        Assert.False(Assert.Single(members, x => x.Id == employee).IsCurrentUser);
        Assert.Equal("Employee", Assert.Single(members, x => x.Id == employee).Role);
    }

    [Fact]
    public async Task Only_an_active_admin_with_a_current_session_and_password_may_manage_members()
    {
        await using var fixture = await Fixture.CreateAsync();
        var employee = await fixture.AddMemberAsync(fixture.TenantId, "zoe@acme.test", TenantRole.Employee);
        var other = await fixture.CreateTenantAsync("Globex", "globex", "owner@globex.test");

        async Task<TenantMemberFailure> Failure(Guid tenantId, Guid actor, long version) =>
            (await Assert.ThrowsAsync<TenantMemberException>(() =>
                fixture.Members.ListMembersAsync(tenantId, actor, version, default))).Failure;

        Assert.Equal(TenantMemberFailure.Unauthorized, await Failure(fixture.TenantId, employee, 0));
        Assert.Equal(TenantMemberFailure.Unauthorized, await Failure(fixture.TenantId, fixture.AdminId, 1));
        Assert.Equal(TenantMemberFailure.Unauthorized, await Failure(other.TenantId, fixture.AdminId, 0));
        Assert.Equal(TenantMemberFailure.Unauthorized,
            await Failure(PlatformAdminBootstrap.PlatformTenantId, fixture.AdminId, 0));

        await fixture.Db.Accounts.Where(x => x.Id == fixture.AdminId).ExecuteUpdateAsync(x =>
            x.SetProperty(a => a.PasswordChangedAtUtc, DateTime.UtcNow.AddDays(-400)));
        Assert.Equal(TenantMemberFailure.Unauthorized, await Failure(fixture.TenantId, fixture.AdminId, 0));
    }

    [Fact]
    public async Task A_new_invitation_replaces_older_codes_and_is_audited()
    {
        await using var fixture = await Fixture.CreateAsync();

        var first = await fixture.Onboarding.IssueInvitationAuthorizedAsync(fixture.TenantId, fixture.AdminId, 0,
            new IssueInvitationRequest("New@Acme.test", "Employee"), default);
        var second = await fixture.Onboarding.IssueInvitationAuthorizedAsync(fixture.TenantId, fixture.AdminId, 0,
            new IssueInvitationRequest("new@acme.test", "TenantAdmin"), default);

        var pending = Assert.Single(await fixture.Members.ListInvitationsAsync(fixture.TenantId, fixture.AdminId, 0, default));
        Assert.Equal(second.InvitationId, pending.Id);
        Assert.Equal(("new@acme.test", "TenantAdmin", "owner@acme.test"), (pending.Email, pending.Role, pending.InvitedByEmail));
        await Assert.ThrowsAsync<OnboardingException>(() =>
            fixture.Onboarding.AcceptInvitationAsync(new AcceptInvitationRequest(first.Code), default));
        var accepted = await fixture.Onboarding.AcceptInvitationAsync(new AcceptInvitationRequest(second.Code), default);
        Assert.Equal("TenantAdmin", accepted.Role);
        Assert.Empty(await fixture.Members.ListInvitationsAsync(fixture.TenantId, fixture.AdminId, 0, default));
        Assert.Equal(2, await fixture.Audits("member.invited").CountAsync());
    }

    [Fact]
    public async Task Resending_an_invitation_issues_a_new_code_and_revoking_one_stops_it()
    {
        await using var fixture = await Fixture.CreateAsync();
        var original = await fixture.Onboarding.IssueInvitationAuthorizedAsync(fixture.TenantId, fixture.AdminId, 0,
            new IssueInvitationRequest("new@acme.test", "Employee"), default);

        var (email, resent) = await fixture.Members.ResendInvitationAsync(fixture.TenantId, fixture.AdminId, 0,
            original.InvitationId, default);

        Assert.Equal("new@acme.test", email);
        Assert.NotEqual(original.Code, resent.Code);
        Assert.Equal(resent.InvitationId,
            Assert.Single(await fixture.Members.ListInvitationsAsync(fixture.TenantId, fixture.AdminId, 0, default)).Id);
        await Assert.ThrowsAsync<OnboardingException>(() =>
            fixture.Onboarding.AcceptInvitationAsync(new AcceptInvitationRequest(original.Code), default));

        await fixture.Members.RevokeInvitationAsync(fixture.TenantId, fixture.AdminId, 0, resent.InvitationId, default);

        Assert.Empty(await fixture.Members.ListInvitationsAsync(fixture.TenantId, fixture.AdminId, 0, default));
        await Assert.ThrowsAsync<OnboardingException>(() =>
            fixture.Onboarding.AcceptInvitationAsync(new AcceptInvitationRequest(resent.Code), default));
        var gone = await Assert.ThrowsAsync<TenantMemberException>(() =>
            fixture.Members.RevokeInvitationAsync(fixture.TenantId, fixture.AdminId, 0, resent.InvitationId, default));
        Assert.Equal(TenantMemberFailure.NotFound, gone.Failure);
        Assert.Equal(1, await fixture.Audits("member.invitation_resent").CountAsync());
        Assert.Equal(1, await fixture.Audits("member.invitation_revoked").CountAsync());
    }

    [Fact]
    public async Task Another_organizations_invitation_or_member_is_not_found()
    {
        await using var fixture = await Fixture.CreateAsync();
        var other = await fixture.CreateTenantAsync("Globex", "globex", "owner@globex.test");
        var otherAdmin = await fixture.ActivateOwnerAsync(other.TenantId);
        var outsider = await fixture.AddMemberAsync(other.TenantId, "spy@globex.test", TenantRole.Employee);
        var invitation = await fixture.Onboarding.IssueInvitationAuthorizedAsync(other.TenantId, otherAdmin, 0,
            new IssueInvitationRequest("new@globex.test", "Employee"), default);

        var failures = new[]
        {
            await Assert.ThrowsAsync<TenantMemberException>(() => fixture.Members.ResendInvitationAsync(
                fixture.TenantId, fixture.AdminId, 0, invitation.InvitationId, default)),
            await Assert.ThrowsAsync<TenantMemberException>(() => fixture.Members.RevokeInvitationAsync(
                fixture.TenantId, fixture.AdminId, 0, invitation.InvitationId, default)),
            await Assert.ThrowsAsync<TenantMemberException>(() => fixture.Members.ChangeRoleAsync(
                fixture.TenantId, fixture.AdminId, 0, outsider, new ChangeTenantMemberRoleRequest("TenantAdmin"), default)),
            await Assert.ThrowsAsync<TenantMemberException>(() => fixture.Members.SetStatusAsync(
                fixture.TenantId, fixture.AdminId, 0, outsider, new SetTenantMemberStatusRequest(false), default))
        };

        Assert.All(failures, x => Assert.Equal(TenantMemberFailure.NotFound, x.Failure));
        Assert.Equal(MembershipStatus.Active, (await fixture.Membership(outsider)).Status);
    }

    [Fact]
    public async Task Changing_a_role_ends_the_members_sessions_and_is_audited()
    {
        await using var fixture = await Fixture.CreateAsync();
        var employee = await fixture.AddMemberAsync(fixture.TenantId, "zoe@acme.test", TenantRole.Employee);
        await fixture.AddRefreshSessionAsync(employee);

        await fixture.Members.ChangeRoleAsync(fixture.TenantId, fixture.AdminId, 0, employee,
            new ChangeTenantMemberRoleRequest("tenantadmin"), default);

        Assert.Equal(TenantRole.TenantAdmin, (await fixture.Membership(employee)).Role);
        Assert.Equal(1, (await fixture.Db.Accounts.SingleAsync(x => x.Id == employee)).SessionVersion);
        Assert.All(await fixture.Db.RefreshSessions.IgnoreQueryFilters().Where(x => x.AccountId == employee).ToListAsync(),
            x => Assert.NotNull(x.RevokedAtUtc));
        Assert.Contains(fixture.Revocations.Accounts, x => x == (fixture.TenantId, employee, 1));
        var audit = await fixture.Audits("member.role_changed").SingleAsync();
        Assert.Equal(fixture.AdminId, audit.ActorAccountId);
        Assert.Contains("\"Employee\"", audit.BeforeJson);
        Assert.Contains("\"TenantAdmin\"", audit.AfterJson);

        await fixture.Members.ChangeRoleAsync(fixture.TenantId, fixture.AdminId, 0, employee,
            new ChangeTenantMemberRoleRequest("Employee"), default);
        Assert.Equal(TenantRole.Employee, (await fixture.Membership(employee)).Role);
    }

    [Fact]
    public async Task Role_changes_refuse_unknown_roles_yourself_and_deactivated_members()
    {
        await using var fixture = await Fixture.CreateAsync();
        var employee = await fixture.AddMemberAsync(fixture.TenantId, "zoe@acme.test", TenantRole.Employee);
        var gone = await fixture.AddMemberAsync(fixture.TenantId, "gone@acme.test", TenantRole.Employee,
            MembershipStatus.Deactivated);

        async Task<TenantMemberFailure> Failure(Guid accountId, string role) =>
            (await Assert.ThrowsAsync<TenantMemberException>(() => fixture.Members.ChangeRoleAsync(
                fixture.TenantId, fixture.AdminId, 0, accountId, new ChangeTenantMemberRoleRequest(role), default))).Failure;

        foreach (var role in new[] { "PlatformAdmin", "1", "", "Owner" })
            Assert.Equal(TenantMemberFailure.InvalidInput, await Failure(employee, role));
        Assert.Equal(TenantMemberFailure.SelfChange, await Failure(fixture.AdminId, "Employee"));
        Assert.Equal(TenantMemberFailure.Conflict, await Failure(gone, "TenantAdmin"));
        Assert.Equal(TenantMemberFailure.NotFound, await Failure(Guid.NewGuid(), "TenantAdmin"));
        Assert.Equal(TenantRole.TenantAdmin, (await fixture.Membership(fixture.AdminId)).Role);
    }

    [Fact]
    public async Task Deactivating_ends_sessions_and_reactivating_restores_access_by_password_state()
    {
        await using var fixture = await Fixture.CreateAsync();
        var activated = await fixture.AddMemberAsync(fixture.TenantId, "zoe@acme.test", TenantRole.Employee,
            passwordHash: "hash");
        var neverActivated = await fixture.AddMemberAsync(fixture.TenantId, "new@acme.test", TenantRole.Employee,
            MembershipStatus.PendingActivation);

        await fixture.Members.SetStatusAsync(fixture.TenantId, fixture.AdminId, 0, activated,
            new SetTenantMemberStatusRequest(false), default);
        await fixture.Members.SetStatusAsync(fixture.TenantId, fixture.AdminId, 0, neverActivated,
            new SetTenantMemberStatusRequest(false), default);

        Assert.Equal(MembershipStatus.Deactivated, (await fixture.Membership(activated)).Status);
        Assert.Equal(1, (await fixture.Db.Accounts.SingleAsync(x => x.Id == activated)).SessionVersion);
        Assert.Equal(2, await fixture.Audits("member.deactivated").CountAsync());

        await fixture.Members.SetStatusAsync(fixture.TenantId, fixture.AdminId, 0, activated,
            new SetTenantMemberStatusRequest(true), default);
        Assert.Equal(MembershipStatus.Active, (await fixture.Membership(activated)).Status);

        // Without a password the member goes back through activation; this fixture has no email
        // delivery, so the status change commits and the send reports Unavailable.
        var unsent = await Assert.ThrowsAsync<TenantMemberException>(() => fixture.Members.SetStatusAsync(
            fixture.TenantId, fixture.AdminId, 0, neverActivated, new SetTenantMemberStatusRequest(true), default));
        Assert.Equal(TenantMemberFailure.Unavailable, unsent.Failure);
        Assert.Equal(MembershipStatus.PendingActivation, (await fixture.Membership(neverActivated)).Status);
        Assert.Equal(2, await fixture.Audits("member.reactivated").CountAsync());

        var self = await Assert.ThrowsAsync<TenantMemberException>(() => fixture.Members.SetStatusAsync(
            fixture.TenantId, fixture.AdminId, 0, fixture.AdminId, new SetTenantMemberStatusRequest(false), default));
        Assert.Equal(TenantMemberFailure.SelfChange, self.Failure);
    }

    [Fact]
    public async Task A_deactivated_member_cannot_be_invited_back_until_reactivated()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddMemberAsync(fixture.TenantId, "gone@acme.test", TenantRole.Employee, MembershipStatus.Deactivated);

        var conflict = await Assert.ThrowsAsync<OnboardingException>(() => fixture.Onboarding.IssueInvitationAuthorizedAsync(
            fixture.TenantId, fixture.AdminId, 0, new IssueInvitationRequest("gone@acme.test", "Employee"), default));

        Assert.Equal(OnboardingFailure.Conflict, conflict.Failure);
    }

    [Fact]
    public async Task Activation_is_resent_only_to_pending_members()
    {
        await using var fixture = await Fixture.CreateAsync();
        var active = await fixture.AddMemberAsync(fixture.TenantId, "zoe@acme.test", TenantRole.Employee);
        var pending = await fixture.AddMemberAsync(fixture.TenantId, "new@acme.test", TenantRole.Employee,
            MembershipStatus.PendingActivation);

        var notPending = await Assert.ThrowsAsync<TenantMemberException>(() => fixture.Members.ResendActivationAsync(
            fixture.TenantId, fixture.AdminId, 0, active, default));
        var noDelivery = await Assert.ThrowsAsync<TenantMemberException>(() => fixture.Members.ResendActivationAsync(
            fixture.TenantId, fixture.AdminId, 0, pending, default));

        Assert.Equal(TenantMemberFailure.Conflict, notPending.Failure);
        Assert.Equal(TenantMemberFailure.Unavailable, noDelivery.Failure);
        Assert.False(await fixture.Audits("member.activation_resent").AnyAsync());
    }

    [Fact]
    public async Task An_account_with_a_password_joins_another_organization_as_active_without_activation()
    {
        await using var fixture = await Fixture.CreateAsync();
        var other = await fixture.CreateTenantAsync("Globex", "globex", "owner@globex.test");
        await fixture.ActivateOwnerAsync(other.TenantId);
        var invitation = await fixture.Onboarding.IssueInvitationAuthorizedAsync(fixture.TenantId, fixture.AdminId, 0,
            new IssueInvitationRequest("owner@globex.test", "Employee"), default);
        var fresh = await fixture.Onboarding.IssueInvitationAuthorizedAsync(fixture.TenantId, fixture.AdminId, 0,
            new IssueInvitationRequest("fresh@acme.test", "Employee"), default);

        var joined = await fixture.Onboarding.AcceptInvitationAsync(new AcceptInvitationRequest(invitation.Code), default);
        var created = await fixture.Onboarding.AcceptInvitationAsync(new AcceptInvitationRequest(fresh.Code), default);

        Assert.Equal("Active", joined.Status);
        Assert.Equal("PendingActivation", created.Status);
        Assert.Equal("hash", (await fixture.Db.Accounts.SingleAsync(x => x.Email == "owner@globex.test")).PasswordHash);
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
        public TenantMemberService Members { get; }
        public RecordingRevocations Revocations { get; } = new();
        public Guid TenantId { get; private set; }
        public Guid AdminId { get; private set; }

        private Fixture(SqliteConnection? connection, bool usesPostgres, IdentityDbContext db, TenantScope scope)
        {
            this.connection = connection;
            this.usesPostgres = usesPostgres;
            Db = db;
            Scope = scope;
            Onboarding = new OnboardingService(db, scope);
            Members = new TenantMemberService(db, scope, Onboarding,
                new PlatformOrganizationService(db, scope, Revocations));
        }

        /// <summary>An organization, Acme, whose owner is an active TenantAdmin with a current
        /// password. Uses in-memory SQLite, or a throwaway PostgreSQL database built from the real
        /// migrations when AIYARA_TEST_POSTGRES holds a server connection string.</summary>
        public static async Task<Fixture> CreateAsync()
        {
            var scope = new TenantScope();
            Fixture fixture;
            if (Environment.GetEnvironmentVariable("AIYARA_TEST_POSTGRES") is { Length: > 0 } server)
            {
                var database = $"identity_members_test_{Guid.NewGuid():N}";
                var postgres = new IdentityDbContext(new DbContextOptionsBuilder<IdentityDbContext>()
                    .UseNpgsql(new Npgsql.NpgsqlConnectionStringBuilder(server) { Database = database }.ConnectionString)
                    .Options, scope);
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
            var created = await fixture.CreateTenantAsync("Acme", "acme", "owner@acme.test");
            fixture.TenantId = created.TenantId;
            fixture.AdminId = await fixture.ActivateOwnerAsync(created.TenantId);
            return fixture;
        }

        public Task<CreateTenantResponse> CreateTenantAsync(string name, string slug, string ownerEmail) =>
            Onboarding.CreateTenantAsync(new CreateTenantRequest(name, slug, ownerEmail), default);

        /// <summary>Activates the organization's pending owner with a password set now.</summary>
        public async Task<Guid> ActivateOwnerAsync(Guid tenantId)
        {
            var owner = await Db.Memberships.IgnoreQueryFilters().SingleAsync(x => x.TenantId == tenantId);
            owner.Status = MembershipStatus.Active;
            var account = await Db.Accounts.SingleAsync(x => x.Id == owner.AccountId);
            account.PasswordHash = "hash";
            account.PasswordChangedAtUtc = DateTime.UtcNow.AddSeconds(1);
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
            return owner.AccountId;
        }

        public async Task<Guid> AddMemberAsync(Guid tenantId, string email, TenantRole role,
            MembershipStatus status = MembershipStatus.Active, string? passwordHash = "hash")
        {
            var accountId = Guid.NewGuid();
            Db.Accounts.Add(new Account { Id = accountId, Email = email, CreatedAtUtc = DateTime.UtcNow,
                PasswordHash = status == MembershipStatus.PendingActivation ? null : passwordHash });
            Db.Memberships.Add(new Membership { Id = Guid.NewGuid(), TenantId = tenantId, AccountId = accountId,
                Role = role, Status = status, CreatedAtUtc = DateTime.UtcNow });
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
            return accountId;
        }

        public async Task AddRefreshSessionAsync(Guid accountId)
        {
            Db.RefreshSessions.Add(new RefreshSession { Id = Guid.NewGuid(), AccountId = accountId,
                TenantId = TenantId, TokenHash = Guid.NewGuid().ToString("N"), SessionVersion = 0,
                CreatedAtUtc = DateTime.UtcNow, ExpiresAtUtc = DateTime.UtcNow.AddDays(1) });
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
        }

        public Task<Membership> Membership(Guid accountId)
        {
            Db.ChangeTracker.Clear();
            return Db.Memberships.IgnoreQueryFilters().SingleAsync(x => x.AccountId == accountId);
        }

        public IQueryable<PlatformOrganizationAudit> Audits(string action) =>
            Db.PlatformOrganizationAudits.IgnoreQueryFilters().Where(x => x.Action == action);

        public async ValueTask DisposeAsync()
        {
            if (usesPostgres) await Db.Database.EnsureDeletedAsync();
            await Db.DisposeAsync();
            if (connection is not null) await connection.DisposeAsync();
        }
    }
}
