using System.Security.Cryptography;
using Aiyara.Identities.Databases;
using Aiyara.Identities.Services.Authentication;
using Aiyara.Identities.Services.Onboarding;
using Aiyara.Timesheet.Component.Abstractions.Securities;
using Aiyara.Timesheet.Component.Abstractions.Securities.Options;
using Aiyara.Timesheet.Contracts.Onboarding.V1;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aiyara.Phase2.Tests;

public sealed class AuthenticationFlowTests
{
    [Fact]
    public async Task Activation_login_refresh_replay_and_logout_enforce_session_state()
    {
        await using var fixture = await Fixture.CreateAsync();
        var tenant = await fixture.Onboarding.CreateTenantAsync(
            new CreateTenantRequest("Acme", "acme", "admin@acme.test"), default);
        await Assert.ThrowsAsync<AuthenticationException>(() => fixture.Auth.LoginAsync(
            new LoginRequest(tenant.TenantId, "admin@acme.test", "Correct-Password-123!"), default));

        await fixture.Auth.RequestActivationAsync(
            new RequestCredentialEmail(tenant.TenantId, "admin@acme.test"), default);
        var code = Assert.Single(fixture.Notifications.Messages).Code;
        await fixture.Auth.CompleteActivationAsync(
            new CompleteCredentialChallenge(code, "Correct-Password-123!"), default);
        await Assert.ThrowsAsync<OnboardingException>(() =>
            fixture.Onboarding.IssueInvitationWithKeyAsync(tenant.TenantId,
                tenant.OnboardingKey, new IssueInvitationRequest("new@acme.test", "Employee"),
                default));
        var activeAccount = await fixture.Db.Accounts.SingleAsync();
        Assert.NotNull(await fixture.Onboarding.IssueInvitationAuthorizedAsync(tenant.TenantId,
            activeAccount.Id, activeAccount.SessionVersion,
            new IssueInvitationRequest("new@acme.test", "Employee"), default));
        await Assert.ThrowsAsync<AuthenticationException>(() => fixture.Auth.CompleteActivationAsync(
            new CompleteCredentialChallenge(code, "Correct-Password-123!"), default));

        var login = await fixture.Auth.LoginAsync(new LoginRequest(tenant.TenantId,
            "admin@acme.test", "Correct-Password-123!"), default);
        Assert.StartsWith("v4.public.", login.AccessToken);
        Assert.False(login.MustChangePassword);
        var refreshed = await fixture.Auth.RefreshAsync(new RefreshRequest(login.RefreshToken), default);
        Assert.NotEqual(login.RefreshToken, refreshed.RefreshToken);
        await Assert.ThrowsAsync<AuthenticationException>(() => fixture.Auth.RefreshAsync(
            new RefreshRequest(login.RefreshToken), default));
        await Assert.ThrowsAsync<AuthenticationException>(() => fixture.Auth.RefreshAsync(
            new RefreshRequest(refreshed.RefreshToken), default));

        var second = await fixture.Auth.LoginAsync(new LoginRequest(tenant.TenantId,
            "admin@acme.test", "Correct-Password-123!"), default);
        await fixture.Auth.LogoutAsync(new LogoutRequest(second.RefreshToken), default);
        await Assert.ThrowsAsync<AuthenticationException>(() => fixture.Auth.RefreshAsync(
            new RefreshRequest(second.RefreshToken), default));
    }

    [Fact]
    public async Task Password_reset_revokes_prior_sessions_and_rejects_weak_password()
    {
        await using var fixture = await Fixture.CreateAsync();
        var tenant = await fixture.Onboarding.CreateTenantAsync(
            new CreateTenantRequest("Beta", "beta", "admin@beta.test"), default);
        await fixture.Auth.RequestActivationAsync(
            new RequestCredentialEmail(tenant.TenantId, "admin@beta.test"), default);
        var activation = Assert.Single(fixture.Notifications.Messages).Code;
        await Assert.ThrowsAsync<AuthenticationException>(() => fixture.Auth.CompleteActivationAsync(
            new CompleteCredentialChallenge(activation, "weak"), default));
        await fixture.Auth.CompleteActivationAsync(
            new CompleteCredentialChallenge(activation, "Correct-Password-123!"), default);
        var login = await fixture.Auth.LoginAsync(new LoginRequest(tenant.TenantId,
            "admin@beta.test", "Correct-Password-123!"), default);
        await fixture.Auth.RequestPasswordResetAsync(
            new RequestCredentialEmail(tenant.TenantId, "admin@beta.test"), default);
        var reset = fixture.Notifications.Messages.Last().Code;
        await fixture.Auth.CompletePasswordResetAsync(
            new CompleteCredentialChallenge(reset, "New-Correct-Password-456!"), default);
        await Assert.ThrowsAsync<AuthenticationException>(() => fixture.Auth.RefreshAsync(
            new RefreshRequest(login.RefreshToken), default));
        await Assert.ThrowsAsync<AuthenticationException>(() => fixture.Auth.LoginAsync(
            new LoginRequest(tenant.TenantId, "admin@beta.test", "Correct-Password-123!"), default));
        Assert.NotNull(await fixture.Auth.LoginAsync(new LoginRequest(tenant.TenantId,
            "admin@beta.test", "New-Correct-Password-456!"), default));
    }

    [Fact]
    public async Task Stricter_tenant_policy_forces_password_change_on_next_login()
    {
        await using var fixture = await Fixture.CreateAsync();
        var tenant = await fixture.Onboarding.CreateTenantAsync(
            new CreateTenantRequest("Gamma", "gamma", "admin@gamma.test"), default);
        await fixture.Auth.RequestActivationAsync(
            new RequestCredentialEmail(tenant.TenantId, "admin@gamma.test"), default);
        await fixture.Auth.CompleteActivationAsync(new CompleteCredentialChallenge(
            fixture.Notifications.Messages.Single().Code, "Correct-Password-123!"), default);
        var account = await fixture.Db.Accounts.SingleAsync();
        await fixture.Auth.UpdateTenantPolicyAsync(tenant.TenantId, account.Id,
            account.SessionVersion, new TenantPasswordPolicyRequest(24, 180,
                true, true, true, true), default);
        var login = await fixture.Auth.LoginAsync(new LoginRequest(tenant.TenantId,
            account.Email, "Correct-Password-123!"), default);
        Assert.True(login.MustChangePassword);
        await fixture.Auth.RequestPasswordResetAsync(
            new RequestCredentialEmail(tenant.TenantId, account.Email), default);
        var configuredTenant = await fixture.Db.Tenants.SingleAsync();
        Assert.True(PasswordPolicy.IsSatisfied(configuredTenant,
            fixture.Notifications.Messages.Last().Code));
        await fixture.Auth.CompletePasswordResetAsync(new CompleteCredentialChallenge(
            fixture.Notifications.Messages.Last().Code, "New-Correct-Password-456789!"), default);
        var changed = await fixture.Auth.LoginAsync(new LoginRequest(tenant.TenantId,
            account.Email, "New-Correct-Password-456789!"), default);
        Assert.False(changed.MustChangePassword);
    }

    [Fact]
    public async Task Platform_admin_bootstrap_requires_email_activation()
    {
        await using var fixture = await Fixture.CreateAsync();
        await new PlatformAdminBootstrap(fixture.Db, fixture.Scope)
            .EnsureAsync("operator@platform.test", default);
        var bootstrap = await fixture.Db.Accounts.SingleAsync();
        Assert.True(bootstrap.IsPlatformAdmin);
        await Assert.ThrowsAsync<AuthenticationException>(() => fixture.Auth.LoginAsync(
            new LoginRequest(PlatformAdminBootstrap.PlatformTenantId,
                bootstrap.Email, "Correct-Password-123!"), default));
        await fixture.Auth.RequestActivationAsync(new RequestCredentialEmail(
            PlatformAdminBootstrap.PlatformTenantId, bootstrap.Email), default);
        await fixture.Auth.CompleteActivationAsync(new CompleteCredentialChallenge(
            fixture.Notifications.Messages.Single().Code, "Correct-Password-123!"), default);
        var login = await fixture.Auth.LoginAsync(new LoginRequest(
            PlatformAdminBootstrap.PlatformTenantId, bootstrap.Email,
            "Correct-Password-123!"), default);
        Assert.StartsWith("v4.public.", login.AccessToken);
    }

    [Fact]
    public async Task Password_reset_in_one_tenant_revokes_other_tenant_refresh_sessions()
    {
        await using var fixture = await Fixture.CreateAsync();
        const string email = "shared@example.test";
        var first = await fixture.Onboarding.CreateTenantAsync(
            new CreateTenantRequest("First", "first", email), default);
        var second = await fixture.Onboarding.CreateTenantAsync(
            new CreateTenantRequest("Second", "second", email), default);
        foreach (var tenantId in new[] { first.TenantId, second.TenantId })
        {
            await fixture.Auth.RequestActivationAsync(
                new RequestCredentialEmail(tenantId, email), default);
            await fixture.Auth.CompleteActivationAsync(new CompleteCredentialChallenge(
                fixture.Notifications.Messages.Last().Code, "Correct-Password-123!"), default);
        }
        var firstLogin = await fixture.Auth.LoginAsync(new LoginRequest(first.TenantId,
            email, "Correct-Password-123!"), default);
        await fixture.Auth.RequestPasswordResetAsync(
            new RequestCredentialEmail(second.TenantId, email), default);
        await fixture.Auth.CompletePasswordResetAsync(new CompleteCredentialChallenge(
            fixture.Notifications.Messages.Last().Code, "New-Correct-Password-456!"), default);
        await Assert.ThrowsAsync<AuthenticationException>(() => fixture.Auth.RefreshAsync(
            new RefreshRequest(firstLogin.RefreshToken), default));
        Assert.Contains(fixture.Revocations.AccountEvents,
            x => x.TenantId == first.TenantId);
        Assert.Contains(fixture.Revocations.AccountEvents,
            x => x.TenantId == second.TenantId);
    }

    private sealed class FakeNotifications : ICredentialNotificationSender
    {
        public List<(string Email, string Template, string Code)> Messages { get; } = [];
        public Task SendAsync(string email, string template, string code,
            CancellationToken cancellationToken)
        {
            Messages.Add((email, template, code));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeRevocations : ISessionRevocationPublisher
    {
        public List<(Guid TenantId, Guid AccountId, long Version)> AccountEvents { get; } = [];
        public Task PublishAccountAsync(Guid tenantId, Guid accountId, long sessionVersion,
            CancellationToken cancellationToken)
        {
            AccountEvents.Add((tenantId, accountId, sessionVersion));
            return Task.CompletedTask;
        }
        public Task PublishTenantPolicyAsync(Guid tenantId, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        public IdentityDbContext Db { get; }
        public TenantScope Scope { get; }
        public OnboardingService Onboarding { get; }
        public AuthenticationService Auth { get; }
        public FakeNotifications Notifications { get; } = new();
        public FakeRevocations Revocations { get; } = new();

        private Fixture(SqliteConnection connection, IdentityDbContext db, TenantScope scope)
        {
            this.connection = connection;
            Db = db;
            Scope = scope;
            Onboarding = new OnboardingService(db, scope);
            var passwords = new EncryptionService(Options.Create(new PasswordGenerateSetting()));
            var tokens = new PasetoTokenService(Options.Create(new PasetoSetting
            {
                Key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            }));
            Auth = new AuthenticationService(db, scope, passwords, tokens, Notifications,
                Revocations);
        }

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var scope = new TenantScope();
            var db = new IdentityDbContext(new DbContextOptionsBuilder<IdentityDbContext>()
                .UseSqlite(connection).Options, scope);
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
