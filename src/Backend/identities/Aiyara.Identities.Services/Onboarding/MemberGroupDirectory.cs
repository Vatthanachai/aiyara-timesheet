using Aiyara.Identities.Databases;
using Microsoft.EntityFrameworkCore;

namespace Aiyara.Identities.Services.Onboarding;

/// <summary>Service-to-service lookup of a member group, for reports that follow a group over
/// time. Members in every status are included, since a deactivated member's past hours still
/// belong in a report about the period they worked.</summary>
public static class MemberGroupDirectory
{
    public static async Task<(string Name, IReadOnlyList<Guid> MemberIds)?> FindAsync(IdentityDbContext db,
        TenantScope tenantScope, Guid tenantId, Guid groupId, CancellationToken cancellationToken)
    {
        tenantScope.TenantId = tenantId;
        var group = await db.MemberGroups.AsNoTracking().SingleOrDefaultAsync(x => x.Id == groupId,
            cancellationToken);
        if (group is null) return null;
        var members = await db.Memberships.AsNoTracking().Where(x => x.MemberGroupId == groupId)
            .Select(x => x.AccountId).OrderBy(x => x).ToListAsync(cancellationToken);
        return (group.Name, members);
    }
}
