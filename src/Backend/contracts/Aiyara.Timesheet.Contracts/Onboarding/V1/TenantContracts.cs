namespace Aiyara.Timesheet.Contracts.Onboarding.V1;

public sealed record CreateTenantRequest(string Name, string Slug, string AdminEmail);

public sealed record CreateTenantResponse(
    Guid TenantId,
    Guid AccountId,
    Guid MembershipId,
    string Role,
    string Status,
    string TimeZoneId);

public sealed record AcceptInvitationRequest(string Code);

public sealed record IssueInvitationRequest(string Email, string Role);

public sealed record IssueInvitationResponse(Guid InvitationId, Guid TenantId,
    DateTime ExpiresAtUtc, string Code);

public sealed record AcceptInvitationResponse(
    Guid TenantId,
    Guid AccountId,
    Guid MembershipId,
    string Role,
    string Status);
