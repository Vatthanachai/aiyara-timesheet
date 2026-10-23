using Aiyara.Identities.Models.Tenancy;
using Aiyara.Identities.Services.Authentication;
using Aiyara.Identities.Services.Onboarding;
using Aiyara.Timesheet.Contracts.Onboarding.V1;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static Aiyara.Phase2.Tests.AuthenticationFlowTests;

namespace Aiyara.Phase2.Tests;

/// <summary>Backlog item 7: a Tenant Admin can require MFA for the organization's members, who enroll at
/// their next sign-in, answer a code or a recovery code afterwards, and can be reset by an admin.</summary>
public sealed class MemberMfaTests
{
    private const string Password = "Correct-Password-123!";

    [Fact]
    public async Task Requiring_mfa_needs_an_enrolled_admin_and_ends_only_unenrolled_members_sessions()
    {
        await using var fixture = await Fixture.CreateAsync();
        var (acme, adminId) = await OrganizationAsync(fixture, "Acme", "acme", "admin@acme.test");
        var staffId = await AddEmployeeAsync(fixture, acme, adminId, "staff@acme.test");
        var adminSession = await LoginAsync(fixture, acme, "admin@acme.test");
        var staffSession = await LoginAsync(fixture, acme, "staff@acme.test");
        var members = Members(fixture);
        var request = new SetTenantMfaRequirementRequest(true, "Policy");

        var notEnrolled = await Assert.ThrowsAsync<TenantMemberException>(() => members.SetMfaRequirementAsync(
            acme.TenantId, adminId, Version(fixture, adminId), request, default));
        await OptInAsync(fixture, acme.TenantId, adminId);
        await Assert.ThrowsAsync<TenantMemberException>(() => members.SetMfaRequirementAsync(
            acme.TenantId, adminId, Version(fixture, adminId), request with { Reason = new string('x', 501) }, default));
        await members.SetMfaRequirementAsync(acme.TenantId, adminId, Version(fixture, adminId), request, default);

        Assert.Equal(TenantMemberFailure.MfaNotEnrolled, notEnrolled.Failure);
        fixture.Db.ChangeTracker.Clear();
        Assert.True((await fixture.Db.Tenants.IgnoreQueryFilters().SingleAsync(x => x.Id == acme.TenantId)).RequireMfa);
        await Assert.ThrowsAsync<AuthenticationException>(() => fixture.Auth.RefreshAsync(
            new RefreshRequest(staffSession.RefreshToken), default));
        Assert.NotNull(await fixture.Auth.RefreshAsync(new RefreshRequest(adminSession.RefreshToken), default));
        Assert.Contains(fixture.Revocations.AccountEvents, x => x.AccountId == staffId && x.TenantId == acme.TenantId);
        var audit = Assert.Single(await AuditsAsync(fixture, "organization.mfa_requirement_changed"));
        Assert.Equal((acme.TenantId, adminId, "Policy"), (audit.TenantId, audit.ActorAccountId, audit.Reason));
        Assert.Contains("\"RequireMfa\":true", audit.AfterJson);

        // Switching it off unenrolls nobody and is audited too; repeating it changes nothing.
        await members.SetMfaRequirementAsync(acme.TenantId, adminId, Version(fixture, adminId),
            new SetTenantMfaRequirementRequest(false, null), default);
        await members.SetMfaRequirementAsync(acme.TenantId, adminId, Version(fixture, adminId),
            new SetTenantMfaRequirementRequest(false, null), default);
        Assert.Equal(2, (await AuditsAsync(fixture, "organization.mfa_requirement_changed")).Count);
        Assert.NotNull((await fixture.Db.Accounts.AsNoTracking().SingleAsync(x => x.Id == adminId)).MfaSecretProtected);
    }

    [Fact]
    public async Task A_required_member_gets_no_session_until_the_first_code_confirms_the_authenticator()
    {
        await using var fixture = await Fixture.CreateAsync();
        var (acme, adminId) = await OrganizationAsync(fixture, "Acme", "acme", "admin@acme.test", requireMfa: true);
        var staffId = await AddEmployeeAsync(fixture, acme, adminId, "staff@acme.test");

        var needed = await Assert.ThrowsAsync<AuthenticationException>(() => LoginAsync(fixture, acme, "staff@acme.test"));
        var unstarted = await Assert.ThrowsAsync<AuthenticationException>(() =>
            LoginAsync(fixture, acme, "staff@acme.test", totp: "123456"));
        var wrongPassword = await Assert.ThrowsAsync<AuthenticationException>(() =>
            fixture.Auth.BeginRequiredMfaEnrollmentAsync(new MemberMfaEnrollmentRequest("staff@acme.test", "Wrong-Password-1!"), default));
        var offer = await fixture.Auth.BeginRequiredMfaEnrollmentAsync(
            new MemberMfaEnrollmentRequest("Staff@Acme.test", Password), default);
        var again = await fixture.Auth.BeginRequiredMfaEnrollmentAsync(
            new MemberMfaEnrollmentRequest("staff@acme.test", Password), default);
        var wrong = await Assert.ThrowsAsync<AuthenticationException>(() =>
            LoginAsync(fixture, acme, "staff@acme.test", totp: "000000"));
        var session = await LoginAsync(fixture, acme, "staff@acme.test", totp: Code(offer.Secret));

        Assert.Equal(AuthenticationFailure.MfaEnrollmentRequired, needed.Failure);
        Assert.Equal(AuthenticationFailure.MfaInvalid, unstarted.Failure);
        Assert.Equal(AuthenticationFailure.InvalidCredentials, wrongPassword.Failure);
        Assert.Equal(offer.Secret, again.Secret);
        Assert.StartsWith("otpauth://totp/Aiyara%20Timesheet:", offer.ProvisioningUri);
        Assert.Equal(AuthenticationFailure.MfaInvalid, wrong.Failure);
        Assert.Equal(10, session.RecoveryCodes!.Count);
        Assert.Equal(acme.TenantId, session.TenantId);
        var account = await fixture.Db.Accounts.AsNoTracking().SingleAsync(x => x.Id == staffId);
        Assert.NotNull(account.MfaEnabledAtUtc);
        Assert.DoesNotContain(offer.Secret, account.MfaSecretProtected);
        Assert.Equal(10, await fixture.Db.MfaRecoveryCodes.CountAsync(x => x.AccountId == staffId));
        var audit = Assert.Single(await AuditsAsync(fixture, AuthenticationService.MemberMfaEnrolledAction));
        Assert.Equal((acme.TenantId, staffId), (audit.TenantId, audit.ActorAccountId));
        Assert.DoesNotContain(offer.Secret, audit.AfterJson);
        Assert.All(session.RecoveryCodes, code => Assert.DoesNotContain(code, audit.AfterJson));

        // From now on the member answers a code; a code already used is refused.
        var missing = await Assert.ThrowsAsync<AuthenticationException>(() => LoginAsync(fixture, acme, "staff@acme.test"));
        var replayed = await Assert.ThrowsAsync<AuthenticationException>(() =>
            LoginAsync(fixture, acme, "staff@acme.test", totp: Code(offer.Secret)));
        var next = await LoginAsync(fixture, acme, "staff@acme.test", totp: Code(offer.Secret, 1));
        Assert.Equal(AuthenticationFailure.MfaRequired, missing.Failure);
        Assert.Equal(AuthenticationFailure.MfaInvalid, replayed.Failure);
        Assert.Null(next.RecoveryCodes);
    }

    [Fact]
    public async Task Email_sign_in_enforces_the_same_rules_for_a_single_organization()
    {
        await using var fixture = await Fixture.CreateAsync();
        var (acme, _) = await OrganizationAsync(fixture, "Acme", "acme", "admin@acme.test", requireMfa: true);

        var needed = await Assert.ThrowsAsync<AuthenticationException>(() =>
            fixture.Auth.SignInAsync(new SignInRequest("admin@acme.test", Password), default));
        var offer = await fixture.Auth.BeginRequiredMfaEnrollmentAsync(
            new MemberMfaEnrollmentRequest("admin@acme.test", Password), default);
        var signIn = await fixture.Auth.SignInAsync(new SignInRequest("admin@acme.test", Password, Code(offer.Secret)), default);

        Assert.Equal(AuthenticationFailure.MfaEnrollmentRequired, needed.Failure);
        Assert.Equal(10, signIn.Session!.RecoveryCodes!.Count);
    }

    [Fact]
    public async Task Five_wrong_codes_lock_the_member_and_the_lockout_is_audited_in_their_organization()
    {
        await using var fixture = await Fixture.CreateAsync();
        var (acme, adminId) = await OrganizationAsync(fixture, "Acme", "acme", "admin@acme.test", requireMfa: true);
        var (secret, _) = await EnrollByLoginAsync(fixture, acme, "admin@acme.test");

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var wrong = await Assert.ThrowsAsync<AuthenticationException>(() =>
                LoginAsync(fixture, acme, "admin@acme.test", totp: "000000"));
            Assert.Equal(AuthenticationFailure.MfaInvalid, wrong.Failure);
        }
        var locked = await Assert.ThrowsAsync<AuthenticationException>(() =>
            LoginAsync(fixture, acme, "admin@acme.test", totp: Code(secret, 1)));

        Assert.Equal(AuthenticationFailure.InvalidCredentials, locked.Failure);
        Assert.True((await fixture.Db.Accounts.AsNoTracking().SingleAsync(x => x.Id == adminId)).LockedUntilUtc > DateTime.UtcNow);
        var audit = Assert.Single(await AuditsAsync(fixture, AuthenticationService.MemberMfaLockedAction));
        Assert.Equal((acme.TenantId, adminId), (audit.TenantId, audit.ActorAccountId));

        // The signed-in methods share the lockout, so a stolen session cannot keep guessing.
        var version = Version(fixture, adminId);
        var throttled = await Assert.ThrowsAsync<AuthenticationException>(() =>
            fixture.Auth.RegenerateMemberRecoveryCodesAsync(acme.TenantId, adminId, version,
                new MfaCodeRequest(Code(secret, 1)), default));
        Assert.Equal(AuthenticationFailure.RateLimited, throttled.Failure);
    }

    [Fact]
    public async Task A_recovery_code_works_once_and_is_audited_without_the_code()
    {
        await using var fixture = await Fixture.CreateAsync();
        var (acme, _) = await OrganizationAsync(fixture, "Acme", "acme", "admin@acme.test", requireMfa: true);
        var (_, enrolled) = await EnrollByLoginAsync(fixture, acme, "admin@acme.test");
        var code = enrolled.RecoveryCodes![2];

        var used = await LoginAsync(fixture, acme, "admin@acme.test", recovery: code.ToLowerInvariant().Replace("-", " "));
        var again = await Assert.ThrowsAsync<AuthenticationException>(() =>
            LoginAsync(fixture, acme, "admin@acme.test", recovery: code));
        var viaSignIn = await fixture.Auth.SignInAsync(
            new SignInRequest("admin@acme.test", Password, RecoveryCode: enrolled.RecoveryCodes[3]), default);

        Assert.NotNull(used.AccessToken);
        Assert.Equal(AuthenticationFailure.MfaInvalid, again.Failure);
        Assert.NotNull(viaSignIn.Session);
        Assert.Equal(8, await fixture.Db.MfaRecoveryCodes.CountAsync(x => x.UsedAtUtc == null));
        var audits = await AuditsAsync(fixture, AuthenticationService.MemberRecoveryCodeUsedAction);
        Assert.Equal(2, audits.Count);
        Assert.All(audits, x => Assert.Equal(acme.TenantId, x.TenantId));
        Assert.All(audits, x => Assert.DoesNotContain(code.Replace("-", ""), x.AfterJson));
    }

    [Fact]
    public async Task A_member_may_opt_in_regenerate_recovery_codes_with_a_code_and_opt_out_when_not_required()
    {
        await using var fixture = await Fixture.CreateAsync();
        var (acme, adminId) = await OrganizationAsync(fixture, "Acme", "acme", "admin@acme.test");
        var staffId = await AddEmployeeAsync(fixture, acme, adminId, "staff@acme.test");
        var before = await fixture.Auth.GetMemberMfaStatusAsync(acme.TenantId, staffId, Version(fixture, staffId), default);

        var (secret, codes) = await OptInAsync(fixture, acme.TenantId, staffId);
        var version = Version(fixture, staffId);
        var after = await fixture.Auth.GetMemberMfaStatusAsync(acme.TenantId, staffId, version, default);
        var alreadyEnrolled = await Assert.ThrowsAsync<AuthenticationException>(() =>
            fixture.Auth.BeginMemberMfaEnrollmentAsync(acme.TenantId, staffId, version, default));

        Assert.Equal(new MemberMfaStatus(false, null, 0, false, false), before);
        Assert.True(after.Enabled);
        Assert.Equal((10, false, true), (after.RecoveryCodesRemaining, after.RequiredByOrganization, after.CanDisable));
        Assert.Equal(AuthenticationFailure.MfaAlreadyEnrolled, alreadyEnrolled.Failure);
        // An account's authenticator protects every sign-in, even where the organization does not require it.
        var asked = await Assert.ThrowsAsync<AuthenticationException>(() => LoginAsync(fixture, acme, "staff@acme.test"));
        Assert.Equal(AuthenticationFailure.MfaRequired, asked.Failure);

        // Regenerating needs a current authenticator code; a recovery code or no code is not enough.
        var noCode = await Assert.ThrowsAsync<AuthenticationException>(() => fixture.Auth.RegenerateMemberRecoveryCodesAsync(
            acme.TenantId, staffId, version, new MfaCodeRequest(null, codes[0]), default));
        var wrong = await Assert.ThrowsAsync<AuthenticationException>(() => fixture.Auth.RegenerateMemberRecoveryCodesAsync(
            acme.TenantId, staffId, version, new MfaCodeRequest("000000"), default));
        var fresh = await fixture.Auth.RegenerateMemberRecoveryCodesAsync(acme.TenantId, staffId, version,
            new MfaCodeRequest(Code(secret, 1)), default);
        Assert.Equal(AuthenticationFailure.MfaRequired, noCode.Failure);
        Assert.Equal(AuthenticationFailure.MfaInvalid, wrong.Failure);
        Assert.Equal(10, fresh.RecoveryCodes.Count);
        Assert.Empty(fresh.RecoveryCodes.Intersect(codes));
        await Assert.ThrowsAsync<AuthenticationException>(() => LoginAsync(fixture, acme, "staff@acme.test", recovery: codes[0]));
        Assert.NotNull(await LoginAsync(fixture, acme, "staff@acme.test", recovery: fresh.RecoveryCodes[0]));
        Assert.Single(await AuditsAsync(fixture, "member.mfa_recovery_codes_regenerated"));

        // Opting out is refused while an organization requires MFA, and allowed otherwise.
        await SetRequireAsync(fixture, acme.TenantId, true);
        var required = await Assert.ThrowsAsync<AuthenticationException>(() => fixture.Auth.DisableMemberMfaAsync(
            acme.TenantId, staffId, version, new MfaCodeRequest(null, fresh.RecoveryCodes[1]), default));
        Assert.Equal(AuthenticationFailure.MfaRequiredByOrganization, required.Failure);
        await SetRequireAsync(fixture, acme.TenantId, false);
        var badCode = await Assert.ThrowsAsync<AuthenticationException>(() => fixture.Auth.DisableMemberMfaAsync(
            acme.TenantId, staffId, version, new MfaCodeRequest("000000"), default));
        Assert.Equal(AuthenticationFailure.MfaInvalid, badCode.Failure);
        await fixture.Auth.DisableMemberMfaAsync(acme.TenantId, staffId, version,
            new MfaCodeRequest(null, fresh.RecoveryCodes[1]), default);
        var notEnrolled = await Assert.ThrowsAsync<AuthenticationException>(() => fixture.Auth.DisableMemberMfaAsync(
            acme.TenantId, staffId, version, new MfaCodeRequest("000000"), default));

        Assert.Equal(AuthenticationFailure.MfaNotEnrolled, notEnrolled.Failure);
        Assert.NotNull(await LoginAsync(fixture, acme, "staff@acme.test"));
        Assert.Empty(await fixture.Db.MfaRecoveryCodes.Where(x => x.AccountId == staffId).ToListAsync());
        Assert.Single(await AuditsAsync(fixture, "member.mfa_disabled"));
    }

    [Fact]
    public async Task An_admin_resets_a_members_mfa_with_a_reason_and_the_member_must_enroll_again()
    {
        await using var fixture = await Fixture.CreateAsync();
        var (acme, adminId) = await OrganizationAsync(fixture, "Acme", "acme", "admin@acme.test", requireMfa: true);
        var staffId = await AddEmployeeAsync(fixture, acme, adminId, "staff@acme.test");
        await EnrollByLoginAsync(fixture, acme, "admin@acme.test");
        var (_, staffSession) = await EnrollByLoginAsync(fixture, acme, "staff@acme.test");
        var members = Members(fixture);
        var (beta, betaAdminId) = await OrganizationAsync(fixture, "Beta", "beta", "boss@beta.test");
        var request = new ResetTenantMemberMfaRequest("Lost phone");

        var byEmployee = await Assert.ThrowsAsync<TenantMemberException>(() => members.ResetMemberMfaAsync(
            acme.TenantId, staffId, Version(fixture, staffId), adminId, request, default));
        var self = await Assert.ThrowsAsync<TenantMemberException>(() => members.ResetMemberMfaAsync(
            acme.TenantId, adminId, Version(fixture, adminId), adminId, request, default));
        var otherOrganization = await Assert.ThrowsAsync<TenantMemberException>(() => members.ResetMemberMfaAsync(
            beta.TenantId, betaAdminId, Version(fixture, betaAdminId), staffId, request, default));
        var longReason = await Assert.ThrowsAsync<TenantMemberException>(() => members.ResetMemberMfaAsync(
            acme.TenantId, adminId, Version(fixture, adminId), staffId, request with { Reason = new string('x', 501) }, default));
        var stale = await Assert.ThrowsAsync<TenantMemberException>(() => members.ResetMemberMfaAsync(
            acme.TenantId, adminId, Version(fixture, adminId) + 1, staffId, request, default));
        await members.ResetMemberMfaAsync(acme.TenantId, adminId, Version(fixture, adminId), staffId, request, default);

        Assert.Equal(TenantMemberFailure.Unauthorized, byEmployee.Failure);
        Assert.Equal(TenantMemberFailure.SelfChange, self.Failure);
        Assert.Equal(TenantMemberFailure.NotFound, otherOrganization.Failure);
        Assert.Equal(TenantMemberFailure.InvalidInput, longReason.Failure);
        Assert.Equal(TenantMemberFailure.Unauthorized, stale.Failure);
        fixture.Db.ChangeTracker.Clear();
        var account = await fixture.Db.Accounts.SingleAsync(x => x.Id == staffId);
        Assert.Null(account.MfaSecretProtected);
        Assert.Null(account.MfaEnabledAtUtc);
        Assert.Empty(await fixture.Db.MfaRecoveryCodes.Where(x => x.AccountId == staffId).ToListAsync());
        await Assert.ThrowsAsync<AuthenticationException>(() => fixture.Auth.RefreshAsync(
            new RefreshRequest(staffSession.RefreshToken), default));
        Assert.Contains(fixture.Revocations.AccountEvents, x => x.AccountId == staffId && x.Version == account.SessionVersion);
        var audit = Assert.Single(await AuditsAsync(fixture, "member.mfa_reset"));
        Assert.Equal((acme.TenantId, adminId, "Lost phone"), (audit.TenantId, audit.ActorAccountId, audit.Reason));
        Assert.Contains("staff@acme.test", audit.BeforeJson);
        Assert.Contains("\"MfaEnabled\":true", audit.BeforeJson);
        Assert.Contains("\"MfaEnabled\":false", audit.AfterJson);
        var again = await Assert.ThrowsAsync<AuthenticationException>(() => LoginAsync(fixture, acme, "staff@acme.test"));
        Assert.Equal(AuthenticationFailure.MfaEnrollmentRequired, again.Failure);

        // The reset is visible in the directory as a member without MFA.
        var directory = await members.ListMembersAsync(acme.TenantId, adminId, Version(fixture, adminId), default);
        Assert.Equal([true, false], directory.OrderByDescending(x => x.Role).Select(x => x.MfaEnabled));
    }

    [Fact]
    public async Task Another_organizations_admin_sees_nothing_and_its_members_are_not_asked_for_a_code()
    {
        await using var fixture = await Fixture.CreateAsync();
        var (acme, acmeAdminId) = await OrganizationAsync(fixture, "Acme", "acme", "admin@acme.test", requireMfa: true);
        var staffId = await AddEmployeeAsync(fixture, acme, acmeAdminId, "staff@acme.test");
        var (beta, betaAdminId) = await OrganizationAsync(fixture, "Beta", "beta", "boss@beta.test");
        await AddEmployeeAsync(fixture, beta, betaAdminId, "worker@beta.test");
        await EnrollByLoginAsync(fixture, acme, "staff@acme.test");
        var members = Members(fixture);

        var reset = await Assert.ThrowsAsync<TenantMemberException>(() => members.ResetMemberMfaAsync(
            beta.TenantId, betaAdminId, Version(fixture, betaAdminId), staffId,
            new ResetTenantMemberMfaRequest(null), default));
        var betaDirectory = await members.ListMembersAsync(beta.TenantId, betaAdminId, Version(fixture, betaAdminId), default);
        var betaLogin = await LoginAsync(fixture, beta, "worker@beta.test");
        await members.SetMfaRequirementAsync(acme.TenantId, acmeAdminId, Version(fixture, acmeAdminId),
            new SetTenantMfaRequirementRequest(false, null), default);

        Assert.Equal(TenantMemberFailure.NotFound, reset.Failure);
        Assert.Equal(["boss@beta.test", "worker@beta.test"], betaDirectory.Select(x => x.Email).Order());
        Assert.All(betaDirectory, x => Assert.False(x.MfaEnabled));
        Assert.Null(betaLogin.RecoveryCodes);
        // Only the organization that changed its setting has it in its audit log.
        var audits = await fixture.Db.PlatformOrganizationAudits.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.Action.StartsWith("organization.mfa") || x.Action.StartsWith("member.mfa")).ToListAsync();
        Assert.All(audits, x => Assert.Equal(acme.TenantId, x.TenantId));
        // Beta's own settings are untouched by Acme's.
        Assert.False((await fixture.Db.Tenants.IgnoreQueryFilters().AsNoTracking().SingleAsync(x => x.Id == beta.TenantId)).RequireMfa);
    }

    [Fact]
    public async Task An_enrolled_account_is_asked_in_every_organization_but_only_a_requiring_one_forces_enrollment()
    {
        await using var fixture = await Fixture.CreateAsync();
        var (acme, _) = await OrganizationAsync(fixture, "Acme", "acme", "admin@acme.test", requireMfa: true);
        var beta = await fixture.Onboarding.CreateTenantAsync(new CreateTenantRequest("Beta", "beta", "owner@beta.test"), default);
        var account = await fixture.Db.Accounts.SingleAsync(x => x.Email == "admin@acme.test");
        fixture.Db.Memberships.Add(new Membership
        {
            Id = Guid.NewGuid(), TenantId = beta.TenantId, AccountId = account.Id, Role = TenantRole.Employee,
            Status = MembershipStatus.Active, CreatedAtUtc = DateTime.UtcNow
        });
        await fixture.Db.SaveChangesAsync();

        // Before enrolling, Beta (which does not require it) signs the member in with a password alone.
        Assert.NotNull(await LoginAsync(fixture, beta, "admin@acme.test"));
        // Choosing an organization needs only the password; the second factor comes with the login.
        var chooser = await fixture.Auth.SignInAsync(new SignInRequest("admin@acme.test", Password), default);
        Assert.Null(chooser.Session);
        var (secret, _) = await EnrollByLoginAsync(fixture, acme, "admin@acme.test");

        var asked = await Assert.ThrowsAsync<AuthenticationException>(() => LoginAsync(fixture, beta, "admin@acme.test"));
        var answered = await LoginAsync(fixture, beta, "admin@acme.test", totp: Code(secret, 1));
        Assert.Equal(AuthenticationFailure.MfaRequired, asked.Failure);
        Assert.Equal(beta.TenantId, answered.TenantId);
        Assert.Null(answered.RecoveryCodes);
    }

    [Fact]
    public async Task A_session_issued_before_the_requirement_cannot_be_renewed_without_an_authenticator()
    {
        await using var fixture = await Fixture.CreateAsync();
        var (acme, adminId) = await OrganizationAsync(fixture, "Acme", "acme", "admin@acme.test");
        await AddEmployeeAsync(fixture, acme, adminId, "staff@acme.test");
        var session = await LoginAsync(fixture, acme, "staff@acme.test");

        await SetRequireAsync(fixture, acme.TenantId, true);

        await Assert.ThrowsAsync<AuthenticationException>(() => fixture.Auth.RefreshAsync(
            new RefreshRequest(session.RefreshToken), default));
    }

    [Fact]
    public async Task Resetting_the_password_never_removes_the_second_factor_and_activation_asks_for_none()
    {
        await using var fixture = await Fixture.CreateAsync();
        var (acme, adminId) = await OrganizationAsync(fixture, "Acme", "acme", "admin@acme.test", requireMfa: true);
        // Activation sets the password only; enrollment happens at the first sign-in.
        await AddEmployeeAsync(fixture, acme, adminId, "staff@acme.test");
        var (secret, _) = await EnrollByLoginAsync(fixture, acme, "staff@acme.test");

        await fixture.Auth.RequestPasswordResetAsync(new PasswordResetRequest("staff@acme.test"), default);
        await fixture.Auth.CompletePasswordResetAsync(new CompleteCredentialChallenge(
            fixture.Notifications.Messages.Last().Code, "New-Correct-Password-456!"), default);

        var asked = await Assert.ThrowsAsync<AuthenticationException>(() => fixture.Auth.LoginAsync(
            new LoginRequest(acme.TenantId, "staff@acme.test", "New-Correct-Password-456!"), default));
        Assert.Equal(AuthenticationFailure.MfaRequired, asked.Failure);
        Assert.NotNull(await fixture.Auth.LoginAsync(new LoginRequest
        {
            TenantId = acme.TenantId, Email = "staff@acme.test", Password = "New-Correct-Password-456!",
            TotpCode = Code(secret, 1)
        }, default));
    }

    [Fact]
    public async Task Enrollment_offers_are_refused_when_not_needed_and_the_signed_in_methods_check_the_session()
    {
        await using var fixture = await Fixture.CreateAsync();
        var (acme, adminId) = await OrganizationAsync(fixture, "Acme", "acme", "admin@acme.test");

        var notRequired = await Assert.ThrowsAsync<AuthenticationException>(() =>
            fixture.Auth.BeginRequiredMfaEnrollmentAsync(new MemberMfaEnrollmentRequest("admin@acme.test", Password), default));
        var stale = await Assert.ThrowsAsync<AuthenticationException>(() =>
            fixture.Auth.GetMemberMfaStatusAsync(acme.TenantId, adminId, Version(fixture, adminId) + 1, default));
        var blank = await Assert.ThrowsAsync<AuthenticationException>(() =>
            fixture.Auth.ConfirmMemberMfaEnrollmentAsync(acme.TenantId, adminId, Version(fixture, adminId),
                new MfaCodeRequest(" "), default));
        var notEnrolled = await Assert.ThrowsAsync<AuthenticationException>(() =>
            fixture.Auth.RegenerateMemberRecoveryCodesAsync(acme.TenantId, adminId, Version(fixture, adminId),
                new MfaCodeRequest("123456"), default));
        await SetRequireAsync(fixture, acme.TenantId, true);
        await EnrollByLoginAsync(fixture, acme, "admin@acme.test");
        var already = await Assert.ThrowsAsync<AuthenticationException>(() =>
            fixture.Auth.BeginRequiredMfaEnrollmentAsync(new MemberMfaEnrollmentRequest("admin@acme.test", Password), default));

        Assert.Equal(AuthenticationFailure.MfaNotRequired, notRequired.Failure);
        Assert.Equal(AuthenticationFailure.InvalidCredentials, stale.Failure);
        Assert.Equal(AuthenticationFailure.InvalidInput, blank.Failure);
        Assert.Equal(AuthenticationFailure.MfaNotEnrolled, notEnrolled.Failure);
        Assert.Equal(AuthenticationFailure.MfaAlreadyEnrolled, already.Failure);
    }

    private static async Task<(CreateTenantResponse Tenant, Guid AdminId)> OrganizationAsync(Fixture fixture,
        string name, string slug, string adminEmail, bool requireMfa = false)
    {
        var tenant = await ActivateTenantAdminAsync(fixture, name, slug, adminEmail);
        var adminId = await fixture.Db.Accounts.Where(x => x.Email == adminEmail).Select(x => x.Id).SingleAsync();
        if (requireMfa) await SetRequireAsync(fixture, tenant.TenantId, true);
        return (tenant, adminId);
    }

    private static async Task SetRequireAsync(Fixture fixture, Guid tenantId, bool required)
    {
        fixture.Scope.TenantId = tenantId;
        (await fixture.Db.Tenants.SingleAsync()).RequireMfa = required;
        await fixture.Db.SaveChangesAsync();
    }

    private static async Task<Guid> AddEmployeeAsync(Fixture fixture, CreateTenantResponse tenant, Guid adminId,
        string email)
    {
        var issued = await fixture.Onboarding.IssueInvitationAuthorizedAsync(tenant.TenantId, adminId,
            Version(fixture, adminId), new IssueInvitationRequest(email, "Employee"), default);
        await fixture.Onboarding.AcceptInvitationAsync(new AcceptInvitationRequest(issued.Code), default);
        await fixture.Auth.RequestActivationAsync(new RequestCredentialEmail(tenant.TenantId, email), default);
        await fixture.Auth.CompleteActivationAsync(new CompleteCredentialChallenge(
            fixture.Notifications.Messages.Last().Code, Password), default);
        return await fixture.Db.Accounts.Where(x => x.Email == email).Select(x => x.Id).SingleAsync();
    }

    private static Task<AuthenticationResponse> LoginAsync(Fixture fixture, CreateTenantResponse tenant,
        string email, string? totp = null, string? recovery = null) =>
        fixture.Auth.LoginAsync(new LoginRequest
        {
            TenantId = tenant.TenantId, Email = email, Password = Password, TotpCode = totp, RecoveryCode = recovery
        }, default);

    /// <summary>The first sign-in of a member whose organization requires MFA, as the client does it:
    /// take the offered secret, then confirm it with the first code.</summary>
    private static async Task<(string Secret, AuthenticationResponse Session)> EnrollByLoginAsync(
        Fixture fixture, CreateTenantResponse tenant, string email)
    {
        var offer = await fixture.Auth.BeginRequiredMfaEnrollmentAsync(
            new MemberMfaEnrollmentRequest(email, Password), default);
        return (offer.Secret, await LoginAsync(fixture, tenant, email, totp: Code(offer.Secret)));
    }

    private static async Task<(string Secret, IReadOnlyList<string> RecoveryCodes)> OptInAsync(Fixture fixture,
        Guid tenantId, Guid accountId)
    {
        var version = Version(fixture, accountId);
        var offer = await fixture.Auth.BeginMemberMfaEnrollmentAsync(tenantId, accountId, version, default);
        var codes = await fixture.Auth.ConfirmMemberMfaEnrollmentAsync(tenantId, accountId, version,
            new MfaCodeRequest(Code(offer.Secret)), default);
        return (offer.Secret, codes.RecoveryCodes);
    }

    private static TenantMemberService Members(Fixture fixture) => new(fixture.Db, fixture.Scope,
        fixture.Onboarding, new PlatformOrganizationService(fixture.Db, fixture.Scope, fixture.Revocations,
            fixture.Auth), fixture.Auth);

    private static long Version(Fixture fixture, Guid accountId) =>
        fixture.Db.Accounts.AsNoTracking().Where(x => x.Id == accountId).Select(x => x.SessionVersion).Single();

    private static string Code(string secret, long stepOffset = 0) =>
        Totp.Compute(secret, Totp.StepAt(DateTimeOffset.UtcNow) + stepOffset);

    private static Task<List<PlatformOrganizationAudit>> AuditsAsync(Fixture fixture, string action) =>
        fixture.Db.PlatformOrganizationAudits.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.Action == action).ToListAsync();
}
