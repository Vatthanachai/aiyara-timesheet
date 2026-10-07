namespace Aiyara.Timesheet.Contracts.Onboarding.V1;

public sealed record PlatformOrganizationAdminSummary(string Email, string Status);

public sealed record PlatformOrganizationSummary(
    Guid TenantId,
    string OrganizationCode,
    string Name,
    string Slug,
    DateTime CreatedAtUtc,
    IReadOnlyList<PlatformOrganizationAdminSummary> Admins);

public sealed record PlatformOrganizationListResponse(
    int TotalCount,
    int Page,
    int PageSize,
    IReadOnlyList<PlatformOrganizationSummary> Items);
