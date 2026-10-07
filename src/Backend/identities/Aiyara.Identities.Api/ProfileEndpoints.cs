using Aiyara.Identities.Databases;
using Aiyara.Identities.Models.Tenancy;
using Aiyara.Timesheet.Component.Abstractions.Securities;
using Microsoft.EntityFrameworkCore;

namespace Aiyara.Identities.Api;

public static class ProfileEndpoints
{
    public static void MapProfileEndpoints(this WebApplication app)
    {
        app.MapGet("/api/v1/tenants/{tenantId:guid}/password-policy", async (Guid tenantId,
            HttpContext context, IdentityDbContext db, IPasetoTokenService tokens,
            TenantScope scope) =>
        {
            var account = await AuthenticatedAccount(context, db, tokens, scope);
            if (account is null || scope.TenantId != tenantId) return Results.Unauthorized();
            var membership = await db.Memberships.SingleAsync(x => x.AccountId == account.Id);
            if (membership.Role != TenantRole.TenantAdmin)
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            var tenant = await db.Tenants.SingleAsync();
            return Results.Ok(new { minimumLength = tenant.PasswordMinimumLength,
                expiryDays = tenant.PasswordExpiryDays, requireUppercase = tenant.PasswordRequireUppercase,
                requireLowercase = tenant.PasswordRequireLowercase, requireDigit = tenant.PasswordRequireDigit,
                requireSymbol = tenant.PasswordRequireSymbol });
        }).WithName("GetTenantPasswordPolicyV1").WithTags("Tenant administration");
        var group = app.MapGroup("/api/v1/profile").WithTags("Profile");
        group.MapGet("/me", async (HttpContext context, IdentityDbContext db,
            IPasetoTokenService tokens, TenantScope scope) =>
        {
            var account = await AuthenticatedAccount(context, db, tokens, scope);
            return account is null ? Results.Unauthorized() : Results.Ok(View(account));
        }).WithName("GetMyProfileV1");
        group.MapPut("/me", async (HttpContext context, IdentityDbContext db,
            IPasetoTokenService tokens, TenantScope scope, ProfileRequest body) =>
        {
            var account = await AuthenticatedAccount(context, db, tokens, scope);
            if (account is null) return Results.Unauthorized();
            if (string.IsNullOrWhiteSpace(body.FirstName) || body.FirstName.Length > 100 ||
                string.IsNullOrWhiteSpace(body.LastName) || body.LastName.Length > 100 ||
                body.JobTitle?.Length > 160 || body.PhotoUrl?.Length > 2048 ||
                body.PhotoUrl is not null &&
                (!Uri.TryCreate(body.PhotoUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps))
                return Results.BadRequest("Invalid profile fields.");
            account.FirstName = body.FirstName.Trim();
            account.LastName = body.LastName.Trim();
            account.JobTitle = body.JobTitle?.Trim();
            account.PhotoUrl = body.PhotoUrl;
            await db.SaveChangesAsync();
            return Results.Ok(View(account));
        }).WithName("UpdateMyProfileV1");
    }

    private static async Task<Account?> AuthenticatedAccount(HttpContext context, IdentityDbContext db,
        IPasetoTokenService tokens, TenantScope scope)
    {
        var authorization = context.Request.Headers.Authorization.ToString();
        var claims = authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? tokens.ValidateToken(authorization[7..].Trim()) : null;
        if (claims is null || claims.MustChangePassword) return null;
        scope.TenantId = claims.TenantId;
        var membership = await db.Memberships.SingleOrDefaultAsync(x => x.AccountId == claims.UserId &&
            x.Status == MembershipStatus.Active);
        if (membership is null || membership.Role.ToString() != claims.Role) return null;
        var account = await db.Accounts.SingleOrDefaultAsync(x => x.Id == claims.UserId);
        return account is not null && account.SessionVersion == claims.SessionVersion &&
            !account.MustChangePassword && !membership.MustChangePassword ? account : null;
    }

    private static object View(Account account) => new
    {
        account.Id, account.Email, account.FirstName, account.LastName,
        account.PhotoUrl, account.JobTitle
    };
}

public sealed record ProfileRequest(string FirstName, string LastName, string? PhotoUrl, string? JobTitle);
