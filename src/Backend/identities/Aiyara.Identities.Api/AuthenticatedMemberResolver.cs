using Aiyara.Identities.Databases;
using Aiyara.Identities.Models.Tenancy;
using Aiyara.Timesheet.Component.Abstractions.Securities;
using Microsoft.EntityFrameworkCore;

namespace Aiyara.Identities.Api;

public sealed record AuthenticatedMember(Account Account, Guid TenantId, TenantRole Role, long SessionVersion);

/// <summary>Resolves the bearer token to an active member of the token's tenant whose role and
/// session version still match, with no forced password change. Sets the tenant scope.</summary>
public sealed class AuthenticatedMemberResolver(IdentityDbContext db, IPasetoTokenService tokens, TenantScope scope)
{
    public async Task<AuthenticatedMember?> ResolveAsync(HttpRequest request, CancellationToken ct)
    {
        var authorization = request.Headers.Authorization.ToString();
        var claims = authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? tokens.ValidateToken(authorization[7..].Trim()) : null;
        if (claims is null || claims.MustChangePassword) return null;
        scope.TenantId = claims.TenantId;
        var membership = await db.Memberships.SingleOrDefaultAsync(x => x.AccountId == claims.UserId &&
            x.Status == MembershipStatus.Active, ct);
        if (membership is null || membership.Role.ToString() != claims.Role) return null;
        var account = await db.Accounts.SingleOrDefaultAsync(x => x.Id == claims.UserId, ct);
        return account is not null && account.SessionVersion == claims.SessionVersion &&
            !account.MustChangePassword && !membership.MustChangePassword
            ? new AuthenticatedMember(account, claims.TenantId, membership.Role, account.SessionVersion)
            : null;
    }
}
