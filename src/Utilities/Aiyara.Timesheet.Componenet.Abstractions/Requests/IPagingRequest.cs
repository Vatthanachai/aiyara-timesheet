namespace Aiyara.Timesheet.Component.Abstractions.Requests;

/// <summary>
/// Interface for paging request.
/// </summary>
public interface IPagingRequest : IOrderDescending, IPageIndex, IPageLimit;