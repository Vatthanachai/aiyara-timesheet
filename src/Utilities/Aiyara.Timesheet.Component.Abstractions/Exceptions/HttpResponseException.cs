namespace Aiyara.Timesheet.Component.Abstractions.Exceptions;

/// <summary>
/// Http response exception
/// </summary>
public class HttpResponseException : System.Exception
{
    /// <summary>
    /// Status
    /// </summary>
    public int Status { get; set; } = 500;

    /// <summary>
    /// Value of exception
    /// </summary>
    public object Value { get; set; }
}