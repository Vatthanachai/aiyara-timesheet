using Newtonsoft.Json;

namespace Aiyara.Timesheet.Component.Abstractions.ServiceResponses;

[Serializable, JsonObject]
public class ServiceOkResponse<T>(T result) : ServiceResponse
{
    public string? StatusCode { get; set; } = string.Empty;
    public string? StatusMessage { get; set; } = string.Empty;
    public T Result { get; set; } = result;
}