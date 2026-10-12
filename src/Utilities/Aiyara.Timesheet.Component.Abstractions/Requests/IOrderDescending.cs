namespace Aiyara.Timesheet.Component.Abstractions.Requests;

/// <summary>
/// Interface for order descending.
/// </summary>
public interface IOrderDescending
{
    /// <summary>
    /// Gets or sets a value indicating whether the order is descending.
    /// </summary>
    bool IsOrderDescending { get; set; }
}