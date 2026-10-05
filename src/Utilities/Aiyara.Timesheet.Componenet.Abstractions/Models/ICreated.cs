namespace Aiyara.Timesheet.Component.Abstractions.Models;

public interface ICreated
{
    string CreatedBy { get; set; }
    DateTime CreatedOn { get; set; }
}