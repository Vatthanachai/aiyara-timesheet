namespace Aiyara.Timesheet.Contracts.Onboarding.V1;

/// <summary>One member of the caller's organization. Id is the account ID. Role is TenantAdmin or
/// Employee; Status is Active, PendingActivation, or Deactivated. GroupId and GroupName are null
/// for a member in no group.</summary>
public sealed record TenantMemberEntry(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    string Role,
    string Status,
    bool IsCurrentUser,
    Guid? GroupId,
    string? GroupName);

/// <summary>An invitation that has been neither accepted nor revoked and has not expired.</summary>
public sealed record TenantInvitationEntry(
    Guid Id,
    string Email,
    string Role,
    string? InvitedByEmail,
    DateTime CreatedAtUtc,
    DateTime ExpiresAtUtc);

public sealed record ChangeTenantMemberRoleRequest(string Role);

public sealed record SetTenantMemberStatusRequest(bool Active);

/// <summary>A member group of the caller's organization and how many members are in it.</summary>
public sealed record TenantMemberGroupEntry(Guid Id, string Name, int MemberCount);

/// <summary>Creates or renames a member group. Name is 1–100 characters and unique in the
/// organization, ignoring case.</summary>
public sealed record SaveTenantMemberGroupRequest(string Name);

/// <summary>Puts a member in a group, or in no group when GroupId is null.</summary>
public sealed record SetTenantMemberGroupRequest(Guid? GroupId);
