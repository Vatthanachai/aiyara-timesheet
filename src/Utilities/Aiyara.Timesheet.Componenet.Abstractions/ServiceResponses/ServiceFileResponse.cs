namespace Aiyara.Timesheet.Component.Abstractions.ServiceResponses;

public sealed class ServiceFileResponse(byte[] content, string contentType, string fileName) : ServiceResponse(200)
{
    [System.Text.Json.Serialization.JsonIgnore] public byte[] Content { get; } = content;
    [System.Text.Json.Serialization.JsonIgnore] public string ContentType { get; } = contentType;
    [System.Text.Json.Serialization.JsonIgnore] public string FileName { get; } = fileName;
}
