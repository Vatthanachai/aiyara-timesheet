namespace Aiyara.Timesheet.Contracts.Messaging.V1;

public static class MessageTypes
{
    public const string TenantCreated = "identity.tenant-created.v1";
    public const string InvitationIssued = "identity.invitation-issued.v1";
    public const string TenantMemberJoined = "identity.tenant-member-joined.v1";
    public const string ReportGenerationRequested = "reporting.report-generation-requested.v1";
}

public sealed record MessageEnvelope<TPayload>(
    Guid MessageId,
    Guid CorrelationId,
    string IdempotencyKey,
    Guid TenantId,
    DateTime OccurredAtUtc,
    string Type,
    TPayload Payload);

public sealed record TenantCreatedV1(Guid TenantId, string Slug, string TimeZoneId);

public sealed record InvitationIssuedV1(Guid InvitationId, Guid TenantId, string Email);

public sealed record TenantMemberJoinedV1(Guid TenantId, Guid AccountId, string Role);

public sealed record ReportGenerationRequestedV1(Guid TenantId, Guid ReportRunId, string ReportType);
