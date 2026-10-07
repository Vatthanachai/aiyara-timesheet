using Aiyara.Identities.Databases;
using Aiyara.Identities.Models.Tenancy;
using Aiyara.Identities.Services.Authentication;
using Aiyara.Identities.Services.Onboarding;
using Aiyara.Timesheet.Component.Abstractions.Securities;
using Aiyara.Timesheet.Contracts.Onboarding.V1;
using Microsoft.EntityFrameworkCore;

namespace Aiyara.Identities.Api;

public static class PlatformOrganizationEndpoints
{
    public static IEndpointRouteBuilder MapPlatformOrganizationEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/platform/organizations", ListOrganizationsAsync)
            .WithName("ListPlatformOrganizationsV1")
            .WithTags("Platform administration")
            .Produces<PlatformOrganizationListResponse>()
            .Produces(StatusCodes.Status401Unauthorized);
        return endpoints;
    }

    private static async Task<IResult> ListOrganizationsAsync(HttpContext context,
        IdentityDbContext db, TenantScope tenantScope, IPasetoTokenService tokens,
        PlatformOrganizationService organizations, string? search, int page = 1,
        int pageSize = 25, CancellationToken cancellationToken = default)
    {
        var authorization = context.Request.Headers.Authorization.ToString();
        var claims = authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? tokens.ValidateToken(authorization[7..].Trim()) : null;
        if (claims is null || claims.MustChangePassword ||
            claims.Role != TenantRole.PlatformAdmin.ToString() ||
            claims.TenantId != PlatformAdminBootstrap.PlatformTenantId)
            return Results.Unauthorized();

        tenantScope.TenantId = claims.TenantId;
        var account = await db.Accounts.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == claims.UserId && x.IsPlatformAdmin,
                cancellationToken);
        var membership = await db.Memberships.AsNoTracking()
            .SingleOrDefaultAsync(x => x.AccountId == claims.UserId &&
                x.Role == TenantRole.PlatformAdmin && x.Status == MembershipStatus.Active,
                cancellationToken);
        if (account is null || account.SessionVersion != claims.SessionVersion ||
            account.MustChangePassword || membership is null)
            return Results.Unauthorized();

        if (search?.Length > 100) return Results.BadRequest();
        return Results.Ok(await organizations.ListAsync(search, Math.Max(page, 1),
            Math.Clamp(pageSize, 1, 100), cancellationToken));
    }
}
