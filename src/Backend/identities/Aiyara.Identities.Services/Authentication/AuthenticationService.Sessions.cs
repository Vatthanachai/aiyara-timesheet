using Aiyara.Identities.Models.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Aiyara.Identities.Services.Authentication;

/// <summary>Session management: the caller lists and ends their own refresh sessions. Every method
/// works on the token's account in the token's organization (the tenant filter applies), so another
/// account's session id, or one from another organization, is simply not found. Ending a session
/// revokes its refresh token; the access token already issued for it lives out its short lifetime.</summary>
public sealed partial class AuthenticationService
{
    public const string SessionRevokedAction = "member.session_revoked";
    public const string OtherSessionsRevokedAction = "member.other_sessions_revoked";
    public const string SignedOutEverywhereAction = "member.signed_out_everywhere";

    /// <summary>The caller's active sessions in this organization, most recently used first.</summary>
    public async Task<IReadOnlyList<MemberSession>> ListSessionsAsync(Guid tenantId, Guid accountId,
        long sessionVersion, Guid? currentSessionId, CancellationToken cancellationToken)
    {
        var account = await LoadSessionOwnerAsync(tenantId, accountId, sessionVersion, cancellationToken);
        var rows = await ActiveSessions(account).ToListAsync(cancellationToken);
        return rows.OrderByDescending(x => x.CreatedAtUtc).Select(x => new MemberSession(x.Id,
            SessionDeviceName.From(x.UserAgent), Utc(x.SignedInAtUtc ?? x.CreatedAtUtc),
            Utc(x.CreatedAtUtc), Utc(x.ExpiresAtUtc), x.Id == currentSessionId)).ToList();
    }

    /// <summary>Ends one other session. 404 when it is not an active session of the caller; the session
    /// the call is made with is refused (409) because ending it is signing out.</summary>
    public async Task RevokeSessionAsync(Guid tenantId, Guid accountId, long sessionVersion,
        Guid? currentSessionId, Guid sessionId, CancellationToken cancellationToken)
    {
        var account = await LoadSessionOwnerAsync(tenantId, accountId, sessionVersion, cancellationToken);
        var session = await ActiveSessions(account).SingleOrDefaultAsync(x => x.Id == sessionId,
            cancellationToken) ?? throw new AuthenticationException(AuthenticationFailure.NotFound,
            "Session not found.");
        if (session.Id == currentSessionId)
            throw new AuthenticationException(AuthenticationFailure.CurrentSession,
                "The current session cannot be ended here.");
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var revoked = await db.RefreshSessions.Where(x => x.Id == session.Id && x.RevokedAtUtc == null)
            .ExecuteUpdateAsync(x => x.SetProperty(s => s.RevokedAtUtc, DateTime.UtcNow), cancellationToken);
        if (revoked != 1) throw new AuthenticationException(AuthenticationFailure.NotFound, "Session not found.");
        AddMfaAudit(tenantId, SessionRevokedAction, account.Id, account.Email);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>Ends every active session in this organization except the caller's own. The caller's
    /// token must carry its session id and that session must be active (409 <c>SESSION_UNKNOWN</c>
    /// otherwise, which asks an older token to sign in again).</summary>
    public async Task RevokeOtherSessionsAsync(Guid tenantId, Guid accountId, long sessionVersion,
        Guid? currentSessionId, CancellationToken cancellationToken)
    {
        var account = await LoadSessionOwnerAsync(tenantId, accountId, sessionVersion, cancellationToken);
        if (currentSessionId is not { } current ||
            !await ActiveSessions(account).AnyAsync(x => x.Id == current, cancellationToken))
            throw new AuthenticationException(AuthenticationFailure.SessionUnknown,
                "The current session is not known.");
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.RefreshSessions.Where(x => x.AccountId == account.Id && x.Id != current &&
                x.RevokedAtUtc == null)
            .ExecuteUpdateAsync(x => x.SetProperty(s => s.RevokedAtUtc, DateTime.UtcNow), cancellationToken);
        AddMfaAudit(tenantId, OtherSessionsRevokedAction, account.Id, account.Email);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>Signs the account out everywhere, this session included: every refresh session in every
    /// organization is revoked and the session version moves on, so access tokens stop working too.</summary>
    public async Task RevokeAllSessionsAsync(Guid tenantId, Guid accountId, long sessionVersion,
        CancellationToken cancellationToken)
    {
        var account = await LoadSessionOwnerAsync(tenantId, accountId, sessionVersion, cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        account.SessionVersion++;
        await db.RefreshSessions.IgnoreQueryFilters().Where(x => x.AccountId == account.Id &&
                x.RevokedAtUtc == null)
            .ExecuteUpdateAsync(x => x.SetProperty(s => s.RevokedAtUtc, DateTime.UtcNow), cancellationToken);
        AddMfaAudit(tenantId, SignedOutEverywhereAction, account.Id, account.Email);
        await db.SaveChangesAsync(cancellationToken);
        await PublishAccountAcrossTenantsAsync(account.Id, account.SessionVersion, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    // Still-valid sessions of this account in the scoped organization: not revoked, not expired, and
    // issued under the current session version.
    private IQueryable<RefreshSession> ActiveSessions(Account account)
    {
        var now = DateTime.UtcNow;
        return db.RefreshSessions.Where(x => x.AccountId == account.Id && x.RevokedAtUtc == null &&
            x.ExpiresAtUtc > now && x.SessionVersion == account.SessionVersion);
    }

    /// <summary>The signed-in account: an active member of the token's organization (any role, a
    /// Platform Admin included) whose session version is current and who has no forced password change.</summary>
    private async Task<Account> LoadSessionOwnerAsync(Guid tenantId, Guid accountId, long sessionVersion,
        CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty || accountId == Guid.Empty) throw InvalidCredentials();
        tenantScope.TenantId = tenantId;
        var account = await db.Accounts.SingleOrDefaultAsync(x => x.Id == accountId, cancellationToken);
        var membership = await db.Memberships.SingleOrDefaultAsync(x => x.AccountId == accountId &&
            x.Status == MembershipStatus.Active, cancellationToken);
        var tenant = await db.Tenants.SingleOrDefaultAsync(cancellationToken);
        if (account is null || membership is null || tenant is null || tenant.Status != TenantStatus.Active ||
            account.SessionVersion != sessionVersion || account.MustChangePassword || membership.MustChangePassword)
            throw InvalidCredentials();
        return account;
    }

    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
