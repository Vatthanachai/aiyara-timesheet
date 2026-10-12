namespace Aiyara.Timesheet.Component.Abstractions.ServiceResponses;

public sealed class ServiceOkResponse<T> : ServiceResponse
{
    public ServiceOkResponse(T result, int statusCode = 200, string? location = null,
        string statusMessage = "") : base(statusCode, statusMessage)
    {
        Result = result;
        Location = location;
    }

    public T Result { get; }
}
