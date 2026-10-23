namespace Aiyara.Timesheet.Contracts.Onboarding.V1;

/// <summary>One member of the caller's organization. Id is the account ID. Role is TenantAdmin or
/// Employee; Status is Active, PendingActivation, or Deactivated. GroupId and GroupName are null
/// for a member in no group. MfaEnabled is true when the member has a confirmed authenticator.</summary>
public sealed record TenantMemberEntry(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    string Role,
    string Status,
    bool IsCurrentUser,
    Guid? GroupId,
    string? GroupName,
    bool MfaEnabled = false);

/// <summary>An invitation that has been neither accepted nor revoked and has not expired.
/// GroupId and GroupName are the group the invitee joins, or null.</summary>
public sealed record TenantInvitationEntry(
    Guid Id,
    string Email,
    string Role,
    string? InvitedByEmail,
    DateTime CreatedAtUtc,
    DateTime ExpiresAtUtc,
    Guid? GroupId,
    string? GroupName);

public sealed record ChangeTenantMemberRoleRequest(string Role);

public sealed record SetTenantMemberStatusRequest(bool Active);

/// <summary>Turns the organization's MFA requirement on or off. Reason (optional, at most 500
/// characters) is kept in the audit log.</summary>
public sealed record SetTenantMfaRequirementRequest(bool Required, string? Reason);

/// <summary>Clears a member's authenticator and recovery codes (a lost device). Reason (optional, at
/// most 500 characters) is kept in the audit log.</summary>
public sealed record ResetTenantMemberMfaRequest(string? Reason);

/// <summary>A member group of the caller's organization and how many members are in it.</summary>
public sealed record TenantMemberGroupEntry(Guid Id, string Name, int MemberCount);

/// <summary>Creates or renames a member group. Name is 1–100 characters and unique in the
/// organization, ignoring case.</summary>
public sealed record SaveTenantMemberGroupRequest(string Name);

/// <summary>Puts a member in a group, or in no group when GroupId is null.</summary>
public sealed record SetTenantMemberGroupRequest(Guid? GroupId);

/// <summary>One member-management action in the caller's organization. Action is a
/// <c>member.*</c> code; Before/After hold the changed fields as JSON and never contain
/// credentials, codes, or keys. Platform Admin actions on the organization are not included.</summary>
public sealed record TenantAuditEntry(
    Guid Id,
    DateTime CreatedAtUtc,
    string Action,
    string? ActorEmail,
    string? BeforeJson,
    string? AfterJson);

public sealed record TenantAuditListResponse(
    int TotalCount,
    int Page,
    int PageSize,
    IReadOnlyList<TenantAuditEntry> Items);
