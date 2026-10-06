using System.Security.Claims;

namespace Aiyara.Identities.Databases;

public static class TenantScopeResolver
{
    public static void Resolve(ClaimsPrincipal principal, TenantScope scope)
    {
        // Never establish a tenant scope from a caller-supplied HTTP header.
        scope.TenantId = null;
        if (principal.Identity?.IsAuthenticated == true &&
            Guid.TryParse(principal.FindFirst("tenant_id")?.Value, out var tenantId) &&
            tenantId != Guid.Empty)
        {
            scope.TenantId = tenantId;
        }
    }
}
