namespace Aiyara.Timesheet.Component.Abstractions.Requests;

/// <summary>
/// Interface for page limit.
/// </summary>
public interface IPageLimit
{
    /// <summary>
    /// Gets or sets the page limit.
    /// </summary>
    int PageLimit { get; set; }
}