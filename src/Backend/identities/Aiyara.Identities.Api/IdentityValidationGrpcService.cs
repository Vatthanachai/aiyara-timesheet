using Aiyara.Identities.Databases;
using Aiyara.Identities.Models.Tenancy;
using Aiyara.Timesheet.Component.Abstractions.Securities;
using Aiyara.Timesheet.Contracts.Identity.V1;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;

internal sealed class IdentityValidationGrpcService(
    IdentityDbContext db, TenantScope tenantScope, IPasetoTokenService tokens)
    : IdentityValidationService.IdentityValidationServiceBase
{
    public override async Task<ValidateAccessTokenResponse> ValidateAccessToken(
        ValidateAccessTokenRequest request, ServerCallContext context)
    {
        var claims = tokens.ValidateToken(request.AccessToken);
        if (claims is null) return Invalid("invalid_token");
        tenantScope.TenantId = claims.TenantId;
        var account = await db.Accounts.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == claims.UserId, context.CancellationToken);
        var membership = await db.Memberships.AsNoTracking()
            .SingleOrDefaultAsync(x => x.AccountId == claims.UserId &&
                x.Status == MembershipStatus.Active, context.CancellationToken);
        var tenant = await db.Tenants.AsNoTracking()
            .SingleOrDefaultAsync(context.CancellationToken);
        if (account is null || membership is null || tenant is null ||
            account.SessionVersion != claims.SessionVersion ||
            membership.Role.ToString() != claims.Role) return Invalid("session_revoked");
        if (account.MustChangePassword || membership.MustChangePassword ||
            claims.MustChangePassword ||
            account.PasswordChangedAtUtc < tenant.PasswordPolicyUpdatedAtUtc ||
            account.PasswordChangedAtUtc < DateTime.UtcNow.AddDays(-tenant.PasswordExpiryDays))
            return Invalid("password_change_required");

        return new ValidateAccessTokenResponse
        {
            IsValid = true, SubjectId = account.Id.ToString(),
            TenantId = membership.TenantId.ToString(), TokenId = claims.TokenId,
            SessionVersion = account.SessionVersion,
            ExpiresAtUtc = Timestamp.FromDateTimeOffset(claims.ExpiresAtUtc),
            MustChangePassword = false,
            Roles = { membership.Role.ToString() }
        };
    }

    public override Task<LookupProfileResponse> LookupProfile(
        LookupProfileRequest request, ServerCallContext context)
        => Task.FromResult(new LookupProfileResponse { Found = false });

    private static ValidateAccessTokenResponse Invalid(string reason) => new()
    {
        IsValid = false, FailureReason = reason
    };
}
