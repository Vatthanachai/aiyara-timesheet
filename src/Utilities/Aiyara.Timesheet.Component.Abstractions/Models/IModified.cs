namespace Aiyara.Timesheet.Component.Abstractions.Models;

public interface IModified
{
    string? ModifiedBy { get; set; }
    DateTime? ModifiedDate { get; set; }
}