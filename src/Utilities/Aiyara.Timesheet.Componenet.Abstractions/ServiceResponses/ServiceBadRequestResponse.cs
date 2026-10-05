namespace Aiyara.Timesheet.Component.Abstractions.ServiceResponses;

public class ServiceBadRequestResponse(
    string statusCode = default,
    string statusMessage = default,
    Exception exception = default) : ServiceResponse
{
    public string StatusCode { get; set; } = statusCode ?? string.Empty;
    public string StatusMessage { get; set; } = statusMessage ?? (exception?.Message ?? string.Empty);
    public Exception Exception { get; set; } = exception ?? new Exception();
}