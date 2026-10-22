using Aiyara.Timesheet.Component.Abstractions.Controllers;
using Aiyara.Timesheet.Component.Abstractions.ServiceResponses;
using Aiyara.Timesheet.Databases;
using Microsoft.AspNetCore.Mvc;

namespace Aiyara.Timesheet.Api.Controllers;

[Route("api/v1")]
public sealed class TimesheetController(TimesheetOperations operations) : BaseController
{
    [HttpGet("context", Name = "GetTimesheetContextV1")]
    [ProducesResponseType(typeof(ServiceOkResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    public Task<IActionResult> Context(CancellationToken ct) => Execute(actor => operations.Context(actor, ct));

    [HttpGet("catalog", Name = "GetTimesheetCatalogV1")]
    [ProducesResponseType(typeof(ServiceOkResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    public Task<IActionResult> Catalog(CancellationToken ct) => Execute(actor => operations.Catalog(actor, ct));

    [HttpPost("projects")]
    [ProducesResponseType(typeof(ServiceOkResponse<Project>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status403Forbidden)]
    public Task<IActionResult> CreateProject([FromBody] NameRequest body, CancellationToken ct) =>
        Execute(actor => operations.SaveCatalog(actor, body, "project", ct));

    [HttpPost("categories")]
    [ProducesResponseType(typeof(ServiceOkResponse<Category>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status403Forbidden)]
    public Task<IActionResult> CreateCategory([FromBody] NameRequest body, CancellationToken ct) =>
        Execute(actor => operations.SaveCatalog(actor, body, "category", ct));

    [HttpPut("projects/{id:guid}")]
    [ProducesResponseType(typeof(ServiceOkResponse<Project>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status404NotFound)]
    public Task<IActionResult> UpdateProject(Guid id, [FromBody] NameRequest body, CancellationToken ct) =>
        Execute(actor => operations.UpdateCatalog(actor, id, body, "project", ct));

    [HttpPut("categories/{id:guid}")]
    [ProducesResponseType(typeof(ServiceOkResponse<Category>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status404NotFound)]
    public Task<IActionResult> UpdateCategory(Guid id, [FromBody] NameRequest body, CancellationToken ct) =>
        Execute(actor => operations.UpdateCatalog(actor, id, body, "category", ct));

    [HttpDelete("projects/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status404NotFound)]
    public Task<IActionResult> DeleteProject(Guid id, CancellationToken ct) =>
        Execute(actor => operations.DeleteCatalog(actor, id, "project", ct));

    [HttpDelete("categories/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status404NotFound)]
    public Task<IActionResult> DeleteCategory(Guid id, CancellationToken ct) =>
        Execute(actor => operations.DeleteCatalog(actor, id, "category", ct));

    [HttpPost("holidays", Name = "CreateHolidayV1")]
    [ProducesResponseType(typeof(ServiceOkResponse<Holiday>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status403Forbidden)]
    public Task<IActionResult> CreateHoliday([FromBody] HolidayRequest body, CancellationToken ct) =>
        Execute(actor => operations.CreateHoliday(actor, body, ct));

    [HttpPut("holidays/{id:guid}", Name = "UpdateHolidayV1")]
    [ProducesResponseType(typeof(ServiceOkResponse<Holiday>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status404NotFound)]
    public Task<IActionResult> UpdateHoliday(Guid id, [FromBody] HolidayRequest body,
        CancellationToken ct) => Execute(actor => operations.UpdateHoliday(actor, id, body, ct));

    [HttpDelete("holidays/{id:guid}", Name = "DeleteHolidayV1")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status404NotFound)]
    public Task<IActionResult> DeleteHoliday(Guid id, CancellationToken ct) =>
        Execute(actor => operations.DeleteHoliday(actor, id, ct));

    /// <summary>Tenant Admin only: sets the organization's working weekdays (Sunday = 0).</summary>
    [HttpPut("work-week", Name = "UpdateWorkWeekV1")]
    [ProducesResponseType(typeof(ServiceOkResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status403Forbidden)]
    public Task<IActionResult> UpdateWorkWeek([FromBody] WorkWeekRequest body, CancellationToken ct) =>
        Execute(actor => operations.SaveWorkWeek(actor, body, ct));

    [HttpGet("tasks", Name = "GetPersonalTasksV1")]
    [ProducesResponseType(typeof(ServiceOkResponse<IReadOnlyList<PersonalTask>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    public Task<IActionResult> Tasks(CancellationToken ct) => Execute(actor => operations.Tasks(actor, ct));

    [HttpPost("tasks", Name = "CreatePersonalTaskV1")]
    [ProducesResponseType(typeof(ServiceOkResponse<PersonalTask>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    public Task<IActionResult> CreateTask([FromBody] TaskRequest body, CancellationToken ct) =>
        Execute(actor => operations.CreateTask(actor, body, ct));

    [HttpPut("tasks/{id:guid}", Name = "UpdatePersonalTaskV1")]
    [ProducesResponseType(typeof(ServiceOkResponse<PersonalTask>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status404NotFound)]
    public Task<IActionResult> UpdateTask(Guid id, [FromBody] TaskRequest body, CancellationToken ct) =>
        Execute(actor => operations.UpdateTask(actor, id, body, ct));

    [HttpDelete("tasks/{id:guid}", Name = "DeletePersonalTaskV1")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status404NotFound)]
    public Task<IActionResult> DeleteTask(Guid id, CancellationToken ct) =>
        Execute(actor => operations.DeleteTask(actor, id, ct));

    [HttpGet("entries", Name = "GetTimeEntriesV1")]
    [ProducesResponseType(typeof(ServiceOkResponse<IReadOnlyList<TimeEntry>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    public Task<IActionResult> Entries([FromQuery] int year, [FromQuery] int month, CancellationToken ct) =>
        Execute(actor => operations.Entries(actor, year, month, ct));

    [HttpPost("entries", Name = "CreateTimeEntryV1")]
    [ProducesResponseType(typeof(ServiceOkResponse<TimeEntry>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status409Conflict)]
    public Task<IActionResult> CreateEntry([FromBody] EntryRequest body, CancellationToken ct) =>
        Execute(actor => operations.SaveEntry(actor, null, body, ct));

    [HttpPut("entries/{id:guid}", Name = "UpdateTimeEntryV1")]
    [ProducesResponseType(typeof(ServiceOkResponse<TimeEntry>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status409Conflict)]
    public Task<IActionResult> UpdateEntry(Guid id, [FromBody] EntryRequest body, CancellationToken ct) =>
        Execute(actor => operations.SaveEntry(actor, id, body, ct));

    [HttpDelete("entries/{id:guid}", Name = "DeleteTimeEntryV1")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status409Conflict)]
    public Task<IActionResult> DeleteEntry(Guid id, [FromQuery] string? reason, CancellationToken ct) =>
        Execute(actor => operations.DeleteEntry(actor, id, reason, ct));

    [HttpGet("leave", Name = "GetLeaveEntriesV1")]
    [ProducesResponseType(typeof(ServiceOkResponse<IReadOnlyList<LeaveEntry>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    public Task<IActionResult> Leaves([FromQuery] int year, [FromQuery] int month, CancellationToken ct) =>
        Execute(actor => operations.Leaves(actor, year, month, ct));

    [HttpPost("leave", Name = "CreateLeaveEntryV1")]
    [ProducesResponseType(typeof(ServiceOkResponse<LeaveEntry>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status409Conflict)]
    public Task<IActionResult> CreateLeave([FromBody] LeaveRequest body, CancellationToken ct) =>
        Execute(actor => operations.SaveLeave(actor, null, body, ct));

    [HttpPut("leave/{id:guid}", Name = "UpdateLeaveEntryV1")]
    [ProducesResponseType(typeof(ServiceOkResponse<LeaveEntry>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status409Conflict)]
    public Task<IActionResult> UpdateLeave(Guid id, [FromBody] LeaveRequest body, CancellationToken ct) =>
        Execute(actor => operations.SaveLeave(actor, id, body, ct));

    [HttpDelete("leave/{id:guid}", Name = "DeleteLeaveEntryV1")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status409Conflict)]
    public Task<IActionResult> DeleteLeave(Guid id, CancellationToken ct) =>
        Execute(actor => operations.DeleteLeave(actor, id, ct));

    [HttpPost("months/{year:int}/{month:int}/lock", Name = "LockTimesheetMonthV1")]
    [ProducesResponseType(typeof(ServiceOkResponse<MonthLock>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status409Conflict)]
    public Task<IActionResult> LockMonth(int year, int month, CancellationToken ct) =>
        Execute(actor => operations.LockMonth(actor, year, month, ct));

    [HttpGet("months/{year:int}/{month:int}/snapshot", Name = "GetTimesheetMonthSnapshotV1")]
    [ProducesResponseType(typeof(ServiceOkResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status404NotFound)]
    public Task<IActionResult> Snapshot(int year, int month, [FromQuery] Guid? ownerId, CancellationToken ct) =>
        Execute(actor => operations.Snapshot(actor, year, month, ownerId, ct));

    /// <summary>The signed-in member's month: approval status, return reason, lock, and whether it can be edited.</summary>
    [HttpGet("me/months/{year:int}/{month:int}", Name = "GetMyTimesheetMonthV1")]
    [ProducesResponseType(typeof(ServiceOkResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    public Task<IActionResult> MyMonth(int year, int month, CancellationToken ct) =>
        Execute(actor => operations.MyMonth(actor, year, month, ct));

    /// <summary>Submits the signed-in member's open or returned month for approval.</summary>
    [HttpPost("me/months/{year:int}/{month:int}/submit", Name = "SubmitTimesheetMonthV1")]
    [ProducesResponseType(typeof(ServiceOkResponse<MonthApproval>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status409Conflict)]
    public Task<IActionResult> SubmitMonth(int year, int month, CancellationToken ct) =>
        Execute(actor => operations.SubmitMonth(actor, year, month, ct));

    /// <summary>Tenant Admin only: approves a member's submitted month and takes its snapshot.</summary>
    [HttpPost("members/{ownerId:guid}/months/{year:int}/{month:int}/approve", Name = "ApproveTimesheetMonthV1")]
    [ProducesResponseType(typeof(ServiceOkResponse<MonthApproval>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status409Conflict)]
    public Task<IActionResult> ApproveMonth(Guid ownerId, int year, int month, CancellationToken ct) =>
        Execute(actor => operations.ApproveMonth(actor, ownerId, year, month, ct));

    /// <summary>Tenant Admin only: returns a member's submitted month with a reason and emails them.</summary>
    [HttpPost("members/{ownerId:guid}/months/{year:int}/{month:int}/return", Name = "ReturnTimesheetMonthV1")]
    [ProducesResponseType(typeof(ServiceOkResponse<MonthApproval>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status409Conflict)]
    public Task<IActionResult> ReturnMonth(Guid ownerId, int year, int month, [FromBody] ReturnRequest body,
        CancellationToken ct) => Execute(actor => operations.ReturnMonth(actor, ownerId, year, month, body, ct));

    /// <summary>Tenant Admin only: hours, entry count, and leave days per member for a month.</summary>
    [HttpGet("members/summary", Name = "GetMemberTimesheetSummaryV1")]
    [ProducesResponseType(typeof(ServiceOkResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status403Forbidden)]
    public Task<IActionResult> MemberSummary([FromQuery] int year, [FromQuery] int month, CancellationToken ct) =>
        Execute(actor => operations.MemberSummary(actor, year, month, ct));

    /// <summary>Tenant Admin only: one member's entries, leave, and the month's holidays.</summary>
    [HttpGet("members/{ownerId:guid}/timesheet", Name = "GetMemberTimesheetV1")]
    [ProducesResponseType(typeof(ServiceOkResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status403Forbidden)]
    public Task<IActionResult> MemberTimesheet(Guid ownerId, [FromQuery] int year, [FromQuery] int month,
        CancellationToken ct) => Execute(actor => operations.MemberTimesheet(actor, ownerId, year, month, ct));

    /// <summary>The signed-in member's own entries, leave, holidays, and working days for a month.</summary>
    [HttpGet("me/timesheet", Name = "GetMyTimesheetV1")]
    [ProducesResponseType(typeof(ServiceOkResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    public Task<IActionResult> MyTimesheet([FromQuery] int year, [FromQuery] int month, CancellationToken ct) =>
        Execute(actor => operations.MyTimesheet(actor, year, month, ct));

    private async Task<IActionResult> Execute(Func<Actor, Task<ServiceResponse>> action)
    {
        if (!Actor.TryRead(HttpContext, out var actor)) return ReturnResponseWithHttpStatus(
            new ServiceErrorResponse(401, "UNAUTHORIZED", "Authentication is required."));
        return ReturnResponseWithHttpStatus(await action(actor));
    }
}
