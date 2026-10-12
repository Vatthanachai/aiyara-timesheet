using System.Security.Cryptography;
using Aiyara.Identities.Databases;
using Aiyara.Identities.Models.Tenancy;
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
    public async Task Organization_code_login_resolves_tenant_and_returns_internal_tenant_id()
    {
        await using var fixture = await Fixture.CreateAsync();
        var tenant = await fixture.Onboarding.CreateTenantAsync(
            new CreateTenantRequest("Acme", "acme", "admin@acme.test"), default);
        await fixture.Auth.RequestActivationAsync(new RequestCredentialEmail(
            tenant.TenantId, "admin@acme.test"), default);
        await fixture.Auth.CompleteActivationAsync(new CompleteCredentialChallenge(
            fixture.Notifications.Messages.Single().Code, "Correct-Password-123!"), default);

        var login = await fixture.Auth.LoginAsync(new LoginRequest
        {
            OrganizationCode = tenant.OrganizationCode.ToLowerInvariant(),
            Email = "admin@acme.test", Password = "Correct-Password-123!"
        }, default);

        Assert.Equal(tenant.TenantId, login.TenantId);
        Assert.StartsWith("v4.public.", login.AccessToken);
        await Assert.ThrowsAsync<AuthenticationException>(() => fixture.Auth.LoginAsync(
            new LoginRequest { OrganizationCode = "WRONG99", Email = "admin@acme.test",
                Password = "Correct-Password-123!" }, default));
    }

    [Fact]
    public async Task Suspended_organization_rejects_new_login_and_refresh_without_deleting_sessions()
    {
        await using var fixture = await Fixture.CreateAsync();
        var tenant = await fixture.Onboarding.CreateTenantAsync(
            new CreateTenantRequest("Acme", "acme", "admin@acme.test"), default);
        await fixture.Auth.RequestActivationAsync(new RequestCredentialEmail(
            tenant.TenantId, "admin@acme.test"), default);
        await fixture.Auth.CompleteActivationAsync(new CompleteCredentialChallenge(
            fixture.Notifications.Messages.Single().Code, "Correct-Password-123!"), default);
        var login = await fixture.Auth.LoginAsync(new LoginRequest(tenant.TenantId,
            "admin@acme.test", "Correct-Password-123!"), default);

        fixture.Scope.TenantId = tenant.TenantId;
        var organization = await fixture.Db.Tenants.SingleAsync();
        organization.Status = Aiyara.Identities.Models.Tenancy.TenantStatus.Suspended;
        await fixture.Db.SaveChangesAsync();

        await Assert.ThrowsAsync<AuthenticationException>(() => fixture.Auth.LoginAsync(
            new LoginRequest(tenant.TenantId, "admin@acme.test", "Correct-Password-123!"), default));
        await Assert.ThrowsAsync<AuthenticationException>(() => fixture.Auth.RefreshAsync(
            new RefreshRequest(login.RefreshToken), default));
        Assert.Equal(1, await fixture.Db.RefreshSessions.IgnoreQueryFilters()
            .CountAsync(session => session.TenantId == tenant.TenantId && session.RevokedAtUtc == null));
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
        await Assert.ThrowsAsync<OnboardingException>(() =>
            fixture.Onboarding.IssueInvitationAuthorizedAsync(tenant.TenantId,
                account.Id, account.SessionVersion,
                new IssueInvitationRequest("new@gamma.test", "Employee"), default));
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
        var bootstrapEmail = await new PlatformAdminBootstrap(fixture.Db, fixture.Scope)
            .EnsureAsync("operator@platform.test", default);
        Assert.Equal("operator@platform.test", bootstrapEmail);
        var bootstrap = await fixture.Db.Accounts.SingleAsync();
        Assert.True(bootstrap.IsPlatformAdmin);
        await Assert.ThrowsAsync<AuthenticationException>(() => fixture.Auth.LoginAsync(
            new LoginRequest(PlatformAdminBootstrap.PlatformTenantId,
                bootstrap.Email, "Correct-Password-123!"), default));
        await fixture.Auth.RequestActivationAsync(new RequestCredentialEmail(
            PlatformAdminBootstrap.PlatformTenantId, bootstrap.Email), default);
        Assert.Equal("operator@platform.test", await new PlatformAdminBootstrap(
            fixture.Db, fixture.Scope).EnsureAsync("operator@platform.test", default));
        await fixture.Auth.CompleteActivationAsync(new CompleteCredentialChallenge(
            fixture.Notifications.Messages.Single().Code, "Correct-Password-123!"), default);
        Assert.Null(await new PlatformAdminBootstrap(fixture.Db, fixture.Scope)
            .EnsureAsync("operator@platform.test", default));
        var withoutSecondFactor = await Assert.ThrowsAsync<AuthenticationException>(() =>
            fixture.Auth.LoginAsync(new LoginRequest(PlatformAdminBootstrap.PlatformTenantId,
                bootstrap.Email, "Correct-Password-123!"), default));
        Assert.Equal(AuthenticationFailure.MfaRequired, withoutSecondFactor.Failure);
        var login = await EnrollPlatformAdminAsync(fixture, bootstrap.Email, "Correct-Password-123!");
        Assert.StartsWith("v4.public.", login.AccessToken);
    }

    [Fact]
    public async Task Sign_in_without_organization_code_issues_a_session_for_a_single_organization()
    {
        await using var fixture = await Fixture.CreateAsync();
        var tenant = await ActivateTenantAdminAsync(fixture, "Acme", "acme", "admin@acme.test");

        var signIn = await fixture.Auth.SignInAsync(new SignInRequest(
            "Admin@Acme.test", "Correct-Password-123!"), default);

        Assert.NotNull(signIn.Session);
        Assert.Equal(tenant.TenantId, signIn.Session.TenantId);
        Assert.Empty(signIn.Organizations);
        await Assert.ThrowsAsync<AuthenticationException>(() => fixture.Auth.SignInAsync(
            new SignInRequest("admin@acme.test", "Wrong-Password-123!"), default));
        await Assert.ThrowsAsync<AuthenticationException>(() => fixture.Auth.SignInAsync(
            new SignInRequest("nobody@acme.test", "Correct-Password-123!"), default));
    }

    [Fact]
    public async Task Sign_in_lists_active_organizations_when_the_account_belongs_to_several()
    {
        await using var fixture = await Fixture.CreateAsync();
        var acme = await ActivateTenantAdminAsync(fixture, "Acme", "acme", "admin@acme.test");
        var beta = await fixture.Onboarding.CreateTenantAsync(
            new CreateTenantRequest("Beta", "beta", "owner@beta.test"), default);
        var suspended = await fixture.Onboarding.CreateTenantAsync(
            new CreateTenantRequest("Zulu", "zulu", "owner@zulu.test"), default);
        var account = await fixture.Db.Accounts.SingleAsync(x => x.Email == "admin@acme.test");
        foreach (var tenantId in new[] { beta.TenantId, suspended.TenantId })
            fixture.Db.Memberships.Add(new Membership
            {
                Id = Guid.NewGuid(), TenantId = tenantId, AccountId = account.Id,
                Role = TenantRole.Employee, Status = MembershipStatus.Active,
                CreatedAtUtc = DateTime.UtcNow
            });
        (await fixture.Db.Tenants.IgnoreQueryFilters().SingleAsync(x => x.Id == suspended.TenantId))
            .Status = TenantStatus.Suspended;
        await fixture.Db.SaveChangesAsync();

        var signIn = await fixture.Auth.SignInAsync(new SignInRequest(
            "admin@acme.test", "Correct-Password-123!"), default);

        Assert.Null(signIn.Session);
        Assert.Equal([new SignInOrganization(acme.OrganizationCode, "Acme"),
            new SignInOrganization(beta.OrganizationCode, "Beta")], signIn.Organizations);
        var login = await fixture.Auth.LoginAsync(new LoginRequest
        {
            OrganizationCode = beta.OrganizationCode, Email = "admin@acme.test",
            Password = "Correct-Password-123!"
        }, default);
        Assert.Equal(beta.TenantId, login.TenantId);
        Assert.Equal(nameof(TenantRole.Employee), login.Role);
    }

    [Fact]
    public async Task Platform_admin_login_needs_no_organization_code_and_rejects_other_tenants()
    {
        await using var fixture = await Fixture.CreateAsync();
        await new PlatformAdminBootstrap(fixture.Db, fixture.Scope)
            .EnsureAsync("operator@platform.test", default);
        await fixture.Auth.RequestActivationAsync(new RequestCredentialEmail(
            PlatformAdminBootstrap.PlatformTenantId, "operator@platform.test"), default);
        await fixture.Auth.CompleteActivationAsync(new CompleteCredentialChallenge(
            fixture.Notifications.Messages.Last().Code, "Correct-Password-123!"), default);
        var tenant = await fixture.Onboarding.CreateTenantAsync(
            new CreateTenantRequest("Acme", "acme", "admin@acme.test"), default);
        await fixture.Auth.RequestActivationAsync(new RequestCredentialEmail(
            tenant.TenantId, "admin@acme.test"), default);
        await fixture.Auth.CompleteActivationAsync(new CompleteCredentialChallenge(
            fixture.Notifications.Messages.Last().Code, "Correct-Password-123!"), default);

        var login = await EnrollPlatformAdminAsync(fixture, "Operator@Platform.test", "Correct-Password-123!");

        Assert.Equal(PlatformAdminBootstrap.PlatformTenantId, login.TenantId);
        Assert.Equal(nameof(TenantRole.PlatformAdmin), login.Role);
        await Assert.ThrowsAsync<AuthenticationException>(() =>
            fixture.Auth.LoginPlatformAdminAsync(new PlatformAdminLoginRequest(
                "admin@acme.test", "Correct-Password-123!"), default));
        await Assert.ThrowsAsync<AuthenticationException>(() =>
            fixture.Auth.LoginPlatformAdminAsync(new PlatformAdminLoginRequest(
                "operator@platform.test", "Wrong-Password-123!"), default));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Platform_admin_bootstrap_is_a_no_op_when_email_is_not_configured(string? configuredEmail)
    {
        await using var fixture = await Fixture.CreateAsync();
        Assert.Null(await new PlatformAdminBootstrap(fixture.Db, fixture.Scope)
            .EnsureAsync(configuredEmail, default));
        Assert.Empty(await fixture.Db.Accounts.ToListAsync());
        Assert.Empty(await fixture.Db.Tenants.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task Platform_admin_bootstrap_rejects_an_invalid_email()
    {
        await using var fixture = await Fixture.CreateAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new PlatformAdminBootstrap(fixture.Db, fixture.Scope)
                .EnsureAsync("not-an-email", default));
        Assert.Empty(await fixture.Db.Accounts.ToListAsync());
    }

    [Fact]
    public async Task Platform_admin_sign_ins_are_audited_on_success_and_failure_but_tenant_logins_are_not()
    {
        await using var fixture = await Fixture.CreateAsync();
        await new PlatformAdminBootstrap(fixture.Db, fixture.Scope)
            .EnsureAsync("operator@platform.test", default);
        await fixture.Auth.RequestActivationAsync(new RequestCredentialEmail(
            PlatformAdminBootstrap.PlatformTenantId, "operator@platform.test"), default);
        await fixture.Auth.CompleteActivationAsync(new CompleteCredentialChallenge(
            fixture.Notifications.Messages.Last().Code, "Correct-Password-123!"), default);
        await ActivateTenantAdminAsync(fixture, "Acme", "acme", "admin@acme.test");
        var operatorId = (await fixture.Db.Accounts.SingleAsync(x => x.Email == "operator@platform.test")).Id;

        await EnrollPlatformAdminAsync(fixture, "operator@platform.test", "Correct-Password-123!");
        // Email sign-in cannot issue a Platform Admin session: it has no second factor.
        var withoutSecondFactor = await Assert.ThrowsAsync<AuthenticationException>(() =>
            fixture.Auth.SignInAsync(new SignInRequest("operator@platform.test", "Correct-Password-123!"), default));
        Assert.Equal(AuthenticationFailure.MfaRequired, withoutSecondFactor.Failure);
        await Assert.ThrowsAsync<AuthenticationException>(() => fixture.Auth.LoginPlatformAdminAsync(
            new PlatformAdminLoginRequest("operator@platform.test", "Wrong-Password-123!"), default));
        await Assert.ThrowsAsync<AuthenticationException>(() => fixture.Auth.LoginPlatformAdminAsync(
            new PlatformAdminLoginRequest("not an email <script>", "x"), default));
        await Assert.ThrowsAsync<AuthenticationException>(() => fixture.Auth.LoginPlatformAdminAsync(
            new PlatformAdminLoginRequest("admin@acme.test", "Correct-Password-123!"), default));
        await fixture.Auth.SignInAsync(new SignInRequest("admin@acme.test", "Correct-Password-123!"), default);

        var audits = await fixture.Db.PlatformOrganizationAudits.IgnoreQueryFilters()
            .OrderBy(x => x.CreatedAtUtc).ToListAsync();
        Assert.All(audits, x => Assert.Equal(PlatformAdminBootstrap.PlatformTenantId, x.TenantId));
        // Enrollment and the session it opens are saved together, so their order is not asserted.
        Assert.Equal(new[] { AuthenticationService.MfaEnrolledAction, AuthenticationService.PlatformSignInAction }
            .Order(), audits.Take(2).Select(x => x.Action).Order());
        Assert.Equal([AuthenticationService.PlatformSignInFailedAction, AuthenticationService.PlatformSignInFailedAction,
            AuthenticationService.PlatformSignInFailedAction], audits.Skip(2).Select(x => x.Action));
        Assert.Equal(operatorId, audits[0].ActorAccountId);
        Assert.Equal(operatorId, audits[2].ActorAccountId);
        Assert.Equal(Guid.Empty, audits[3].ActorAccountId);
        Assert.Contains("\"Email\":null", audits[3].AfterJson);
        Assert.Contains("admin@acme.test", audits[4].AfterJson);
        Assert.DoesNotContain(audits, x => x.AfterJson!.Contains("Password"));
    }

    [Fact]
    public async Task Platform_admin_bootstrap_rejects_an_email_that_belongs_to_a_customer_organization()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Onboarding.CreateTenantAsync(
            new CreateTenantRequest("Acme", "acme", "owner@acme.test"), default);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new PlatformAdminBootstrap(fixture.Db, fixture.Scope).EnsureAsync("owner@acme.test", default));

        Assert.Contains("customer organization", exception.Message);
        Assert.False((await fixture.Db.Accounts.SingleAsync()).IsPlatformAdmin);
    }

    [Fact]
    public async Task Platform_admin_bootstrap_backfills_organization_code_for_an_existing_platform_tenant()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Db.Tenants.Add(new Tenant
        {
            Id = PlatformAdminBootstrap.PlatformTenantId, Slug = "platform-system",
            Name = "Aiyara Platform", OrganizationCode = string.Empty,
            CreatedAtUtc = DateTime.UtcNow
        });
        await fixture.Db.SaveChangesAsync();

        await new PlatformAdminBootstrap(fixture.Db, fixture.Scope)
            .EnsureAsync("operator@platform.test", default);

        var tenant = await fixture.Db.Tenants.IgnoreQueryFilters().SingleAsync(
            x => x.Id == PlatformAdminBootstrap.PlatformTenantId);
        Assert.Equal(OrganizationCodeGenerator.PlatformCode, tenant.OrganizationCode);
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

    [Fact]
    public async Task Relaxing_policy_does_not_force_password_change()
    {
        await using var fixture = await Fixture.CreateAsync();
        var tenant = await fixture.Onboarding.CreateTenantAsync(
            new CreateTenantRequest("Delta", "delta", "admin@delta.test"), default);
        await fixture.Auth.RequestActivationAsync(
            new RequestCredentialEmail(tenant.TenantId, "admin@delta.test"), default);
        await fixture.Auth.CompleteActivationAsync(new CompleteCredentialChallenge(
            fixture.Notifications.Messages.Single().Code, "Correct-Password-123!"), default);
        var account = await fixture.Db.Accounts.SingleAsync();
        await fixture.Auth.UpdateTenantPolicyAsync(tenant.TenantId, account.Id,
            account.SessionVersion, new TenantPasswordPolicyRequest(12, 365,
                false, false, false, false), default);
        var login = await fixture.Auth.LoginAsync(new LoginRequest(tenant.TenantId,
            account.Email, "Correct-Password-123!"), default);
        Assert.False(login.MustChangePassword);
    }

    [Fact]
    public async Task Forced_password_change_revokes_existing_sessions_and_clears_policy_flag()
    {
        await using var fixture = await Fixture.CreateAsync();
        var tenant = await fixture.Onboarding.CreateTenantAsync(
            new CreateTenantRequest("Epsilon", "epsilon", "admin@epsilon.test"), default);
        await fixture.Auth.RequestActivationAsync(
            new RequestCredentialEmail(tenant.TenantId, "admin@epsilon.test"), default);
        await fixture.Auth.CompleteActivationAsync(new CompleteCredentialChallenge(
            fixture.Notifications.Messages.Single().Code, "Correct-Password-123!"), default);
        var before = await fixture.Auth.LoginAsync(new LoginRequest(tenant.TenantId,
            "admin@epsilon.test", "Correct-Password-123!"), default);
        var account = await fixture.Db.Accounts.SingleAsync();
        await fixture.Auth.UpdateTenantPolicyAsync(tenant.TenantId, account.Id,
            account.SessionVersion, new TenantPasswordPolicyRequest(24, 180,
                true, true, true, true), default);
        Assert.All(await fixture.Db.RefreshSessions.AsNoTracking().ToListAsync(),
            session => Assert.NotNull(session.RevokedAtUtc));
        var forced = await fixture.Auth.LoginAsync(new LoginRequest(tenant.TenantId,
            account.Email, "Correct-Password-123!"), default);
        Assert.True(forced.MustChangePassword);
        await Assert.ThrowsAsync<AuthenticationException>(() => fixture.Auth.ChangePasswordAsync(
            tenant.TenantId, account.Id, account.SessionVersion,
            new ChangePasswordRequest("wrong", "Very-Strong-New-Password-789!"), default));
        await fixture.Auth.ChangePasswordAsync(tenant.TenantId, account.Id,
            account.SessionVersion, new ChangePasswordRequest("Correct-Password-123!",
                "Very-Strong-New-Password-789!"), default);
        await Assert.ThrowsAsync<AuthenticationException>(() => fixture.Auth.RefreshAsync(
            new RefreshRequest(before.RefreshToken), default));
        await Assert.ThrowsAsync<AuthenticationException>(() => fixture.Auth.RefreshAsync(
            new RefreshRequest(forced.RefreshToken), default));
        var changed = await fixture.Auth.LoginAsync(new LoginRequest(tenant.TenantId,
            account.Email, "Very-Strong-New-Password-789!"), default);
        Assert.False(changed.MustChangePassword);
        Assert.False((await fixture.Db.Memberships.SingleAsync()).MustChangePassword);
    }

    internal static async Task<CreateTenantResponse> ActivateTenantAdminAsync(Fixture fixture,
        string name, string slug, string email)
    {
        var tenant = await fixture.Onboarding.CreateTenantAsync(
            new CreateTenantRequest(name, slug, email), default);
        await fixture.Auth.RequestActivationAsync(new RequestCredentialEmail(
            tenant.TenantId, email), default);
        await fixture.Auth.CompleteActivationAsync(new CompleteCredentialChallenge(
            fixture.Notifications.Messages.Last().Code, "Correct-Password-123!"), default);
        return tenant;
    }

    internal sealed class FakeNotifications : ICredentialNotificationSender
    {
        public List<(string Email, string Template, string Code)> Messages { get; } = [];
        public Task SendAsync(string email, string template, string code,
            CancellationToken cancellationToken)
        {
            Messages.Add((email, template, code));
            return Task.CompletedTask;
        }
    }

    internal sealed class FakeRevocations : ISessionRevocationPublisher
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
        public Task CompleteTenantPolicyAsync(Guid tenantId, long policyVersion,
            CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    /// <summary>Signs a Platform Admin in for the first time: takes the offered enrollment secret
    /// and confirms it with the current code, as an authenticator app would.</summary>
    internal static async Task<AuthenticationResponse> EnrollPlatformAdminAsync(Fixture fixture,
        string email, string password)
    {
        var offer = await fixture.Auth.LoginPlatformAdminAsync(new PlatformAdminLoginRequest(email, password), default);
        Assert.Null(offer.Session);
        var secret = Assert.IsType<MfaEnrollment>(offer.Enrollment).Secret;
        var enrolled = await fixture.Auth.LoginPlatformAdminAsync(new PlatformAdminLoginRequest(email, password,
            Totp.Compute(secret, Totp.StepAt(DateTimeOffset.UtcNow))), default);
        Assert.Equal(10, enrolled.RecoveryCodes.Count);
        return Assert.IsType<AuthenticationResponse>(enrolled.Session);
    }

    internal sealed class FakeAttemptLimiter : ICredentialAttemptLimiter
    {
        public Task CheckAsync(Guid tenantId, string normalizedEmail, string purpose,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    internal sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection? connection;
        public IdentityDbContext Db { get; }
        public TenantScope Scope { get; }
        public OnboardingService Onboarding { get; }
        public AuthenticationService Auth { get; }
        public FakeNotifications Notifications { get; } = new();
        public FakeRevocations Revocations { get; } = new();

        private Fixture(SqliteConnection? connection, IdentityDbContext db, TenantScope scope)
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
                Revocations, new FakeAttemptLimiter(),
                new AesGcmMfaSecretProtector(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))));
        }

        /// <summary>Uses in-memory SQLite by default. When AIYARA_TEST_POSTGRES holds a server
        /// connection string, each test instead gets a throwaway PostgreSQL database built from the
        /// real migrations, because SQLite translates some EF queries differently.</summary>
        public static async Task<Fixture> CreateAsync()
        {
            var scope = new TenantScope();
            if (Environment.GetEnvironmentVariable("AIYARA_TEST_POSTGRES") is { Length: > 0 } server)
            {
                var database = $"identity_auth_test_{Guid.NewGuid():N}";
                var postgres = new IdentityDbContext(new DbContextOptionsBuilder<IdentityDbContext>()
                    .UseNpgsql(new Npgsql.NpgsqlConnectionStringBuilder(server) { Database = database }.ConnectionString)
                    .Options, scope);
                await postgres.Database.MigrateAsync();
                return new Fixture(null, postgres, scope);
            }
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new IdentityDbContext(new DbContextOptionsBuilder<IdentityDbContext>()
                .UseSqlite(connection).Options, scope);
            await db.Database.EnsureCreatedAsync();
            return new Fixture(connection, db, scope);
        }

        public async ValueTask DisposeAsync()
        {
            if (connection is null) await Db.Database.EnsureDeletedAsync();
            await Db.DisposeAsync();
            if (connection is not null) await connection.DisposeAsync();
        }
    }
}
