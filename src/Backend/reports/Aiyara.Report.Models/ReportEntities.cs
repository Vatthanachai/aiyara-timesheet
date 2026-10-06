namespace Aiyara.Report.Models;

public enum ReportKind { Weekly, Monthly, Annual, Performance }
public enum ReportFormat { Pdf, Xlsx }
public enum ReportRunStatus { Queued, Running, Succeeded, Failed }

public sealed class ReportDefinition
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public ReportKind Kind { get; set; }
    public ReportFormat Format { get; set; }
    public required string Name { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class ReportSchedule
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ReportDefinitionId { get; set; }
    public required string TimeZoneId { get; set; }
    public TimeOnly LocalTime { get; set; } = new(0, 15);
    public DateTime? NextFireAtUtc { get; set; }
    public DateTime? LastFireAtUtc { get; set; }
    public bool IsEnabled { get; set; } = true;
}

public sealed class ReportRun
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ReportDefinitionId { get; set; }
    public required string IdempotencyKey { get; set; }
    public ReportRunStatus Status { get; set; } = ReportRunStatus.Queued;
    public DateTime PeriodStartUtc { get; set; }
    public DateTime PeriodEndUtc { get; set; }
    public int AttemptCount { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
}

public sealed class ReportSnapshot
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ReportRunId { get; set; }
    public required string PayloadJson { get; set; }
    public required string Sha256 { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class ReportObject
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ReportRunId { get; set; }
    public required string ObjectKey { get; set; }
    public required string Sha256 { get; set; }
    public required string ContentType { get; set; }
    public long LengthBytes { get; set; }
    public int Version { get; set; } = 1;
    public bool IsExternallySigned { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime RetainUntilUtc { get; set; }
}

public sealed class ReportRetentionPolicy
{
    public Guid TenantId { get; set; }
    public int Years { get; set; } = 7;
    public DateTime UpdatedAtUtc { get; set; }
}
