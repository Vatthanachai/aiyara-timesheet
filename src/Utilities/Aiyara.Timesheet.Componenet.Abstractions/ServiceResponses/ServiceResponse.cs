namespace Aiyara.Timesheet.Component.Abstractions.ServiceResponses;

public abstract class ServiceResponse(int statusCode, string statusMessage = "")
{
    public int StatusCode { get; } = statusCode;
    public string StatusMessage { get; } = statusMessage;
    [System.Text.Json.Serialization.JsonIgnore]
    public string? Location { get; protected set; }
}
