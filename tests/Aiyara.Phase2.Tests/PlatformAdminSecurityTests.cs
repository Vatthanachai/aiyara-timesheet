using System.Security.Cryptography;
using System.Text;
using Aiyara.Identities.Models.Tenancy;
using Aiyara.Identities.Services.Authentication;
using Aiyara.Identities.Services.Onboarding;
using Aiyara.Timesheet.Contracts.Onboarding.V1;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static Aiyara.Phase2.Tests.AuthenticationFlowTests;

namespace Aiyara.Phase2.Tests;

/// <summary>Phase D of the Platform Admin console: TOTP for Platform Admin sign-in (D2) and the
/// platform tenant's password policy (D1).</summary>
public sealed class PlatformAdminSecurityTests
{
    private const string Email = "operator@platform.test";
    private const string Password = "Correct-Password-123!";

    [Theory]
    [InlineData(59, "287082")]
    [InlineData(1111111109, "081804")]
    [InlineData(1234567890, "005924")]
    public void Totp_matches_the_rfc_6238_sha1_test_vectors(long unixSeconds, string expected)
    {
        var secret = Base32("12345678901234567890");

        Assert.Equal(expected, Totp.Compute(secret, Totp.StepAt(DateTimeOffset.FromUnixTimeSeconds(unixSeconds))));
    }

    [Fact]
    public void Totp_accepts_one_step_of_drift_but_never_a_step_already_used()
    {
        var secret = Totp.GenerateSecret();
        var now = DateTimeOffset.UtcNow;
        var step = Totp.StepAt(now);

        Assert.True(Totp.TryVerify(secret, Totp.Compute(secret, step - 1), now, 0, out var drifted));
        Assert.Equal(step - 1, drifted);
        Assert.False(Totp.TryVerify(secret, Totp.Compute(secret, step), now, step, out _));
        Assert.False(Totp.TryVerify(secret, Totp.Compute(secret, step - 2), now, 0, out _));
        Assert.False(Totp.TryVerify(secret, "12345", now, 0, out _));
        Assert.Contains($"secret={secret}", Totp.ProvisioningUri("Aiyara Timesheet", Email, secret));
    }

    [Fact]
    public void Secret_protector_round_trips_and_rejects_tampering_or_a_different_key()
    {
        var protector = new AesGcmMfaSecretProtector(Key());
        var protectedSecret = protector.Protect("JBSWY3DPEHPK3PXP");
        var tampered = Convert.FromBase64String(protectedSecret);
        tampered[^1] ^= 0x01;

        Assert.Equal("JBSWY3DPEHPK3PXP", protector.Unprotect(protectedSecret));
        Assert.NotEqual(protectedSecret, protector.Protect("JBSWY3DPEHPK3PXP"));
        Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect(Convert.ToBase64String(tampered)));
        Assert.ThrowsAny<CryptographicException>(() => new AesGcmMfaSecretProtector(Key()).Unprotect(protectedSecret));
        Assert.Throws<InvalidOperationException>(() => new AesGcmMfaSecretProtector(Convert.ToBase64String(new byte[16])));
        Assert.Throws<InvalidOperationException>(() => new AesGcmMfaSecretProtector(null));
    }

    [Fact]
    public async Task Enrollment_keeps_offering_the_same_secret_and_stores_it_encrypted()
    {
        await using var fixture = await PlatformAdminAsync();

        var first = await fixture.Auth.LoginPlatformAdminAsync(new PlatformAdminLoginRequest(Email, Password), default);
        var second = await fixture.Auth.LoginPlatformAdminAsync(new PlatformAdminLoginRequest(Email, Password), default);
        var wrong = await Assert.ThrowsAsync<AuthenticationException>(() => fixture.Auth.LoginPlatformAdminAsync(
            new PlatformAdminLoginRequest(Email, Password, "000000"), default));

        Assert.Null(first.Session);
        Assert.Equal(first.Enrollment!.Secret, second.Enrollment!.Secret);
        Assert.StartsWith("otpauth://totp/Aiyara%20Timesheet:", first.Enrollment.ProvisioningUri);
        Assert.Equal(AuthenticationFailure.MfaInvalid, wrong.Failure);
        var account = await fixture.Db.Accounts.AsNoTracking().SingleAsync(x => x.Email == Email);
        Assert.Null(account.MfaSecretProtected);
        Assert.DoesNotContain(first.Enrollment.Secret, account.MfaPendingSecretProtected);

        await EnrollPlatformAdminAsync(fixture, Email, Password);
        account = await fixture.Db.Accounts.AsNoTracking().SingleAsync(x => x.Email == Email);
        Assert.NotNull(account.MfaEnabledAtUtc);
        Assert.Null(account.MfaPendingSecretProtected);
        Assert.DoesNotContain(first.Enrollment.Secret, account.MfaSecretProtected);
        Assert.Equal(10, await fixture.Db.MfaRecoveryCodes.CountAsync(x => x.AccountId == account.Id));
    }

    [Fact]
    public async Task An_enrolled_admin_needs_a_fresh_code_and_a_replayed_one_is_refused()
    {
        await using var fixture = await PlatformAdminAsync();
        var secret = await EnrollAndGetSecretAsync(fixture);

        var missing = await Assert.ThrowsAsync<AuthenticationException>(() => fixture.Auth.LoginPlatformAdminAsync(
            new PlatformAdminLoginRequest(Email, Password), default));
        var replayed = await Assert.ThrowsAsync<AuthenticationException>(() => fixture.Auth.LoginPlatformAdminAsync(
            new PlatformAdminLoginRequest(Email, Password, Totp.Compute(secret, Totp.StepAt(DateTimeOffset.UtcNow))), default));
        var next = await fixture.Auth.LoginPlatformAdminAsync(new PlatformAdminLoginRequest(Email, Password,
            Totp.Compute(secret, Totp.StepAt(DateTimeOffset.UtcNow) + 1)), default);

        Assert.Equal(AuthenticationFailure.MfaRequired, missing.Failure);
        Assert.Equal(AuthenticationFailure.MfaInvalid, replayed.Failure);
        Assert.NotNull(next.Session);
        Assert.Empty(next.RecoveryCodes);
    }

    [Fact]
    public async Task Five_wrong_codes_lock_the_account_even_with_the_right_password()
    {
        await using var fixture = await PlatformAdminAsync();
        var secret = await EnrollAndGetSecretAsync(fixture);

        for (var attempt = 0; attempt < 5; attempt++)
            await Assert.ThrowsAsync<AuthenticationException>(() => fixture.Auth.LoginPlatformAdminAsync(
                new PlatformAdminLoginRequest(Email, Password, "000000"), default));
        var locked = await Assert.ThrowsAsync<AuthenticationException>(() => fixture.Auth.LoginPlatformAdminAsync(
            new PlatformAdminLoginRequest(Email, Password, Totp.Compute(secret, Totp.StepAt(DateTimeOffset.UtcNow) + 1)), default));

        Assert.Equal(AuthenticationFailure.InvalidCredentials, locked.Failure);
        Assert.True((await fixture.Db.Accounts.AsNoTracking().SingleAsync(x => x.Email == Email)).LockedUntilUtc > DateTime.UtcNow);
        Assert.Equal(6, await fixture.Db.PlatformOrganizationAudits.IgnoreQueryFilters()
            .CountAsync(x => x.Action == AuthenticationService.PlatformSignInFailedAction));
    }

    [Fact]
    public async Task A_recovery_code_signs_in_once()
    {
        await using var fixture = await PlatformAdminAsync();
        var offer = await fixture.Auth.LoginPlatformAdminAsync(new PlatformAdminLoginRequest(Email, Password), default);
        var enrolled = await fixture.Auth.LoginPlatformAdminAsync(new PlatformAdminLoginRequest(Email, Password,
            Totp.Compute(offer.Enrollment!.Secret, Totp.StepAt(DateTimeOffset.UtcNow))), default);
        var code = enrolled.RecoveryCodes[3];

        var used = await fixture.Auth.LoginPlatformAdminAsync(new PlatformAdminLoginRequest(Email, Password,
            RecoveryCode: code.ToLowerInvariant().Replace("-", " ")), default);
        var again = await Assert.ThrowsAsync<AuthenticationException>(() => fixture.Auth.LoginPlatformAdminAsync(
            new PlatformAdminLoginRequest(Email, Password, RecoveryCode: code), default));

        Assert.NotNull(used.Session);
        Assert.Equal(AuthenticationFailure.MfaInvalid, again.Failure);
        Assert.Matches("^[A-Z2-9]{5}-[A-Z2-9]{5}$", code);
        Assert.Equal(1, await fixture.Db.PlatformOrganizationAudits.IgnoreQueryFilters()
            .CountAsync(x => x.Action == AuthenticationService.RecoveryCodeUsedAction));
        Assert.Equal(9, await fixture.Db.MfaRecoveryCodes.CountAsync(x => x.UsedAtUtc == null));
    }

    [Fact]
    public async Task Another_admin_resets_mfa_and_the_admin_enrolls_again()
    {
        await using var fixture = await PlatformAdminAsync();
        await EnrollAndGetSecretAsync(fixture);
        var second = await AddActivePlatformAdminAsync(fixture, "second@platform.test");
        var operatorId = await fixture.Db.Accounts.Where(x => x.Email == Email).Select(x => x.Id).SingleAsync();
        var organizations = new PlatformOrganizationService(fixture.Db, fixture.Scope, fixture.Revocations);
        var roster = new PlatformAdminRosterService(fixture.Db, fixture.Scope, organizations);

        var operatorVersion = await VersionAsync(fixture, operatorId);
        var self = await Assert.ThrowsAsync<PlatformOrganizationException>(() =>
            roster.ResetMfaAsync(operatorId, operatorVersion, new ResetPlatformAdminMfaRequest(Email, null), default));
        await roster.ResetMfaAsync(second, 0, new ResetPlatformAdminMfaRequest(Email, "Lost phone"), default);

        Assert.Equal(PlatformOrganizationFailure.SelfChange, self.Failure);
        fixture.Db.ChangeTracker.Clear();
        var account = await fixture.Db.Accounts.SingleAsync(x => x.Id == operatorId);
        Assert.Null(account.MfaSecretProtected);
        Assert.Null(account.MfaEnabledAtUtc);
        Assert.Empty(await fixture.Db.MfaRecoveryCodes.Where(x => x.AccountId == operatorId).ToListAsync());
        Assert.Contains(fixture.Revocations.AccountEvents, x => x.AccountId == operatorId && x.Version == account.SessionVersion);
        var audit = await fixture.Db.PlatformOrganizationAudits.IgnoreQueryFilters().SingleAsync(x => x.Action == "platform_admin.mfa_reset");
        Assert.Equal(second, audit.ActorAccountId);
        Assert.Equal("Lost phone", audit.Reason);
        var offer = await fixture.Auth.LoginPlatformAdminAsync(new PlatformAdminLoginRequest(Email, Password), default);
        Assert.NotNull(offer.Enrollment);
    }

    [Fact]
    public async Task Platform_password_policy_is_audited_and_a_stricter_one_forces_a_change()
    {
        await using var fixture = await PlatformAdminAsync();
        await EnrollAndGetSecretAsync(fixture);
        var operatorId = await fixture.Db.Accounts.Where(x => x.Email == Email).Select(x => x.Id).SingleAsync();
        var organizations = new PlatformOrganizationService(fixture.Db, fixture.Scope, fixture.Revocations);
        var security = new PlatformSecurityService(fixture.Db, organizations, fixture.Auth);
        var version = await VersionAsync(fixture, operatorId);

        var outsider = await Assert.ThrowsAsync<PlatformOrganizationException>(() => security.UpdatePasswordPolicyAsync(
            Guid.NewGuid(), 0, new UpdatePlatformPasswordPolicyRequest(16, 90, true, true, true, true, null), default));
        var invalid = await Assert.ThrowsAsync<PlatformOrganizationException>(() => security.UpdatePasswordPolicyAsync(
            operatorId, version, new UpdatePlatformPasswordPolicyRequest(8, 90, true, true, true, true, null), default));
        await security.UpdatePasswordPolicyAsync(operatorId, version,
            new UpdatePlatformPasswordPolicyRequest(16, 90, true, true, true, true, "Annual review"), default);

        Assert.Equal(PlatformOrganizationFailure.Unauthorized, outsider.Failure);
        Assert.Equal(PlatformOrganizationFailure.InvalidInput, invalid.Failure);
        Assert.Equal(new PlatformPasswordPolicy(16, 90, true, true, true, true), await security.GetPasswordPolicyAsync(default));
        fixture.Db.ChangeTracker.Clear();
        Assert.True((await fixture.Db.Memberships.IgnoreQueryFilters().SingleAsync(x => x.AccountId == operatorId)).MustChangePassword);
        var audit = await fixture.Db.PlatformOrganizationAudits.IgnoreQueryFilters()
            .SingleAsync(x => x.Action == PlatformSecurityService.PasswordPolicyUpdatedAction);
        Assert.Equal(PlatformAdminBootstrap.PlatformTenantId, audit.TenantId);
        Assert.Equal("Annual review", audit.Reason);
        Assert.Contains("\"MinimumLength\":12", audit.BeforeJson);
        Assert.Contains("\"MinimumLength\":16", audit.AfterJson);
    }

    private static async Task<Fixture> PlatformAdminAsync()
    {
        var fixture = await Fixture.CreateAsync();
        await new PlatformAdminBootstrap(fixture.Db, fixture.Scope).EnsureAsync(Email, default);
        await fixture.Auth.RequestActivationAsync(new RequestCredentialEmail(PlatformAdminBootstrap.PlatformTenantId, Email), default);
        await fixture.Auth.CompleteActivationAsync(new CompleteCredentialChallenge(
            fixture.Notifications.Messages.Last().Code, Password), default);
        return fixture;
    }

    private static async Task<string> EnrollAndGetSecretAsync(Fixture fixture)
    {
        var offer = await fixture.Auth.LoginPlatformAdminAsync(new PlatformAdminLoginRequest(Email, Password), default);
        var secret = offer.Enrollment!.Secret;
        await fixture.Auth.LoginPlatformAdminAsync(new PlatformAdminLoginRequest(Email, Password,
            Totp.Compute(secret, Totp.StepAt(DateTimeOffset.UtcNow))), default);
        return secret;
    }

    private static async Task<Guid> AddActivePlatformAdminAsync(Fixture fixture, string email)
    {
        var id = Guid.NewGuid();
        fixture.Db.Accounts.Add(new Account { Id = id, Email = email, IsPlatformAdmin = true, CreatedAtUtc = DateTime.UtcNow });
        fixture.Db.Memberships.Add(new Membership { Id = Guid.NewGuid(), TenantId = PlatformAdminBootstrap.PlatformTenantId,
            AccountId = id, Role = TenantRole.PlatformAdmin, Status = MembershipStatus.Active, CreatedAtUtc = DateTime.UtcNow });
        await fixture.Db.SaveChangesAsync();
        return id;
    }

    private static Task<long> VersionAsync(Fixture fixture, Guid accountId) =>
        fixture.Db.Accounts.AsNoTracking().Where(x => x.Id == accountId).Select(x => x.SessionVersion).SingleAsync();

    private static string Key() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private static string Base32(string ascii)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bits = string.Concat(Encoding.ASCII.GetBytes(ascii).Select(b => Convert.ToString(b, 2).PadLeft(8, '0')));
        return string.Concat(Enumerable.Range(0, bits.Length / 5).Select(i => alphabet[Convert.ToInt32(bits.Substring(i * 5, 5), 2)]));
    }
}
