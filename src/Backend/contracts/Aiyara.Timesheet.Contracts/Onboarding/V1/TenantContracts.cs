namespace Aiyara.Timesheet.Contracts.Onboarding.V1;

public sealed record CreateTenantRequest(string Name, string Slug, string AdminEmail);

public sealed record CreateTenantResponse(
    Guid TenantId,
    Guid AccountId,
    Guid MembershipId,
    string Role,
    string Status,
    string TimeZoneId,
    string OnboardingKey,
    DateTime OnboardingKeyExpiresAtUtc)
{
    public override string ToString() =>
        $"CreateTenantResponse {{ TenantId = {TenantId}, OnboardingKey = [REDACTED] }}";
}

public sealed record AcceptInvitationRequest(string Code)
{
    public override string ToString() => "AcceptInvitationRequest { Code = [REDACTED] }";
}

public sealed record IssueInvitationRequest(string Email, string Role);

public sealed record IssueInvitationResponse(Guid InvitationId, Guid TenantId,
    DateTime ExpiresAtUtc, string Code)
{
    public override string ToString() =>
        $"IssueInvitationResponse {{ InvitationId = {InvitationId}, Code = [REDACTED] }}";
}

public sealed record AcceptInvitationResponse(
    Guid TenantId,
    Guid AccountId,
    Guid MembershipId,
    string Role,
    string Status);
