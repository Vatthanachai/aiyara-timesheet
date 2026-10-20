namespace Aiyara.Timesheet.Contracts.Onboarding.V1;

/// <summary>One member of the caller's organization. Id is the account ID. Role is TenantAdmin or
/// Employee; Status is Active, PendingActivation, or Deactivated.</summary>
public sealed record TenantMemberEntry(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    string Role,
    string Status,
    bool IsCurrentUser);

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
