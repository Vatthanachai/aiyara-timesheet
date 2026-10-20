using Aiyara.Timesheet.Contracts.Identity.V1;

namespace Aiyara.Report.Services;

/// <summary>A tenant's member group and the accounts in it now.</summary>
public sealed record ReportGroup(string Name, IReadOnlyList<Guid> MemberIds);

/// <summary>Member groups live in Identity; Report asks for one when a group schedule is created
/// and each time it fires, so a scheduled report follows the group's current members.</summary>
public interface IReportGroupDirectory
{
    /// <summary>The group, or null when the tenant has no such group (for example it was deleted).</summary>
    Task<ReportGroup?> FindAsync(Guid tenantId, Guid groupId, CancellationToken cancellationToken);
}

public sealed class GrpcReportGroupDirectory(IdentityValidationService.IdentityValidationServiceClient identity)
    : IReportGroupDirectory
{
    public async Task<ReportGroup?> FindAsync(Guid tenantId, Guid groupId, CancellationToken cancellationToken)
    {
        var response = await identity.ListGroupMembersAsync(new ListGroupMembersRequest
        {
            TenantId = tenantId.ToString(), GroupId = groupId.ToString(), CorrelationId = Guid.NewGuid().ToString()
        }, cancellationToken: cancellationToken);
        return response.Found
            ? new ReportGroup(response.GroupName, response.MemberIds.Select(Guid.Parse).ToArray())
            : null;
    }
}
