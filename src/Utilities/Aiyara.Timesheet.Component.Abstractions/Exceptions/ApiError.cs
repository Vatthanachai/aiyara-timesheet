using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Aiyara.Timesheet.Component.Abstractions.Exceptions;

public class ApiError
{
    public ApiError()
    {
    }

    public ApiError(string message)
    {
        Message = message;
    }

    public ApiError(ModelStateDictionary modelState)
    {
        Message = "Invalid parameters.";
        var modelStateError = modelState
            .FirstOrDefault(x => x.Value != null && x.Value.Errors.Any());

        if (modelStateError.Value != null) Detail = modelStateError.Value.Errors.FirstOrDefault()?.ErrorMessage;
    }

    public string Message { get; set; } = null!;

    public string? Detail { get; set; }
}