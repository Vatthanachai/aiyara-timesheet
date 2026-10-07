using Aiyara.Identities.Databases;
using Aiyara.Identities.Models.Tenancy;
using Aiyara.Timesheet.Component.Abstractions.Controllers;
using Aiyara.Timesheet.Component.Abstractions.Securities;
using Aiyara.Timesheet.Component.Abstractions.ServiceResponses;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aiyara.Identities.Api.Controllers;

[Route("api/v1")]
public sealed class ProfileController(IdentityDbContext db, IPasetoTokenService tokens,
    TenantScope scope) : BaseController
{
    [HttpGet("tenants/{tenantId:guid}/password-policy", Name = "GetTenantPasswordPolicyV1")]
    public async Task<IActionResult> PasswordPolicy(Guid tenantId, CancellationToken ct)
    {
        var account = await AuthenticatedAccount(ct);
        if (account is null || scope.TenantId != tenantId) return Error(401, "UNAUTHORIZED");
        var membership = await db.Memberships.SingleAsync(x => x.AccountId == account.Id, ct);
        if (membership.Role != TenantRole.TenantAdmin) return Error(403, "FORBIDDEN");
        var tenant = await db.Tenants.SingleAsync(ct);
        return ReturnResponseWithHttpStatus(new ServiceOkResponse<object>(new
        {
            minimumLength = tenant.PasswordMinimumLength,
            expiryDays = tenant.PasswordExpiryDays,
            requireUppercase = tenant.PasswordRequireUppercase,
            requireLowercase = tenant.PasswordRequireLowercase,
            requireDigit = tenant.PasswordRequireDigit,
            requireSymbol = tenant.PasswordRequireSymbol
        }));
    }

    [HttpGet("profile/me", Name = "GetMyProfileV1")]
    public async Task<IActionResult> GetMe(CancellationToken ct)
    {
        var account = await AuthenticatedAccount(ct);
        return account is null ? Error(401, "UNAUTHORIZED") :
            ReturnResponseWithHttpStatus(new ServiceOkResponse<object>(View(account)));
    }

    [HttpPut("profile/me", Name = "UpdateMyProfileV1")]
    public async Task<IActionResult> UpdateMe([FromBody] ProfileRequest body, CancellationToken ct)
    {
        var account = await AuthenticatedAccount(ct);
        if (account is null) return Error(401, "UNAUTHORIZED");
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(body.FirstName) || body.FirstName.Length > 100)
            errors["firstName"] = ["INVALID_VALUE"];
        if (string.IsNullOrWhiteSpace(body.LastName) || body.LastName.Length > 100)
            errors["lastName"] = ["INVALID_VALUE"];
        if (body.JobTitle?.Length > 160) errors["jobTitle"] = ["INVALID_VALUE"];
        if (body.PhotoUrl?.Length > 2048 || body.PhotoUrl is not null &&
            (!Uri.TryCreate(body.PhotoUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps))
            errors["photoUrl"] = ["INVALID_VALUE"];
        if (errors.Count > 0) return ReturnResponseWithHttpStatus(
            new ServiceErrorResponse(400, "VALIDATION_FAILED", "The request contains invalid fields.", errors));
        account.FirstName = body.FirstName.Trim();
        account.LastName = body.LastName.Trim();
        account.JobTitle = body.JobTitle?.Trim();
        account.PhotoUrl = body.PhotoUrl;
        await db.SaveChangesAsync(ct);
        return ReturnResponseWithHttpStatus(new ServiceOkResponse<object>(View(account)));
    }

    private async Task<Account?> AuthenticatedAccount(CancellationToken ct)
    {
        var authorization = Request.Headers.Authorization.ToString();
        var claims = authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? tokens.ValidateToken(authorization[7..].Trim()) : null;
        if (claims is null || claims.MustChangePassword) return null;
        scope.TenantId = claims.TenantId;
        var membership = await db.Memberships.SingleOrDefaultAsync(x => x.AccountId == claims.UserId &&
            x.Status == MembershipStatus.Active, ct);
        if (membership is null || membership.Role.ToString() != claims.Role) return null;
        var account = await db.Accounts.SingleOrDefaultAsync(x => x.Id == claims.UserId, ct);
        return account is not null && account.SessionVersion == claims.SessionVersion &&
            !account.MustChangePassword && !membership.MustChangePassword ? account : null;
    }

    private IActionResult Error(int status, string code) => ReturnResponseWithHttpStatus(
        new ServiceErrorResponse(status, code, "The request could not be completed."));

    private static object View(Account account) => new
    {
        account.Id, account.Email, account.FirstName, account.LastName,
        account.PhotoUrl, account.JobTitle
    };
}

public sealed record ProfileRequest(string FirstName, string LastName, string? PhotoUrl, string? JobTitle);
