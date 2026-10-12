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

public sealed record PlatformOrganizationDetail(
    Guid TenantId,
    string OrganizationCode,
    string Name,
    string Slug,
    string Status,
    DateTime CreatedAtUtc,
    int ActiveMemberCount,
    int PendingMemberCount,
    IReadOnlyList<PlatformOrganizationAdminSummary> Admins);

/// <summary>One administrative action. Before/After hold the changed fields as JSON and never
/// contain credentials, codes, or keys.</summary>
public sealed record PlatformAuditEntry(
    Guid Id,
    DateTime CreatedAtUtc,
    string Action,
    Guid TenantId,
    string? OrganizationName,
    string? OrganizationCode,
    string? ActorEmail,
    string? Reason,
    string? BeforeJson,
    string? AfterJson);

public sealed record PlatformAuditListResponse(
    int TotalCount,
    int Page,
    int PageSize,
    IReadOnlyList<PlatformAuditEntry> Items);

public sealed record PlatformAuditQuery(
    Guid? TenantId,
    string? Actor,
    string? Action,
    DateTime? FromUtc,
    DateTime? ToUtc,
    int Page = 1,
    int PageSize = 25);

public sealed record PlatformOverviewResponse(
    int OrganizationCount,
    int ActiveCount,
    int SuspendedCount,
    int AwaitingAdministratorCount,
    IReadOnlyList<PlatformOrganizationSummary> NewestOrganizations,
    IReadOnlyList<PlatformAuditEntry> RecentActivity);
