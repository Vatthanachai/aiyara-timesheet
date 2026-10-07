namespace Aiyara.Timesheet.Component.Abstractions.ServiceResponses;

public sealed class ServiceErrorResponse(
    int statusCode,
    string errorCode,
    string statusMessage = "The request could not be completed.",
    IReadOnlyDictionary<string, string[]>? errors = null,
    string? traceId = null) : ServiceResponse(statusCode, statusMessage)
{
    public string ErrorCode { get; init; } = errorCode;
    public IReadOnlyDictionary<string, string[]>? Errors { get; init; } = errors;
    public string? TraceId { get; init; } = traceId;
}
