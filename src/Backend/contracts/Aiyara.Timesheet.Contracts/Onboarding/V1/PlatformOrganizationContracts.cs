namespace Aiyara.Timesheet.Contracts.Onboarding.V1;

public sealed record PlatformOrganizationAdminSummary(string Email, string Status);

public sealed record PlatformOrganizationSummary(
    Guid TenantId,
    string OrganizationCode,
    string Name,
    string Slug,
    string Status,
    DateTime CreatedAtUtc,
    IReadOnlyList<PlatformOrganizationAdminSummary> Admins);

public sealed record PlatformOrganizationListResponse(
    int TotalCount,
    int Page,
    int PageSize,
    IReadOnlyList<PlatformOrganizationSummary> Items);

public sealed record UpdatePlatformOrganizationRequest(string Name, string Slug, string? Reason);

public sealed record SetPlatformOrganizationStatusRequest(bool Suspended, string? Reason);

public sealed record CreatePlatformOrganizationRequest(string Name, string Slug, string AdminEmail);

public sealed record InvitePlatformOrganizationAdminRequest(string Email);

public sealed record RequestPlatformAdministratorActivationRequest(string Email);

public sealed record PlatformOrganizationUpdateResult(
    Guid TenantId,
    string OrganizationCode,
    string Name,
    string Slug,
    string Status);
